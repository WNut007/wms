using FluentMigrator.Infrastructure;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();
var db = args.Length >= 2 ? args[1].ToLowerInvariant() : "master";

if (command is not ("up" or "down" or "list" or "version"))
{
    Console.Error.WriteLine($"Unknown command: '{command}'");
    PrintUsage();
    return 1;
}

if (db is not ("master" or "tenant" or "tenants"))
{
    Console.Error.WriteLine($"Unknown db: '{db}' — expected 'master', 'tenant', or 'tenants'");
    PrintUsage();
    return 1;
}

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

// Phase 26 — the 'tenants' command iterates master.Tenants and runs
// the tenant migration suite against EACH tenant DB. Use after Master
// migrations are applied + before resuming traffic.
//
// Stops on first failure (fail-fast). Re-running picks up where it
// left off because FluentMigrator's VersionInfo table per tenant DB
// makes each application idempotent.
if (db == "tenants")
{
    return await RunForAllTenantsAsync(config, command);
}

var connKey = db == "tenant" ? "TenantTemplate" : "MasterDb";
var connString = config.GetConnectionString(connKey)
    ?? throw new InvalidOperationException(
        $"ConnectionString '{connKey}' not configured in appsettings.json");

var tag = db == "tenant" ? "Tenant" : "Master";
return RunSingle(connString, tag, command);

static int RunSingle(string connString, string tag, string command)
{
    var services = new ServiceCollection()
        .AddLogging(lb => lb
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information))
        .AddFluentMigratorCore()
        .ConfigureRunner(rb => rb
            .AddSqlServer()
            .WithGlobalConnectionString(connString)
            .ScanIn(typeof(Program).Assembly).For.Migrations())
        .Configure<RunnerOptions>(opts =>
        {
            opts.Tags = new[] { tag };
        })
        .BuildServiceProvider(validateScopes: false);

    using var scope = services.CreateScope();
    var sp = scope.ServiceProvider;

    try
    {
        switch (command)
        {
            case "up":
                sp.GetRequiredService<IMigrationRunner>().MigrateUp();
                break;

            case "down":
                sp.GetRequiredService<IMigrationRunner>().Rollback(1);
                break;

            case "list":
                PrintList(sp, tag);
                break;

            case "version":
                PrintVersion(sp);
                break;
        }
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: {ex.Message}");
        return 2;
    }
}

