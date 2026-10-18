using System.Security.Cryptography;
using System.Text;

namespace CloveParentalGuard.Core;

/// <summary>
/// ECDSA P-256 / SHA-256 signatures over the exact bytes of the blocklist file.
/// The private key lives only on the owner's PC (passphrase-encrypted); the app embeds
/// only public keys. A list is accepted if ANY embedded key verifies it, which lets a
/// backup key kept offline take over if the primary is ever lost or compromised.
/// </summary>
public static class BlocklistSigner
{
    // Domain separation: a signature made for this purpose cannot be replayed as
    // a signature over anything else, and vice versa.
    private static readonly byte[] Context = Encoding.ASCII.GetBytes("CloveParentalGuard-blocklist-v1\n");

    private const int KdfIterations = 600_000;

    /// <summary>Creates a key pair. Returns the public key (base64 SPKI) and the passphrase-encrypted private key (PEM).</summary>
    public static (string PublicKey, string EncryptedPrivateKeyPem) GenerateKeyPair(string passphrase)
    {
        if (passphrase.Length < 12)
            throw new ArgumentException("Passphrase must be at least 12 characters.", nameof(passphrase));

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        var pem = key.ExportEncryptedPkcs8PrivateKeyPem(passphrase,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, KdfIterations));
        return (pub, pem);
    }

    /// <summary>The public key (base64 SPKI) that belongs to an encrypted private key.</summary>
    public static string PublicKeyOf(string encryptedPrivateKeyPem, string passphrase)
    {
        using var key = ECDsa.Create();
        key.ImportFromEncryptedPem(encryptedPrivateKeyPem, passphrase);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    public static string Sign(byte[] data, string encryptedPrivateKeyPem, string passphrase)
    {
        using var key = ECDsa.Create();
        key.ImportFromEncryptedPem(encryptedPrivateKeyPem, passphrase);
        return Convert.ToBase64String(key.SignData(Message(data), HashAlgorithmName.SHA256));
    }

    /// <summary>True only if <paramref name="signatureBase64"/> is a valid signature of <paramref name="data"/> by one of the keys. Never throws.</summary>
    public static bool Verify(byte[] data, string signatureBase64, IEnumerable<string> publicKeysBase64)
    {
        byte[] sig;
        try { sig = Convert.FromBase64String(signatureBase64.Trim()); }
        catch { return false; }

        var message = Message(data);
        foreach (var pub in publicKeysBase64)
        {
            try
            {
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pub.Trim()), out _);
                if (key.VerifyData(message, sig, HashAlgorithmName.SHA256)) return true;
            }
            catch { /* malformed key or signature: try the next key */ }
        }
        return false;
    }

    private static byte[] Message(byte[] data)
    {
        var message = new byte[Context.Length + data.Length];
        Context.CopyTo(message, 0);
        data.CopyTo(message, Context.Length);
        return message;
    }
}
