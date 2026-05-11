namespace WMS.Web.Infrastructure;

// Phase 26 — fail-fast on missing production config. Runs once at
// Program.cs startup BEFORE the host is built. Throws if a required
// key is missing AND the environment is Production.
//
// Development is permissive: missing keys log a warning but boot
// proceeds (so a fresh checkout can `dotnet run` without user-secrets
// setup). Production is strict — the app refuses to start with empty
// connection strings.
//
// Env var mapping (ASP.NET Core standard):
//   ConnectionStrings__MasterDb        → ConnectionStrings:MasterDb
//   ConnectionStrings__TenantTemplate  → ConnectionStrings:TenantTemplate
public static class ConfigurationValidator
{
    private static readonly string[] RequiredConnectionStrings =
    {
        "MasterDb",
        "TenantTemplate",
    };

    public static void Validate(IConfiguration config, IHostEnvironment env)
    {
        var missing = new List<string>();

        foreach (var name in RequiredConnectionStrings)
        {
            var value = config.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(value))
                missing.Add($"ConnectionStrings:{name}");
        }

        if (missing.Count == 0) return;

        if (env.IsProduction())
        {
            throw new InvalidOperationException(
                $"Production configuration is missing required keys: {string.Join(", ", missing)}. " +
                "Set them via environment variables (ConnectionStrings__MasterDb etc.) " +
                "or appsettings.Production.json.");
        }

        // Dev/Staging — log to stderr; boot continues so devs can run a
        // partial config (e.g. before completing user-secrets setup).
        Console.Error.WriteLine(
            $"[ConfigurationValidator] WARNING: missing keys: {string.Join(", ", missing)} " +
            $"(env={env.EnvironmentName} — non-Production, continuing).");
    }
}