// Phase 26 — Tenant-fanout coordinator. Reads master.Tenants, builds a
// per-tenant connection string from the TenantTemplate (replacing the
// DB name token), and runs Tenant-tagged migrations against each.
//
// Convention: TenantTemplate uses '{0}' or 'WMS_TenantTemplate' as the
// DB-name token; we substitute Tenants.DatabaseName. Status='Active'
// tenants are migrated; Suspended/Inactive skipped (operator can
// re-enable in master.Tenants then re-run this command).
static async Task<int> RunForAllTenantsAsync(IConfiguration config, string command)
{
    if (command is not ("up" or "list" or "version"))
    {
        Console.Error.WriteLine(
            $"Command '{command}' not supported for 'tenants' mode. " +
            $"Use 'up' to migrate all tenant DBs; 'list' / 'version' to inspect one. " +
            $"Per-tenant 'down' is intentionally not supported — roll back tenant-by-tenant " +
            $"with `dotnet run -- down tenant` against a single connection string instead.");
        return 1;
    }

    var masterConn = config.GetConnectionString("MasterDb")
        ?? throw new InvalidOperationException("ConnectionString 'MasterDb' not configured.");
    var template = config.GetConnectionString("TenantTemplate")
        ?? throw new InvalidOperationException("ConnectionString 'TenantTemplate' not configured.");

    var tenants = await ReadActiveTenantsAsync(masterConn);
    if (tenants.Count == 0)
    {
        Console.WriteLine("(no active tenants found in master.Tenants)");
        return 0;
    }

    Console.WriteLine($"Found {tenants.Count} active tenant(s). Running '{command}' against each...");

    var ok = 0;
    var failed = 0;
    foreach (var t in tenants)
    {
        Console.WriteLine();
        Console.WriteLine($"--- Tenant: {t.Code} ({t.DatabaseName}) ---");
        var tenantConn = BuildTenantConnection(template, t.DatabaseName);
        var exit = RunSingle(tenantConn, "Tenant", command);
        if (exit == 0) ok++; else failed++;
        if (exit != 0)
        {
            Console.Error.WriteLine($"  STOPPED on '{t.Code}' (exit {exit}). " +
                $"{ok} succeeded, {tenants.Count - ok - failed} remaining.");
            return exit;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"All {ok} tenant(s) migrated successfully.");
    return 0;
}

static string BuildTenantConnection(string template, string databaseName)
{
    // Two substitution shapes supported:
    //   "Database={0}"           — positional placeholder
    //   "Database=WMS_TenantTemplate" — replace literal token
    if (template.Contains("{0}", StringComparison.Ordinal))
        return string.Format(template, databaseName);
    if (template.Contains("WMS_TenantTemplate", StringComparison.OrdinalIgnoreCase))
        return template.Replace("WMS_TenantTemplate", databaseName, StringComparison.OrdinalIgnoreCase);

    // Last-resort: rewrite the Database= / Initial Catalog= keyword
    // via SqlConnectionStringBuilder.
    var b = new SqlConnectionStringBuilder(template) { InitialCatalog = databaseName };
    return b.ConnectionString;
}

static async Task<List<TenantRow>> ReadActiveTenantsAsync(string masterConnString)
{
    var rows = new List<TenantRow>();
    using var conn = new SqlConnection(masterConnString);
    await conn.OpenAsync();
    using var cmd = conn.CreateCommand();
    cmd.CommandText =
        "SELECT Code, DatabaseName FROM master.Tenants " +
        "WHERE Status = 'Active' ORDER BY Code";
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        rows.Add(new TenantRow(reader.GetString(0), reader.GetString(1)));
    }
    return rows;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage: dotnet run -- <command> [<db>]

          command : up | down | list | version
          db      : master | tenant | tenants   (default: master)

        Examples:
          dotnet run -- up master         # apply Master migrations
          dotnet run -- up tenant         # apply Tenant migrations to TenantTemplate DB
          dotnet run -- up tenants        # apply Tenant migrations to EVERY active tenant DB
          dotnet run -- list tenant
          dotnet run -- version

        Phase 26 deployment-aware:
          The 'tenants' mode reads master.Tenants (Status='Active') and runs
          Tenant-tagged migrations against each tenant's DatabaseName. Stops on
          first failure; safe to re-run (FluentMigrator VersionInfo is per-DB).
        """);
}

static void PrintList(IServiceProvider sp, string tag)
{
    var loader = sp.GetRequiredService<IMigrationInformationLoader>();

    SortedList<long, IMigrationInfo>? migrations = null;
    try
    {
        migrations = loader.LoadMigrations();
    }
    catch (Exception ex) when (ex.Message.Contains("No migrations found", StringComparison.OrdinalIgnoreCase))
    {
        // FluentMigrator throws when the assembly has no [Migration] types. Treat as empty.
    }

    var count = migrations?.Count ?? 0;
    Console.WriteLine($"Migrations tagged '{tag}': {count}");
    if (count == 0)
    {
        Console.WriteLine("(none)");
        return;
    }

    HashSet<long>? applied = null;
    try
    {
        var versionLoader = sp.GetRequiredService<IVersionLoader>();
        applied = versionLoader.VersionInfo.AppliedMigrations().ToHashSet();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"(applied/pending status unavailable: {ex.Message})");
    }

    Console.WriteLine($"{"Version",-15} {"Status",-10} {"Description"}");
    Console.WriteLine(new string('-', 70));
    foreach (var m in migrations!)
    {
        var status = applied is null
            ? "[unknown]"
            : applied.Contains(m.Key) ? "[applied]" : "[pending]";
        Console.WriteLine($"{m.Key,-15} {status,-10} {m.Value.Migration.GetType().Name}");
    }
}

static void PrintVersion(IServiceProvider sp)
{
    var versionLoader = sp.GetRequiredService<IVersionLoader>();
    var info = versionLoader.VersionInfo;
    var latest = info.Latest();
    Console.WriteLine($"Latest applied version: {(latest == 0 ? "(none)" : latest.ToString())}");
}

internal record TenantRow(string Code, string DatabaseName);
