using FluentMigrator.Infrastructure;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
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

if (db is not ("master" or "tenant"))
{
    Console.Error.WriteLine($"Unknown db: '{db}' — expected 'master' or 'tenant'");
    PrintUsage();
    return 1;
}

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddCommandLine(args)
    .Build();

var connKey = db == "tenant" ? "TenantTemplate" : "MasterDb";
var connString = config.GetConnectionString(connKey)
    ?? throw new InvalidOperationException(
        $"ConnectionString '{connKey}' not configured in appsettings.json");

var tag = db == "tenant" ? "Tenant" : "Master";

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

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage: dotnet run -- <command> [<db>]

          command : up | down | list | version
          db      : master | tenant   (default: master)

        Examples:
          dotnet run -- up master
          dotnet run -- list tenant
          dotnet run -- version
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

    Console.WriteLine($"{"Version",-18} {"Description"}");
    Console.WriteLine(new string('-', 60));
    foreach (var m in migrations!)
    {
        Console.WriteLine($"{m.Key,-18} {m.Value.Migration.GetType().Name}");
    }
}

static void PrintVersion(IServiceProvider sp)
{
    var versionLoader = sp.GetRequiredService<IVersionLoader>();
    var info = versionLoader.VersionInfo;
    var latest = info.Latest();
    Console.WriteLine($"Latest applied version: {(latest == 0 ? "(none)" : latest.ToString())}");
}
