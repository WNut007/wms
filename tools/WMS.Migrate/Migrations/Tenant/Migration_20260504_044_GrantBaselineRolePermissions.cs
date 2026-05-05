using FluentMigrator;

namespace WMS.Migrate.Migrations.Tenant;

// Baseline permission grants for the three non-admin built-in roles
// (Picker / Packer / Manager). ADMIN is already covered by 043 + the
// BLL bypass; this migration shapes the day-one matrix for everyone
// else.
//
// Each tuple is (FunctionCode, View, Add, Edit, Delete, Approve).
// Flags use 0/1 ints rather than bool so the SQL stays direct.
//
// Idempotent: each grant is inserted via NOT EXISTS, so re-applying
// after a partial failure adds zero rows.
//
// Down() is defensive: it deletes only the (role, function) pairs
// this migration grants. That preserves any custom permission rows
// an admin may add later through the UI on the same role.
[Migration(20260504044L)]
[Tags("Tenant")]
public class Migration_20260504_044_GrantBaselineRolePermissions : MigrationBase
{
    // Picker confirms scans (Edit on PICKS) and reads enough surrounding
    // context to make sense of what's being picked.
    private static readonly (string Code, int V, int A, int E, int D, int Ap)[] PickerPerms =
    {
        ("OUTBOUND.PICKS",  1, 0, 1, 0, 0),
        ("OUTBOUND.ORDERS", 1, 0, 0, 0, 0),
        ("INVENTORY.STOCK", 1, 0, 0, 0, 0),
        ("MASTER.PRODUCTS", 1, 0, 0, 0, 0),
    };

    // Packer mirrors Picker but on the pack side, plus shipment edit
    // for routing the parcel.
    private static readonly (string Code, int V, int A, int E, int D, int Ap)[] PackerPerms =
    {
        ("OUTBOUND.PACKS",     1, 0, 1, 0, 0),
        ("OUTBOUND.SHIPMENTS", 1, 0, 1, 0, 0),
        ("OUTBOUND.ORDERS",    1, 0, 0, 0, 0),
        ("MASTER.PRODUCTS",    1, 0, 0, 0, 0),
    };

    // Manager is operational oversight: broad View, edit + approve on
    // operational flows, no schema admin (USERS / ROLES / FUNCTIONS
    // belong to ADMIN). Master tables stay read-only for Manager —
    // ADMIN owns master maintenance.
    private static readonly (string Code, int V, int A, int E, int D, int Ap)[] ManagerPerms =
    {
        // Master — view-only
        ("MASTER.WAREHOUSES", 1, 0, 0, 0, 0),
        ("MASTER.PRODUCTS",   1, 0, 0, 0, 0),
        ("MASTER.CUSTOMERS",  1, 0, 0, 0, 0),
        ("MASTER.OWNERS",     1, 0, 0, 0, 0),
        ("MASTER.CARRIERS",   1, 0, 0, 0, 0),
        ("MASTER.CHANNELS",   1, 0, 0, 0, 0),
        ("MASTER.CATEGORIES", 1, 0, 0, 0, 0),
        ("MASTER.UOM",        1, 0, 0, 0, 0),
        ("MASTER.HOLIDAYS",   1, 0, 0, 0, 0),

        // Inventory — view; create/edit/approve on the manual-action tables
        ("INVENTORY.STOCK",       1, 0, 0, 0, 0),
        ("INVENTORY.LOTS",        1, 0, 0, 0, 0),
        ("INVENTORY.PALLETS",     1, 0, 0, 0, 0),
        ("INVENTORY.ADJUSTMENTS", 1, 1, 1, 0, 1),
        ("INVENTORY.TRANSFERS",   1, 1, 1, 0, 1),

        // Inbound — view + edit + approve (qty deviations, dock changes)
        ("INBOUND.PURCHASE_ORDERS", 1, 0, 1, 0, 1),
        ("INBOUND.RECEIVING",       1, 0, 1, 0, 1),
        ("INBOUND.PUTAWAY",         1, 0, 1, 0, 1),

        // Outbound — view + edit + approve (cancellations, exception routing)
        ("OUTBOUND.ORDERS",    1, 0, 1, 0, 1),
        ("OUTBOUND.WAVES",     1, 0, 1, 0, 1),
        ("OUTBOUND.PICKS",     1, 0, 1, 0, 1),
        ("OUTBOUND.PACKS",     1, 0, 1, 0, 1),
        ("OUTBOUND.SHIPMENTS", 1, 0, 1, 0, 1),

        // Returns + Counts — full operational including create
        ("RETURNS.RMAS",        1, 1, 1, 0, 1),
        ("COUNTS.CYCLE_COUNTS", 1, 1, 1, 0, 1),

        // Billing — view rate cards; approve invoices but don't author them
        ("BILLING.RATE_CARDS", 1, 0, 0, 0, 0),
        ("BILLING.INVOICES",   1, 0, 0, 0, 1),

        // Security — only the audit trail; user / role admin stays with ADMIN
        ("SECURITY.AUDIT_LOG", 1, 0, 0, 0, 0),
    };

    public override void Up()
    {
        Grant("PICKER",  PickerPerms);
        Grant("PACKER",  PackerPerms);
        Grant("MANAGER", ManagerPerms);
    }

    public override void Down()
    {
        Revoke("PICKER",  PickerPerms);
        Revoke("PACKER",  PackerPerms);
        Revoke("MANAGER", ManagerPerms);
    }

    // Inserts one (role, function) permission row when it isn't already
    // present. Lookup by natural keys (Role.Code + Function.Code) keeps
    // this migration insulated from the IDs the seed migrations chose.
    private void Grant(
        string roleCode,
        (string Code, int V, int A, int E, int D, int Ap)[] perms)
    {
        foreach (var (code, v, a, e, d, ap) in perms)
        {
            Execute.Sql($@"
INSERT INTO [security].[RoleFunctionPermissions]
    (Id, RoleId, FunctionId, CanView, CanAdd, CanEdit, CanDelete, CanApprove, CreatedAt)
SELECT NEWID(), r.Id, f.Id, {v}, {a}, {e}, {d}, {ap}, SYSUTCDATETIME()
FROM [security].[Roles] r
CROSS JOIN [security].[Functions] f
WHERE r.Code = '{roleCode}' AND f.Code = '{code}'
  AND NOT EXISTS (
      SELECT 1 FROM [security].[RoleFunctionPermissions] rfp
      WHERE rfp.RoleId = r.Id AND rfp.FunctionId = f.Id
  );");
        }
    }

    // Defensive revoke: deletes only the (role, function) pairs this
    // migration granted. Custom permission rows an admin may add to
    // these roles later survive a Down().
    private void Revoke(
        string roleCode,
        (string Code, int V, int A, int E, int D, int Ap)[] perms)
    {
        foreach (var (code, _, _, _, _, _) in perms)
        {
            Execute.Sql($@"
DELETE rfp
FROM [security].[RoleFunctionPermissions] rfp
JOIN [security].[Roles] r     ON r.Id = rfp.RoleId
JOIN [security].[Functions] f ON f.Id = rfp.FunctionId
WHERE r.Code = '{roleCode}' AND f.Code = '{code}';");
        }
    }
}
