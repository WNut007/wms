-- Phase 30A.2 — Per-tenant pollution audit
--
-- Goal: enumerate which tenant DBs contain demo-seed pollution and
-- whether any polluted rows have downstream operational references
-- (FK NO ACTION blocks naive cleanup).
--
-- This script READS ONLY. No DELETE / UPDATE / INSERT.
--
-- USAGE — run twice:
--
--   (1) Against WMS_Master to list tenant DBs:
--       USE WMS_Master;
--       SELECT DatabaseName, Code, Status FROM master.Tenants
--         ORDER BY Code;
--
--   (2) For EACH tenant DB returned (EXCEPT WMS_Tenant_Template —
--       that one is intentionally seeded with demo data), run the
--       BLOCK below with USE [<DbName>] at the top. The block returns
--       11 rows per DB summarising what's polluted.
--
--   Example invocation pattern:
--       USE WMS_Tenant_GEPLUS;  GO  <paste BLOCK below>
--       USE WMS_Tenant_<NEXT>;  GO  <paste BLOCK again>
--
--   Or wrap in a sqlcmd :setvar loop / Powershell foreach if many tenants.

-- ─────────────────────── BLOCK (run per tenant DB) ───────────────────

SET NOCOUNT ON;

SELECT
    DB_NAME() AS DbName,
    'master.Warehouses' AS TablePath,
    'WH-DM%' AS CodePattern,
    COUNT(*) AS RowsPresent,
    (SELECT COUNT(*) FROM master.Warehouses w
     WHERE w.Code LIKE 'WH-DM%'
       AND (EXISTS (SELECT 1 FROM master.Locations l WHERE l.WarehouseId = w.Id)
         OR EXISTS (SELECT 1 FROM master.Zones z WHERE z.WarehouseId = w.Id))) AS RowsWithFkRefs,
    'Pure demo (Migration_508_007); expected 24' AS Notes
FROM master.Warehouses WHERE Code LIKE 'WH-DM%'

UNION ALL

SELECT DB_NAME(), 'master.Customers', 'CUST-[0-9]{4}',
    COUNT(*),
    (SELECT COUNT(DISTINCT c.Id) FROM master.Customers c
     WHERE c.Code LIKE 'CUST-[0-9][0-9][0-9][0-9]'
       AND EXISTS (SELECT 1 FROM outbound.SalesOrders so WHERE so.CustomerId = c.Id)),
    'Pure demo (Migration_508_006); expected 25'
FROM master.Customers WHERE Code LIKE 'CUST-[0-9][0-9][0-9][0-9]'

UNION ALL

SELECT DB_NAME(), 'master.Products', 'PROD-[0-9]{4}',
    COUNT(*),
    (SELECT COUNT(DISTINCT p.Id) FROM master.Products p
     WHERE p.Code LIKE 'PROD-[0-9][0-9][0-9][0-9]'
       AND (EXISTS (SELECT 1 FROM inventory.Stock s WHERE s.ProductId = p.Id)
         OR EXISTS (SELECT 1 FROM inbound.PurchaseOrderLines pol WHERE pol.ProductId = p.Id)
         OR EXISTS (SELECT 1 FROM outbound.SalesOrderLines sol WHERE sol.ProductId = p.Id))),
    'Pure demo (Migration_508_005); expected 24'
FROM master.Products WHERE Code LIKE 'PROD-[0-9][0-9][0-9][0-9]'

UNION ALL

SELECT DB_NAME(), 'master.Products', 'DEMO-001',
    COUNT(*),
    (SELECT COUNT(*) FROM master.Products p
     WHERE p.Code = 'DEMO-001'
       AND (EXISTS (SELECT 1 FROM inventory.Stock s WHERE s.ProductId = p.Id)
         OR EXISTS (SELECT 1 FROM inbound.PurchaseOrderLines pol WHERE pol.ProductId = p.Id)
         OR EXISTS (SELECT 1 FROM outbound.SalesOrderLines sol WHERE sol.ProductId = p.Id))),
    'Demo-named (Migration_052); expected 1'
FROM master.Products WHERE Code = 'DEMO-001'

UNION ALL

SELECT DB_NAME(), 'master.ProductCategories', 'DEMO',
    COUNT(*),
    (SELECT COUNT(*) FROM master.ProductCategories c
     WHERE c.Code = 'DEMO'
       AND EXISTS (SELECT 1 FROM master.Products p WHERE p.CategoryId = c.Id)),
    'Demo-named (Migration_052); expected 1'
FROM master.ProductCategories WHERE Code = 'DEMO'

UNION ALL

