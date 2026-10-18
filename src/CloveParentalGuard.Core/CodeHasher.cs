using System.Security.Cryptography;

namespace CloveParentalGuard.Core;

public record HashedCode(string Salt, string Hash, int Iterations);

public static class CodeHasher
{
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static HashedCode Hash(string code)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Derive(code, salt, Iterations);
        return new HashedCode(Convert.ToBase64String(salt), Convert.ToBase64String(hash), Iterations);
    }

    public static bool Verify(string code, HashedCode stored)
    {
        byte[] salt = Convert.FromBase64String(stored.Salt);
        byte[] expected = Convert.FromBase64String(stored.Hash);
        byte[] actual = Derive(code, salt, stored.Iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string code, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(code, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
