using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CorePlasma.Common.Helpers;

/// <summary>
/// PBKDF2 (SHA-256) password hashing. Format: {iterations}.{saltBase64}.{hashBase64}
/// Imported PLT bcrypt and tagged PLM HMAC-SHA512 credentials are verified, then upgraded on sign-in.
/// </summary>
public static class PasswordHasher
{
    private const string LegacyPlmPrefix = "PLM-HMACSHA512$";
    private const int LegacyPlmSaltSize = 128;
    private const int LegacyPlmHashSize = 64;
    private const int LegacyPlmSaltBase64Length = 172;
    private const int LegacyPlmHashBase64Length = 88;

    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    // PLT uses cost 10. Bound imported costs before calling bcrypt because its work is exponential.
    private static readonly Regex LegacyBcryptFormat = new(
        @"\A\$2[aby]\$(0[4-9]|1[0-4])\$[./A-Za-z0-9]{53}\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static bool IsLegacyBcryptHash(string? storedHash) =>
        storedHash is { Length: 60 } && LegacyBcryptFormat.IsMatch(storedHash);

    public static bool IsLegacyPlmHmacSha512Hash(string? storedHash) =>
        TryReadLegacyPlmHmacSha512Hash(storedHash, out _, out _);

    private static bool TryReadLegacyPlmHmacSha512Hash(string? storedHash, out byte[] salt, out byte[] expected)
    {
        salt = [];
        expected = [];
        if (storedHash is null
            || storedHash.Length != LegacyPlmPrefix.Length + LegacyPlmSaltBase64Length + 1 + LegacyPlmHashBase64Length
            || !storedHash.StartsWith(LegacyPlmPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var separator = LegacyPlmPrefix.Length + LegacyPlmSaltBase64Length;
        if (storedHash[separator] != '$')
        {
            return false;
        }

        var saltText = storedHash.AsSpan(LegacyPlmPrefix.Length, LegacyPlmSaltBase64Length);
        var hashText = storedHash.AsSpan(separator + 1, LegacyPlmHashBase64Length);
        var decodedSalt = new byte[LegacyPlmSaltSize];
        var decodedHash = new byte[LegacyPlmHashSize];
        if (!Convert.TryFromBase64Chars(saltText, decodedSalt, out var saltBytes) || saltBytes != LegacyPlmSaltSize
            || !Convert.TryFromBase64Chars(hashText, decodedHash, out var hashBytes) || hashBytes != LegacyPlmHashSize
            || !saltText.SequenceEqual(Convert.ToBase64String(decodedSalt))
            || !hashText.SequenceEqual(Convert.ToBase64String(decodedHash)))
        {
            return false;
        }

        salt = decodedSalt;
        expected = decodedHash;
        return true;
    }

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        if (storedHash.StartsWith(LegacyPlmPrefix, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(password)
                || !TryReadLegacyPlmHmacSha512Hash(storedHash, out var salt, out var expected))
            {
                return false;
            }

            var actual = HMACSHA512.HashData(salt, System.Text.Encoding.UTF8.GetBytes(password));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        if (IsLegacyBcryptHash(storedHash))
        {
            try
            {
                // Standard UTF-8 bcrypt preserves PLT's first-72-byte behavior, including Unicode.
                // Enhanced/prehashed bcrypt would change existing credentials and must not be used.
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        var parts = storedHash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
