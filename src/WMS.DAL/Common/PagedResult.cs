namespace WMS.DAL.Common;

// Shared paged-query result. Lifted out of WMS.Web.Services.Mock so the
// real master-data repos (Phase 6B) and the doomed mock services can
// agree on the shape during the cutover. The Mock copy is removed in T11
// when the mock services are deleted; this is the canonical home.
public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
