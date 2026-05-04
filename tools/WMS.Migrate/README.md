# WMS.Migrate

FluentMigrator-based runner for the WMS Master DB and Tenant DB schemas.

## Run

```bash
cd tools/WMS.Migrate

dotnet run -- <command> [<db>]
```

| Command   | Effect                                         |
| --------- | ---------------------------------------------- |
| `up`      | Apply all pending migrations                   |
| `down`    | Roll back the most recently applied migration |
| `list`    | List migrations in the assembly (no DB query) |
| `version` | Show the latest applied version (queries DB)  |

`<db>` is `master` or `tenant`. Defaults to `master` when omitted.

Examples:

```bash
dotnet run -- up master
dotnet run -- list tenant
dotnet run -- down master
dotnet run -- version
```

Connection strings live in `appsettings.json` and can be overridden on the command line, e.g.

```bash
dotnet run -- up master --ConnectionStrings:MasterDb="Server=other;..."
```

## Adding a new migration

1. Pick a folder: `Migrations/Master/` or `Migrations/Tenant/`.
2. Pick a version number `YYYYMMDDNNN` — date plus a 3-digit sequence within that day, e.g. `20260504001`.
3. File and class name follow `Migration_YYYYMMDD_NNN_Description.cs`.
4. Inherit `MigrationBase` (NOT FluentMigrator's `Migration` directly) to reuse `AddAuditFields` and `CreateIndex`.
5. Tag the migration so the runner picks it up only for the right database.

```csharp
using FluentMigrator;

namespace WMS.Migrate.Migrations.Master;

[Migration(20260504001L)]
[Tags("Master")]
public class Migration_20260504_001_CreateTenantsTable : MigrationBase
{
    public override void Up()
    {
        Create.Schema("master");

        var t = Create.Table("Tenants").InSchema("master")
            .WithColumn("Id").AsGuid().PrimaryKey()
            .WithColumn("Name").AsString(200).NotNullable();

        AddAuditFields(t);

        CreateIndex("master", "Tenants", "Name");
    }

    public override void Down()
    {
        Delete.Table("Tenants").InSchema("master");
    }
}
```

## Troubleshooting

- **"Cannot open database WMS_Master requested by the login"** — the database does not exist. Create it first in SSMS / `sqlcmd`:
  `CREATE DATABASE WMS_Master;`
- **"Login failed for user"** — `Trusted_Connection=true` requires that the OS user running `dotnet` has access. Either run as that user, or switch to SQL auth in the connection string.
- **TLS / certificate error** — make sure `TrustServerCertificate=true` is in the connection string. Required for SQL Server 2022 with the default self-signed cert.
- **"No migrations found"** — confirm the migration is tagged correctly (`[Tags("Master")]` vs `[Tags("Tenant")]`) and that `[Migration(...)]` is present.
