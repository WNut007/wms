namespace WMS.DAL.Repositories.Master;

// Read-only projection for Customer list pages (/Customers list view).
// No JOIN aggregates — Phase 6B Customer list is a straight SELECT off
// master.Customers. TotalOrders / LastOrderDate stay stubbed in the
// controller until an orders schema lands (TD-011).
public sealed record CustomerListRow(
    Guid Id,
    string Code,
    string Name,
    string CustomerType,
    string? Country,
    string? Email,
    string Status,
    DateTime? UpdatedAt);
