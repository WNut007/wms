namespace WMS.Web.Services.Mappers;

// Customer.Initials and Customer.AvatarColor are display-only fields
// the mock service computed; the schema doesn't carry them. Computed
// here on the way out, deterministic from the customer Name so the
// same customer always renders with the same avatar across requests
// and machines.
//
// Palette mirrors the 8-color set the mock used (MockCustomerData
// Service.GenerateSeed) so the look stays identical after the
// controller swap in T9.
public static class CustomerAvatar
{
    private static readonly string[] Palette =
    {
        "#534AB7", "#0C447C", "#27500A", "#854F0B",
        "#A32D2D", "#3C3489", "#085041", "#BA7517",
    };

    // First char of the first two whitespace-delimited words, upper-
    // cased. Single-word names → single initial. Empty / whitespace-
    // only names → "—" (em dash) so the UI never shows a blank chip.
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "—";

        var parts = name.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => "—",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant(),
        };
    }

    // Stable per-name color picker. Uses the FNV-1a-flavoured rolling
    // hash so the output is deterministic across runtimes (string
    // .GetHashCode is randomised per AppDomain in .NET Core+ — would
    // give different colors after a restart).
    public static string Color(string? seed)
    {
        if (string.IsNullOrEmpty(seed)) return Palette[0];

        var hash = 0;
        foreach (var ch in seed)
        {
            hash = unchecked(hash * 31 + ch);
        }
        // Mask off the sign bit instead of Math.Abs — Math.Abs(int
        // .MinValue) throws.
        var idx = (hash & int.MaxValue) % Palette.Length;
        return Palette[idx];
    }
}
