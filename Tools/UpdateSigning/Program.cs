using FMSC_SMS.UpdateSigning;

try
{
    if (args.SequenceEqual(new[] { "--self-test" }, StringComparer.Ordinal))
    {
        LocalPublisherSigningKey.SelfTest();
        Console.WriteLine("PASS: publisher private key never written in plaintext, DPAPI sealed, RSA-PSS signature verified, no silent replacement.");
        return;
    }
    if (!args.SequenceEqual(new[] { "initialize" }, StringComparer.Ordinal))
        throw new ArgumentException("Use 'initialize' or '--self-test'.");
    var result = LocalPublisherSigningKey.Generate(LocalPublisherSigningKey.DefaultDirectory);
    Console.WriteLine("Created one local publisher signing identity for this Windows user.");
    Console.WriteLine("PROTECTED PRIVATE key (never upload or commit): " + result.ProtectedKey);
    Console.WriteLine("PUBLIC verification key (safe to review and commit): " + result.PublicKey);
    Console.WriteLine("After owner approval, copy the PUBLIC key into Core/Updates/publisher-public.pem in the source project.");
    Console.WriteLine("Use --signing-protected-key for manual local packaging. Never distribute or paste the private key.");
}
catch (Exception error)
{
    Console.Error.WriteLine("Publisher signing initialization failed: " + error.Message);
    Environment.ExitCode = 1;
}
