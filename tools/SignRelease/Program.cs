// Release signing for MiniPrinter updates (ECDSA P-256 / SHA-256).
//   SignRelease keygen                       → prints a new private key (PKCS#8) and public key (SPKI), Base64
//   SignRelease sums <file>... [--out SHA256SUMS] → sha256sum-style lines
//   SignRelease sign <file>                  → writes <file>.sig; key from RELEASE_SIGNING_KEY or --key-file
//   SignRelease verify <file> <publicKey>    → checks <file>.sig
using System.Text;
using MiniPrinter.Updates;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: SignRelease keygen | sums <file>... | sign <file> [--key-file <path>] | verify <file> <publicKeyBase64>");
    return 64;
}

switch (args[0])
{
    case "keygen":
    {
        var (privateKey, publicKey) = ReleaseSignature.GenerateKeyPair();
        Console.WriteLine("PRIVATE KEY (secret RELEASE_SIGNING_KEY — keep a backup, never commit it):");
        Console.WriteLine(privateKey);
        Console.WriteLine();
        Console.WriteLine("PUBLIC KEY (embed in the tray app):");
        Console.WriteLine(publicKey);
        return 0;
    }
    case "sums":
    {
        // sums <file>... [--out <path>]: --out writes the exact bytes that will be signed.
        var outIndex = Array.IndexOf(args, "--out");
        var files = args.Skip(1).Where((_, i) => outIndex < 0 || (i + 1 != outIndex && i + 1 != outIndex + 1)).ToList();
        var content = Checksums.Create(files);
        if (outIndex > 0)
            File.WriteAllText(args[outIndex + 1], content, new UTF8Encoding(false));
        else
            Console.Write(content);
        return 0;
    }
    case "sign":
    {
        var file = args.ElementAtOrDefault(1) ?? throw new ArgumentException("sign needs a file.");
        var keyFileIndex = Array.IndexOf(args, "--key-file");
        var key = keyFileIndex > 0 ? File.ReadAllText(args[keyFileIndex + 1]) : Environment.GetEnvironmentVariable("RELEASE_SIGNING_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("No signing key: set RELEASE_SIGNING_KEY or pass --key-file.");
            return 2;
        }
        File.WriteAllText(file + ".sig", ReleaseSignature.Sign(File.ReadAllBytes(file), key), Encoding.ASCII);
        Console.WriteLine($"{file}.sig");
        return 0;
    }
    case "verify":
    {
        var file = args[1];
        var ok = ReleaseSignature.Verify(File.ReadAllBytes(file), File.ReadAllText(file + ".sig"), args[2]);
        Console.WriteLine(ok ? "signature OK" : "signature INVALID");
        return ok ? 0 : 1;
    }
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        return 64;
}
