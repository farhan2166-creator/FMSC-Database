using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FMSC_SMS.Core.Updates;

namespace FMSC_SMS.UpdateBroker;

internal sealed record ReleaseAsset(long Id, string Name, long Size);
internal sealed record VerifiedRelease(long Id, UpdateManifest Manifest, byte[] ManifestBytes,
    byte[] Signature, IReadOnlyDictionary<string, ReleaseAsset> Assets);

/// <summary>
/// Server-side, read-only GitHub App access. Short-lived installation tokens are
/// NEVER sent to a school computer, serialized in broker responses or forwarded
/// to GitHub's asset CDN on a redirect.
/// </summary>
internal sealed class GitHubPrivateReleases
{
    private readonly BrokerConfig _settings;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly SemaphoreSlim _releaseLock = new(1, 1);
    private string? _installationToken;
    private DateTimeOffset _tokenExpiration;
    private VerifiedRelease? _cachedRelease;
    private DateTimeOffset _cachedUntil;

    internal GitHubPrivateReleases(BrokerConfig settings, IHttpClientFactory factory)
    {
        _settings = settings;
        _http = factory.CreateClient("github");
    }

    internal async Task<VerifiedRelease> GetLatestAsync(CancellationToken cancellationToken)
    {
        if (_cachedRelease is not null && _cachedUntil > DateTimeOffset.UtcNow)
            return _cachedRelease;
        await _releaseLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedRelease is not null && _cachedUntil > DateTimeOffset.UtcNow)
                return _cachedRelease;

            // An explicit privacy gate. The existing source repository must first
            // be made private by its owner before this service may distribute it.
            using (var repo = await GetJsonAsync(RepoPath, cancellationToken))
                if (!repo.RootElement.GetProperty("private").GetBoolean())
                    throw new InvalidOperationException("Private GitHub repository required.");

            using var release = await GetJsonAsync(RepoPath + "/releases/latest", cancellationToken);
            var root = release.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
                throw new InvalidDataException("Unapproved preview release was returned by the stable feed.");
            long id = root.GetProperty("id").GetInt64();
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (id <= 0) throw new InvalidDataException("Invalid GitHub release ID.");

            var assets = new Dictionary<string, ReleaseAsset>(StringComparer.Ordinal);
            foreach (var item in root.GetProperty("assets").EnumerateArray())
            {
                string name = item.GetProperty("name").GetString() ?? "";
                long assetId = item.GetProperty("id").GetInt64();
                long size = item.GetProperty("size").GetInt64();
                if (assetId <= 0 || size <= 0 || name.Length == 0 || !assets.TryAdd(name, new(assetId, name, size)))
                    throw new InvalidDataException("Duplicate or invalid private release asset.");
            }
            var manifestAsset = RequiredAsset(assets, "update-manifest.json");
            var signatureAsset = RequiredAsset(assets, "update-manifest.sig");
            byte[] manifestBytes = await ReadSmallAssetAsync(manifestAsset, 4 * 1024 * 1024, cancellationToken);
            byte[] signature = await ReadSmallAssetAsync(signatureAsset, 8192, cancellationToken);
            using var publicKey = RSA.Create();
            publicKey.ImportFromPem(File.ReadAllText(_settings.PublisherPublicKeyPath));
            if (publicKey.KeySize < 3072)
                throw new CryptographicException("Publisher verification key is too short.");
            var manifest = UpdateManifestPolicy.ReadVerified(manifestBytes, signature, publicKey);

            if (tag != "v" + manifest.Version)
                throw new InvalidDataException("The signed manifest version does not match its immutable release tag.");
            CheckArchive(assets, manifest.Full);
            if (manifest.Incremental is not null) CheckArchive(assets, manifest.Incremental);
            // Explicit allowlist: never permit arbitrary release assets through broker URLs.
            var approved = new Dictionary<string, ReleaseAsset>(StringComparer.Ordinal)
            {
                [manifest.Full.Name] = assets[manifest.Full.Name]
            };
            if (manifest.Incremental is not null)
                approved[manifest.Incremental.Name] = assets[manifest.Incremental.Name];

