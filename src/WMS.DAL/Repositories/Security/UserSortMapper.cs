namespace WMS.DAL.Repositories.Security;

// Closed-set SQL injection defence for user-list sortBy parameter.
// Unknown / hostile values fall through to email-ascending.
internal static class UserSortMapper
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["email"]       = "u.Email",
        ["fullName"]    = "u.FullName",
        ["isActive"]    = "u.IsActive",
        ["lastLoginAt"] = "u.LastLoginAt",
        ["createdAt"]   = "u.CreatedAt",
    };

    public static string ResolveColumn(string? sortBy)
    {
        if (sortBy is not null && Map.TryGetValue(sortBy, out var col))
            return col;
        return "u.Email";
    }
}