SELECT DB_NAME(), 'master.Owners', 'SELF',
    COUNT(*),
    (SELECT COUNT(*) FROM master.Owners o
     WHERE o.Code = 'SELF'
       AND EXISTS (SELECT 1 FROM inventory.Stock s WHERE s.OwnerId = o.Id)),
    'Bootstrap-shaped (Migration_052); RECOMMEND KEEP'
FROM master.Owners WHERE Code = 'SELF'

UNION ALL

SELECT DB_NAME(), 'master.UnitsOfMeasure', 'EA',
    COUNT(*),
    (SELECT COUNT(*) FROM master.UnitsOfMeasure u
     WHERE u.Code = 'EA'
       AND EXISTS (SELECT 1 FROM master.Products p WHERE p.BaseUomId = u.Id)),
    'Bootstrap-shaped (Migration_052); KEEP — required base UoM'
FROM master.UnitsOfMeasure WHERE Code = 'EA'

UNION ALL

SELECT DB_NAME(), 'master.Zones', 'RECV',
    (SELECT COUNT(*) FROM master.Zones z
       INNER JOIN master.Warehouses w ON w.Id = z.WarehouseId
       WHERE w.Code = 'WH-MAIN' AND z.Code = 'RECV'),
    (SELECT COUNT(*) FROM master.Zones z
       INNER JOIN master.Warehouses w ON w.Id = z.WarehouseId
       WHERE w.Code = 'WH-MAIN' AND z.Code = 'RECV'
       AND EXISTS (SELECT 1 FROM master.Locations l WHERE l.ZoneId = z.Id)),
    'Demo-named (Migration_052); expected 1'

UNION ALL

SELECT DB_NAME(), 'master.Locations', 'RECV-01',
    (SELECT COUNT(*) FROM master.Locations l
       INNER JOIN master.Warehouses w ON w.Id = l.WarehouseId
       WHERE w.Code = 'WH-MAIN' AND l.Code = 'RECV-01'),
    (SELECT COUNT(*) FROM master.Locations l
       INNER JOIN master.Warehouses w ON w.Id = l.WarehouseId
       WHERE w.Code = 'WH-MAIN' AND l.Code = 'RECV-01'
       AND EXISTS (SELECT 1 FROM inventory.Stock s WHERE s.LocationId = l.Id)),
    'Demo-named (Migration_052); expected 1'

UNION ALL

SELECT DB_NAME(), 'master.Zones', 'STORAGE',
    (SELECT COUNT(*) FROM master.Zones z
       INNER JOIN master.Warehouses w ON w.Id = z.WarehouseId
       WHERE w.Code = 'WH-MAIN' AND z.Code = 'STORAGE'),
    (SELECT COUNT(*) FROM master.Zones z
       INNER JOIN master.Warehouses w ON w.Id = z.WarehouseId
       WHERE w.Code = 'WH-MAIN' AND z.Code = 'STORAGE'
       AND EXISTS (SELECT 1 FROM master.Locations l WHERE l.ZoneId = z.Id)),
    'Demo-named (Migration_060); expected 1'

UNION ALL

SELECT DB_NAME(), 'master.Locations', 'STORAGE-A1',
    (SELECT COUNT(*) FROM master.Locations l
       INNER JOIN master.Warehouses w ON w.Id = l.WarehouseId
       WHERE w.Code = 'WH-MAIN' AND l.Code = 'STORAGE-A1'),
    (SELECT COUNT(*) FROM master.Locations l
       INNER JOIN master.Warehouses w ON w.Id = l.WarehouseId
       WHERE w.Code = 'WH-MAIN' AND l.Code = 'STORAGE-A1'
       AND EXISTS (SELECT 1 FROM inventory.Stock s WHERE s.LocationId = l.Id)),
    'Demo-named (Migration_060); expected 1'

ORDER BY TablePath, CodePattern;

-- ─────────────────────── END BLOCK ───────────────────────────────────
--
-- Interpretation:
--   RowsPresent       = how many polluting rows exist in this DB
--   RowsWithFkRefs    = how many of those are referenced by operational
--                       data (Stock, POs, SOs, etc.)
--   RowsSafeToDelete  = RowsPresent - RowsWithFkRefs
--
-- Rows marked "KEEP" / "RECOMMEND KEEP" should be retained — they are
-- functional defaults (UoM 'EA', Owner 'SELF') that real tenants
-- legitimately use.
--
-- All other rows are safe to delete from non-template DBs PROVIDED
-- RowsWithFkRefs = 0. If RowsWithFkRefs > 0 for a code pattern, the
-- tenant has actual data tied to the demo rows — manual review needed
-- (likely they accidentally used the demo rows as real data).