            var verified = new VerifiedRelease(id, manifest, manifestBytes, signature, approved);
            _cachedRelease = verified;
            _cachedUntil = DateTimeOffset.UtcNow.AddSeconds(60);
            return verified;
        }
        finally
        {
            _releaseLock.Release();
        }
    }

    internal async Task StreamArchiveAsync(ReleaseAsset asset, Stream destination,
        CancellationToken cancellationToken)
    {
        using var response = await OpenAssetAsync(asset.Id, cancellationToken);
        if (response.Content.Headers.ContentLength is long length && length != asset.Size)
            throw new InvalidDataException("GitHub release asset size differs from signed release metadata.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[65536];
        long total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > asset.Size)
                throw new InvalidDataException("GitHub streamed more bytes than the approved update archive.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total != asset.Size)
            throw new InvalidDataException("GitHub streamed an incomplete update archive.");
        // The caller still verifies the SHA-256 against the publisher-signed
        // manifest after download. The broker does not claim transport is a signature.
    }

    private string RepoPath => "repos/" + _settings.GitHubOwner + "/" + _settings.GitHubRepository;

    private static ReleaseAsset RequiredAsset(IReadOnlyDictionary<string, ReleaseAsset> assets, string name) =>
        assets.TryGetValue(name, out var asset) ? asset :
            throw new InvalidDataException("Approved release is missing " + name + ".");

    private static void CheckArchive(IReadOnlyDictionary<string, ReleaseAsset> assets,
        UpdateArchive archive)
    {
        var actual = RequiredAsset(assets, archive.Name);
        if (actual.Size != archive.Length)
            throw new InvalidDataException("GitHub release asset size does not match signed manifest.");
    }

    private async Task<byte[]> ReadSmallAssetAsync(ReleaseAsset asset, int limit,
        CancellationToken cancellationToken)
    {
        if (asset.Size > limit)
            throw new InvalidDataException("Release metadata exceeds its allowed size.");
        using var response = await OpenAssetAsync(asset.Id, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + read > limit)
                throw new InvalidDataException("Release metadata stream exceeds its allowed size.");
            output.Write(buffer, 0, read);
        }
        if (output.Length != asset.Size)
            throw new InvalidDataException("GitHub release metadata size mismatch.");
        return output.ToArray();
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await AuthorizedRequestAsync(HttpMethod.Get, path,
            "application/vnd.github+json", cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException("Private GitHub release lookup was unavailable.");
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> OpenAssetAsync(long assetId,
        CancellationToken cancellationToken)
    {
        var response = await AuthorizedRequestAsync(HttpMethod.Get,
            RepoPath + "/releases/assets/" + assetId, "application/octet-stream", cancellationToken);
        // GitHub documents 200 (direct binary) or 302 (signed temporary asset URL).
        // Never forward the GitHub installation token when following a redirect.
        for (int hop = 0; hop < 3; hop++)
        {
            if (!IsRedirect(response.StatusCode)) break;
            Uri destination = response.Headers.Location
                ?? throw new InvalidDataException("Release asset redirect was missing a location.");
            if (!destination.IsAbsoluteUri ||
                !IsTrustedReleaseAssetRedirect(destination))
                throw new InvalidDataException("GitHub returned an untrusted release asset redirect.");
            response.Dispose();
            using var request = MakeRequest(HttpMethod.Get, destination);
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        if (response.StatusCode != HttpStatusCode.OK ||
            response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            response.Dispose();
            throw new HttpRequestException("Private GitHub release asset could not be downloaded.");
        }
        return response;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    internal static bool IsTrustedReleaseAssetRedirect(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        (uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private async Task<HttpResponseMessage> AuthorizedRequestAsync(HttpMethod method,
        string relativePath, string accept, CancellationToken cancellationToken)
    {
        using var request = MakeRequest(method, new Uri("https://api.github.com/" + relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await GetInstallationTokenAsync(cancellationToken));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static HttpRequestMessage MakeRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.UserAgent.ParseAdd("FMSC-UpdateBroker/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private async Task<string> GetInstallationTokenAsync(CancellationToken cancellationToken)
    {
        if (_installationToken is not null && _tokenExpiration > DateTimeOffset.UtcNow.AddMinutes(2))
            return _installationToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_installationToken is not null && _tokenExpiration > DateTimeOffset.UtcNow.AddMinutes(2))
                return _installationToken;
            string jwt = CreateAppJwt();
            using var request = MakeRequest(HttpMethod.Post,
                new Uri("https://api.github.com/app/installations/" + _settings.GitHubInstallationId + "/access_tokens"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            // Restrict even the GitHub App installation's token to one repo and contents:read.
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { repositories = new[] { _settings.GitHubRepository },
                    permissions = new { contents = "read" } }), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Created)
                throw new HttpRequestException("Unable to obtain a read-only GitHub App installation credential.");
            using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            string value = result.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidDataException("GitHub App returned no installation token.");
            var expiry = result.RootElement.GetProperty("expires_at").GetDateTimeOffset();
            if (value.Length < 20 || expiry <= DateTimeOffset.UtcNow.AddMinutes(3))
                throw new InvalidDataException("GitHub App returned an unusable short-lived token.");
            _installationToken = value;
            _tokenExpiration = expiry;
            return value;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private string CreateAppJwt()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = BrokerTokens.Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        string payload = BrokerTokens.Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new { iat = now - 60, exp = now + 8 * 60, iss = _settings.GitHubAppId })));
        string unsigned = header + "." + payload;
        using var key = RSA.Create();
        key.ImportFromPem(File.ReadAllText(_settings.GitHubAppPrivateKeyPath));
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes(unsigned),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return unsigned + "." + BrokerTokens.Base64Url(signature);
    }
}
