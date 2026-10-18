using System.Text;
using CloveParentalGuard.Core;

// Owner-only tool for the signed online blocklist.
//
//   keygen <name>   create a passphrase-protected key pair (do this twice: "primary", "backup")
//   sign            validate blocklist.json, bump its version, sign it -> blocklist.sig
//   verify          check blocklist.json + blocklist.sig against the keys built into the app
//
// Private keys are written to %USERPROFILE%\.cloveparentalguard\ and never into the repo.

static class Program
{
    static readonly string KeyDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cloveparentalguard");

    static int Main(string[] args)
    {
        try
        {
            return (args.Length > 0 ? args[0] : "") switch
            {
                "keygen" => KeyGen(args.Length > 1 ? args[1] : ""),
                "sign" => Sign(args.Skip(1).ToArray()),
                "verify" => Verify(),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    static int Usage()
    {
        Console.WriteLine("""
            usage:
              keygen <primary|backup>        create a key pair (asks for a passphrase)
              sign [--key <name>]            sign blocklist.json (default key: primary)
              verify                         verify blocklist.json against the built-in public keys
            """);
        return 2;
    }

    static int KeyGen(string name)
    {
        if (name is not ("primary" or "backup"))
        {
            Console.Error.WriteLine("Name the key 'primary' or 'backup'.");
            return 2;
        }
        Directory.CreateDirectory(KeyDir);
        var keyPath = Path.Combine(KeyDir, name + ".key.pem");
        if (File.Exists(keyPath))
        {
            Console.Error.WriteLine($"{keyPath} already exists. Refusing to overwrite a signing key.");
            return 1;
        }

        Console.WriteLine($"Creating the {name} key. Choose a long passphrase (12+ characters) and keep it safe:");
        Console.WriteLine("without it the key is useless, and nobody can recover it for you.");
        var pass = ReadPassphrase("Passphrase: ");
        if (pass != ReadPassphrase("Repeat passphrase: "))
        {
            Console.Error.WriteLine("Passphrases do not match.");
            return 1;
        }

        var (pub, pem) = BlocklistSigner.GenerateKeyPair(pass);
        File.WriteAllText(keyPath, pem);
        File.WriteAllText(Path.Combine(KeyDir, name + ".pub"), pub + "\n");

        var keysFile = FindUp("src/CloveParentalGuard.Core/blocklist-public-keys.txt");
        if (keysFile is not null && !File.ReadAllLines(keysFile).Contains(pub))
        {
            File.AppendAllText(keysFile, pub + "\n");
            Console.WriteLine($"Public key added to {keysFile}");
        }

        Console.WriteLine($"""

            Private key (encrypted): {keyPath}
            Public key:              {pub}

            NEVER commit or share the private key file.
            Back it up somewhere safe. For the 'backup' key, keep it OFFLINE (e.g. a USB stick in a drawer).
            """);
        return 0;
    }

    static int Sign(string[] args)
    {
        var name = args.Length == 2 && args[0] == "--key" ? args[1] : "primary";
        var keyPath = Path.Combine(KeyDir, name + ".key.pem");
        if (!File.Exists(keyPath)) throw new FileNotFoundException($"No key at {keyPath}. Run: keygen {name}");

        var listPath = FindUp("blocklist.json") ?? throw new FileNotFoundException("blocklist.json not found (run from the repo).");
        var sigPath = Path.Combine(Path.GetDirectoryName(listPath)!, "blocklist.sig");

        // Parse with the same strict rules the app uses: refuse to sign anything the app would reject.
        var list = BlocklistValidator.Parse(File.ReadAllBytes(listPath));
        list.Version = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), list.Version + 1);
        var bytes = list.ToBytes();
        BlocklistValidator.Parse(bytes); // round-trip check

        var pem = File.ReadAllText(keyPath);
        var pass = ReadPassphrase($"Passphrase for the {name} key: ");
        var pub = BlocklistSigner.PublicKeyOf(pem, pass);

        if (!BuiltInBlocklist.TrustedKeys().Contains(pub))
            throw new InvalidOperationException(
                "This key's public key is not in blocklist-public-keys.txt, so installed apps would reject the list.");

        var sig = BlocklistSigner.Sign(bytes, pem, pass);
        if (!BlocklistSigner.Verify(bytes, sig, BuiltInBlocklist.TrustedKeys()))
            throw new InvalidOperationException("Self-check failed: signature does not verify.");

        File.WriteAllBytes(listPath, bytes);
        File.WriteAllText(sigPath, sig + "\n", new UTF8Encoding(false));

        Console.WriteLine($"Signed {list.EntryCount} entries, version {list.Version}.");
        Console.WriteLine($"  {listPath}\n  {sigPath}");
        Console.WriteLine("Now commit BOTH files and push.");
        return 0;
    }

    static int Verify()
    {
        var listPath = FindUp("blocklist.json") ?? throw new FileNotFoundException("blocklist.json not found.");
        var sigPath = Path.Combine(Path.GetDirectoryName(listPath)!, "blocklist.sig");
        var result = BlocklistUpdater.Evaluate(
            File.ReadAllBytes(listPath), File.ReadAllText(sigPath), BuiltInBlocklist.TrustedKeys(), currentVersion: 0);
        Console.WriteLine($"{result.Outcome}: {result.Message}");
        return result.Outcome == BlocklistOutcome.Rejected ? 1 : 0;
    }

    static string? FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    static string ReadPassphrase(string prompt)
    {
        Console.Write(prompt);
        var sb = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return sb.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
            else if (!char.IsControl(key.KeyChar)) sb.Append(key.KeyChar);
        }
    }
}
