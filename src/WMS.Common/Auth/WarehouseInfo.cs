namespace WMS.Common.Auth;

// Warehouse the requesting user can pick at Step 3 of the login flow.
// Returned by IWarehouseRepository (DAL) and surfaced through
// WarehouseSelectViewModel (Web) — defined in Common so neither layer
// references the other.
public sealed record WarehouseInfo(
    Guid Id,
    string Code,
    string Name);
