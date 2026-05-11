using System.Security.Cryptography;

namespace WMS.Web.Services.SuperAdmin;

// Phase 27 — cryptographically strong temp password generator for the
// bootstrap ADMIN user created at tenant provisioning time.
//
// Output guarantees:
//   - 16 characters
//   - At least 2 uppercase, 2 lowercase, 2 digits, 2 symbols
//   - Drawn from RandomNumberGenerator (NOT System.Random)
//   - Passes PasswordPolicy.Validate (8+ mixed + digit)
//
// Symbol set excludes characters that look alike in confirmation
// emails / clipboard pastes (0/O, 1/l/I) to reduce operator
// confusion when typing the temp password.
public static class TempPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // I, O excluded
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";  // l excluded
    private const string Digits    = "23456789";                   // 0, 1 excluded
    private const string Symbols   = "!@#$%^&*?";                  // safe URL/shell symbols

    public static string Generate(int length = 16)
    {
        if (length < 12)
            throw new ArgumentOutOfRangeException(nameof(length),
                "Temp password length must be at least 12.");

        // Start with a guaranteed minimum from each pool.
        var chars = new char[length];
        chars[0] = PickRandom(Uppercase);
        chars[1] = PickRandom(Uppercase);
        chars[2] = PickRandom(Lowercase);
        chars[3] = PickRandom(Lowercase);
        chars[4] = PickRandom(Digits);
        chars[5] = PickRandom(Digits);
        chars[6] = PickRandom(Symbols);
        chars[7] = PickRandom(Symbols);

        // Remainder from combined pool.
        var pool = Uppercase + Lowercase + Digits + Symbols;
        for (int i = 8; i < length; i++)
            chars[i] = PickRandom(pool);

        // Shuffle in place so the guaranteed-pool prefix doesn't make
        // the password's first chars predictable.
        Shuffle(chars);

        return new string(chars);
    }

    private static char PickRandom(string pool) =>
        pool[RandomNumberGenerator.GetInt32(pool.Length)];

    private static void Shuffle(char[] arr)
    {
        // Fisher-Yates with a cryptographic PRNG.
        for (int i = arr.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
    }
}
