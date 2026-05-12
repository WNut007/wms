-- ============================================================
-- seed-demo-suppliers.sql — Block 4.5.2 smoke unblock
-- ============================================================
-- Purpose: seed 3 demo Owner rows (2 Supplier + 1 VMI) into a
-- dev tenant DB so PO Create's Vendor dropdown has data while
-- Block 4 (Owners CRUD) is still pending.
--
-- WHY a script and not a migration:
--   - Tenant-tagged migration would fan out to ALL tenants
--     including production-shape ones — wrong for demo data.
--   - Demo-tagged migration only reaches WMS_Tenant_Template,
--     which isn't the dev daily-use tenant (e.g., GEPLUS).
--   - Manual SQL stays surgical: hits exactly the dev tenants
--     the operator names, no fan-out risk.
--
-- USAGE:
--   1. Open SSMS against the SQL Server hosting your dev tenant
--   2. Edit the USE statement below to your dev tenant DB name
--   3. Run the script (F5)
--   4. Verify the SELECT at the bottom shows 3 rows
--   5. Refresh PO Create's Vendor dropdown; the 3 VND-* entries
--      appear (sorted by Code per OwnerRepository.GetActiveSuppliersAsync)
--
-- CLEANUP (when Block 4 ships proper Owners CRUD, optional —
-- operator-created Owners coexist fine with seeded ones):
--   DELETE FROM master.Owners WHERE Code LIKE 'VND-%';
--
-- IDEMPOTENCY: re-running the script against a tenant that
-- already has VND-* rows is a no-op (guarded by NOT EXISTS).
--
-- SCHEMA REFERENCE (master.Owners, Migration_20260504_011):
--   Code        AnsiString(20) NOT NULL UQ
--   Name        String(200)    NOT NULL
--   OwnerType   AnsiString(20) NOT NULL  CHECK IN ('Self','Supplier','VMI','Customer3PL')
--   IsActive    Bit            NOT NULL  DEFAULT 1
--   Id, CreatedAt have server-side DEFAULTs (NEWID, SYSUTCDATETIME)
--   All other columns nullable
-- ============================================================

USE [WMS_GEPLUS];  -- ADJUST to your dev tenant DB name
GO

IF NOT EXISTS (SELECT 1 FROM master.Owners WHERE Code LIKE 'VND-%')
BEGIN
    INSERT INTO master.Owners (Code, Name, OwnerType, IsActive)
    VALUES
        ('VND-001', 'Demo Supplier Alpha', 'Supplier', 1),
        ('VND-002', 'Demo Supplier Beta',  'Supplier', 1),
        ('VND-003', 'Demo VMI Partner',    'VMI',      1);

    PRINT 'Seeded 3 demo Owner suppliers (VND-001..VND-003).';
END
ELSE
BEGIN
    PRINT 'VND-* Owners already exist in this tenant — script skipped (idempotent).';
END
GO

-- Verification — should return 3 rows
SELECT Id, Code, Name, OwnerType, IsActive, CreatedAt
FROM master.Owners
WHERE Code LIKE 'VND-%'
ORDER BY Code;
