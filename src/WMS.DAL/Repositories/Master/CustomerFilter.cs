namespace WMS.DAL.Repositories.Master;

// Inputs to ICustomerRepository.GetPagedAsync. Mirrors ProductFilter's
// shape; field set matches the existing /Customers query parameters
// (CustomerType, Country) so the JSON contract on the wire is unchanged
// after the controller swap in T9.
public sealed record CustomerFilter(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    string? Status = null,
    string? CustomerType = null,
    string? Country = null,
    string? SortBy = null,
    bool SortDesc = false);
