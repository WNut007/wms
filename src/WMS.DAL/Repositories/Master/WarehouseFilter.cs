namespace WMS.DAL.Repositories.Master;

// Inputs to IWarehouseRepository.GetPagedAsync.
//
// Note IsActive is bool? — the controller boundary maps the existing
// /Warehouses?status=active|inactive wire format to this typed flag,
// keeping the SQL strongly-typed (master.Warehouses.IsActive is BIT).
// Mock UI's third 'maintenance' state is intentionally absent — schema
// has no Status string column, see TD-009.
public sealed record WarehouseFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    bool? IsActive = null,
    string? Type = null,
    string? SortBy = null,
    bool SortDesc = false);
