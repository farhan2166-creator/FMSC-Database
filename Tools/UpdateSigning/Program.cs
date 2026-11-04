using System.Security.Cryptography;
using FMSC_SMS.UpdateSigning;

try
{
    if (args.SequenceEqual(new[] { "--self-test" }, StringComparer.Ordinal))
    {
        LocalPublisherSigningKey.SelfTest();
        Console.WriteLine("PASS: DPAPI publisher key, RSA-PSS signing, encrypted backup/restore, tamper and overwrite rejection.");
        return;
    }

    if (args.SequenceEqual(new[] { "initialize" }, StringComparer.Ordinal))
    {
        var result = LocalPublisherSigningKey.Generate(LocalPublisherSigningKey.DefaultDirectory);
        PrintPaths(result);
        return;
    }

    if (args.Length == 2 && args[0] == "backup")
    {
        char[] first = ReadHiddenPassphrase("New offline recovery passphrase");
        char[] second = ReadHiddenPassphrase("Repeat recovery passphrase");
        try
        {
            if (!first.AsSpan().SequenceEqual(second))
                throw new InvalidOperationException("Recovery passphrases did not match; no backup was created.");
            LocalPublisherSigningKey.Backup(
                Path.Combine(LocalPublisherSigningKey.DefaultDirectory,
                    LocalPublisherSigningKey.ProtectedFileName), args[1], first);
            Console.WriteLine("Encrypted offline signing-key recovery backup created at: " +
                Path.GetFullPath(args[1]));
            Console.WriteLine("Store the backup OUTSIDE both repositories. " +
                "Keep its recovery passphrase separately; loss of both prevents future signed updates.");
        }
        finally
        {
            Array.Fill(first, '\0');
            Array.Fill(second, '\0');
        }
        return;
    }

    if (args.Length == 2 && args[0] == "restore")
    {
        char[] password = ReadHiddenPassphrase("Offline recovery passphrase");
        try
        {
            var result = LocalPublisherSigningKey.Restore(args[1],
                LocalPublisherSigningKey.DefaultDirectory, password);
            PrintPaths(result);
        }
        finally
        {
            Array.Fill(password, '\0');
        }
        return;
    }

    throw new ArgumentException(
        "Use 'initialize', 'backup <NEW-OFFLINE-FILE.private.pem>', " +
        "'restore <EXISTING-ENCRYPTED-FILE.private.pem>', or '--self-test'.");
}
catch (Exception error)
{
    Console.Error.WriteLine("Publisher signing initialization failed: " + error.Message);
    Environment.ExitCode = 1;
}

static void PrintPaths((string ProtectedKey, string PublicKey) key)
{
    Console.WriteLine("Publisher signing identity protected for this Windows user.");
    Console.WriteLine("PROTECTED PRIVATE key (never commit or upload): " + key.ProtectedKey);
    Console.WriteLine("PUBLIC verification key (safe to review and commit): " + key.PublicKey);
    Console.WriteLine("Back up the publisher identity with the 'backup' command BEFORE publishing real updates.");
    Console.WriteLine("After owner approval, copy ONLY the PUBLIC key into Core/Updates/publisher-public.pem.");
}

static char[] ReadHiddenPassphrase(string label)
{
    if (Console.IsInputRedirected)
        throw new InvalidOperationException("Enter the recovery passphrase interactively; never pass it in command arguments or pipelines.");
    Console.Write(label + ": ");
    var password = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Count > 0) password.RemoveAt(password.Count - 1);
            continue;
        }
        if (char.IsControl(key.KeyChar)) continue;
        if (password.Count >= 256)
            throw new InvalidOperationException("Recovery passphrase exceeds 256 characters.");
        password.Add(key.KeyChar);
    }
    Console.WriteLine();
    char[] result = password.ToArray();
    password.Clear();
    if (result.Length < 16)
    {
        Array.Fill(result, '\0');
        throw new ArgumentException("Recovery passphrase must contain at least 16 characters.");
    }
    return result;
}
