using System.Net;
using System.Threading.RateLimiting;
using FMSC_SMS.UpdateBroker;
using Microsoft.AspNetCore.RateLimiting;

if (args.SequenceEqual(new[] { "--self-test" }, StringComparer.Ordinal))
{
    BrokerSelfTests.Run();
    Console.WriteLine("PASS: broker credentials and GitHub redirect safety checks.");
    return;
}

var config = BrokerConfig.Load();
var store = new EnrollmentStore(config);

// Owner-only management operations are intentionally LOCAL SERVER CLI actions,
// never public HTTP endpoints. No operator credential is accepted over the API.
if (args.Length > 0)
{
    switch (args[0])
    {
        case "provision-school" when args.Length == 2:
        {
            var provisioned = await store.ProvisionSchoolAsync(args[1]);
            Console.WriteLine("School ID: " + provisioned.SchoolId);
            Console.WriteLine("School admin credential (DISPLAYED ONCE): " + provisioned.AdminCredential);
            return;
        }
        case "rotate-school-admin" when args.Length == 2 && Guid.TryParse(args[1], out var schoolId):
        {
            string? secret = await store.RotateAdminAsync(schoolId);
            if (secret is null) throw new InvalidOperationException("School not found or disabled.");
            Console.WriteLine("New admin credential (DISPLAYED ONCE): " + secret);
            return;
        }
        case "disable-school" when args.Length == 2 && Guid.TryParse(args[1], out var schoolId):
        {
            if (!await store.DisableSchoolAsync(schoolId))
                throw new InvalidOperationException("School not found or already disabled.");
            Console.WriteLine("School disabled; every enrolled computer now fails authorization.");
            return;
        }
        default:
            throw new ArgumentException(
                "Use provision-school <name>, rotate-school-admin <school-guid>, " +
                "disable-school <school-guid>, or --self-test.");
    }
}

await store.VerifySchemaAsync();

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 4096);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(store);
builder.Services.AddHttpClient("github", client =>
{
    client.Timeout = TimeSpan.FromMinutes(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    AutomaticDecompression = DecompressionMethods.None,
    UseCookies = false
});
builder.Services.AddSingleton<GitHubPrivateReleases>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        string group = context.Request.Path.StartsWithSegments("/v1/enroll") ? "enroll"
            : context.Request.Path.StartsWithSegments("/v1/updates/assets") ? "archive"
            : "api";
        string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(group + "|" + ip,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = group == "enroll" ? 10 : group == "archive" ? 20 : 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsJsonAsync(new { error = "Update service temporarily unavailable." });
}));
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "private, no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    // No HTTP credentials or bearer tokens ever reach the application.
    // Deploy with Kestrel HTTPS or configure forwarding ONLY from a trusted proxy.
    if (!context.Request.IsHttps)
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        return;
    }
    await next();
});
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));

app.MapPost("/v1/enroll", async Task<IResult> (HttpContext http, EnrollmentRequest request,
    EnrollmentStore database, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.DeviceName) || request.DeviceName.Length > 100 ||
        request.DeviceName.Any(char.IsControl))
        return Results.BadRequest(new { error = "Invalid computer name." });
    try
    {
        var enrolled = await database.EnrollAsync(http.Request.Headers["X-FMSC-School-Admin-Key"].ToString(),
            request.DeviceName, cancellationToken);
        return enrolled is null ? Results.Unauthorized() :
            Results.Ok(new
            {
                schoolId = enrolled.SchoolId,
                deviceId = enrolled.DeviceId,
                deviceCredential = enrolled.DeviceCredential
            });
    }
    catch (DeviceEnrollmentLimitException)
    {
        return Results.Conflict(new { error = "This school has reached its device enrollment limit." });
    }
});

app.MapGet("/v1/school/devices", async Task<IResult> (HttpContext http,
    EnrollmentStore database, CancellationToken cancellationToken) =>
{
    var devices = await database.DevicesAsync(http.Request.Headers["X-FMSC-School-Admin-Key"].ToString(),
        cancellationToken);
    return devices is null ? Results.Unauthorized() : Results.Ok(new { devices });
});

app.MapPost("/v1/school/devices/{deviceId:guid}/revoke",
    async Task<IResult> (Guid deviceId, HttpContext http, EnrollmentStore database,
        CancellationToken cancellationToken) =>
    {
        var result = await database.RevokeDeviceAsync(
            http.Request.Headers["X-FMSC-School-Admin-Key"].ToString(), deviceId, cancellationToken);
        return result switch
        {
            RevokeResult.Unauthorized => Results.Unauthorized(),
            RevokeResult.NotFound => Results.NotFound(),
            _ => Results.NoContent()
        };
    });

app.MapGet("/v1/updates/latest", async Task<IResult> (HttpContext http,
    EnrollmentStore database, GitHubPrivateReleases releases, CancellationToken cancellationToken) =>
{
    if (!await database.AuthenticateDeviceAsync(Bearer(http), cancellationToken))
        return Results.Unauthorized();
    if (http.Request.Query["channel"].ToString() is not ("" or "stable") ||
        http.Request.Query["platform"].ToString() is not ("" or "win-x64"))
        return Results.BadRequest(new { error = "Only stable win-x64 updates are supported." });
    var latest = await releases.GetLatestAsync(cancellationToken);
    return Results.Ok(new
    {
        releaseId = latest.Id,
        version = latest.Manifest.Version,
        manifestBase64 = Convert.ToBase64String(latest.ManifestBytes),
        signatureBase64 = Convert.ToBase64String(latest.Signature),
        full = new { latest.Manifest.Full.Name, latest.Manifest.Full.Length },
        incremental = latest.Manifest.Incremental is null ? null :
            new { latest.Manifest.Incremental.Name, latest.Manifest.Incremental.Length }
    });
});

app.MapGet("/v1/updates/assets/{releaseId:long}/{archiveName}",
    async (long releaseId, string archiveName, HttpContext http,
        EnrollmentStore database, GitHubPrivateReleases releases, CancellationToken cancellationToken) =>
    {
        if (!await database.AuthenticateDeviceAsync(Bearer(http), cancellationToken))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        var release = await releases.GetLatestAsync(cancellationToken);
        if (releaseId != release.Id || !release.Assets.TryGetValue(archiveName, out var archive))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "application/zip";
        http.Response.ContentLength = archive.Size;
        try
        {
            await releases.StreamArchiveAsync(archive, http.Response.Body, cancellationToken);
        }
        catch when (http.Response.HasStarted)
        {
            http.Abort();
        }
    });

app.Run();

static string? Bearer(HttpContext context)
{
    string header = context.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        ? header[7..].Trim() : null;
}

internal sealed record EnrollmentRequest(string? DeviceName);
