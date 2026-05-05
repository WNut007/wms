using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Seeds the Phase 1 catalogue of permissionable functions. Codes are
// dotted MODULE.FUNCTION (uppercase, ASCII) — the dotted prefix
// mirrors the Module column so permission lookup can reason about
// either form. DisplayOrder is per-module (1..N within each Module),
// matching IX_Functions_Module's sort.
//
// Idempotent: existence-checked by Code (the unique key), so re-runs
// are safe.
//
// Phase 2+ modules (Marketplace, VMI, Forecast, Analytics) are
// intentionally omitted — they'll land alongside their respective
// screens in later chunks. The BLL is responsible for special-casing
// the ADMIN role to bypass the permission matrix, so functions added
// after this seed don't strand the admin without access.
[Migration(20260504042L)]
[Tags("Tenant")]
public class Migration_20260504_042_SeedDefaultFunctions : MigrationBase
{
    private static readonly (string Code, string Name, string Module, int DisplayOrder)[] Functions =
    {
        // Master
        ("MASTER.WAREHOUSES",   "Warehouses",                  "Master", 1),
        ("MASTER.PRODUCTS",     "Products",                    "Master", 2),
        ("MASTER.CUSTOMERS",    "Customers",                   "Master", 3),
        ("MASTER.OWNERS",       "Owners",                      "Master", 4),
        ("MASTER.CARRIERS",     "Carriers",                    "Master", 5),
        ("MASTER.CHANNELS",     "Channels",                    "Master", 6),
        ("MASTER.CATEGORIES",   "Product Categories",          "Master", 7),
        ("MASTER.UOM",          "Units of Measure",            "Master", 8),
        ("MASTER.HOLIDAYS",     "Holiday Calendar",            "Master", 9),

        // Inventory
        ("INVENTORY.STOCK",       "Stock",                     "Inventory", 1),
        ("INVENTORY.LOTS",        "Lots",                      "Inventory", 2),
        ("INVENTORY.PALLETS",     "Pallets",                   "Inventory", 3),
        ("INVENTORY.ADJUSTMENTS", "Stock Adjustments",         "Inventory", 4),
        ("INVENTORY.TRANSFERS",   "Inter-warehouse Transfers", "Inventory", 5),

        // Inbound
        ("INBOUND.PURCHASE_ORDERS", "Purchase Orders", "Inbound", 1),
        ("INBOUND.RECEIVING",       "Receiving",       "Inbound", 2),
        ("INBOUND.PUTAWAY",         "Putaway",         "Inbound", 3),

        // Outbound
        ("OUTBOUND.ORDERS",    "Orders",    "Outbound", 1),
        ("OUTBOUND.WAVES",     "Waves",     "Outbound", 2),
        ("OUTBOUND.PICKS",     "Picks",     "Outbound", 3),
        ("OUTBOUND.PACKS",     "Packs",     "Outbound", 4),
        ("OUTBOUND.SHIPMENTS", "Shipments", "Outbound", 5),

        // Returns
        ("RETURNS.RMAS", "Return Authorizations (RMAs)", "Returns", 1),

        // Counts
        ("COUNTS.CYCLE_COUNTS", "Cycle Counts", "Counts", 1),

        // Security
        ("SECURITY.USERS",     "Users",     "Security", 1),
        ("SECURITY.ROLES",     "Roles",     "Security", 2),
        ("SECURITY.FUNCTIONS", "Functions", "Security", 3),
        ("SECURITY.AUDIT_LOG", "Audit Log", "Security", 4),

        // Billing
        ("BILLING.RATE_CARDS", "Rate Cards", "Billing", 1),
        ("BILLING.INVOICES",   "Invoices",   "Billing", 2),
    };

    public override void Up()
    {
        foreach (var (code, name, module, displayOrder) in Functions)
        {
            // N-prefix on Name preserves any unicode the operator might
            // localize a function label to. Code / Module are ASCII per
            // their AsAnsiString column types.
            Execute.Sql(
                $"IF NOT EXISTS (SELECT 1 FROM [security].[Functions] WHERE Code = '{code}') " +
                $"INSERT INTO [security].[Functions] " +
                $"(Id, Code, Name, Module, DisplayOrder, IsActive, CreatedAt) " +
                $"VALUES (NEWID(), '{code}', N'{name}', '{module}', " +
                $"{displayOrder}, 1, SYSUTCDATETIME());");
        }
    }

    public override void Down()
    {
        // CASCADE on RoleFunctionPermissions sweeps any permission rows
        // referencing these functions. Rollback intentionally tears the
        // catalogue down.
        var codes = string.Join(", ", System.Array.ConvertAll(Functions, f => $"'{f.Code}'"));
        Execute.Sql(
            $"DELETE FROM [security].[Functions] WHERE Code IN ({codes});");
    }
}
