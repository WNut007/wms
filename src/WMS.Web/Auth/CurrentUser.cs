using System.Security.Claims;
using WMS.Common.Auth;

namespace WMS.Web.Auth;

// HttpContext-backed ICurrentUser. Reads claims from the authenticated
// principal — no DB hit per request. Returns nulls / empty list rather
// than throwing for anonymous requests so the same instance can be
// resolved on the login page.
internal sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUser(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId => TryGuid(Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
    public Guid? TenantId => TryGuid(Principal?.FindFirstValue(WmsClaimTypes.TenantId));
    public Guid? WarehouseId => TryGuid(Principal?.FindFirstValue(WmsClaimTypes.WarehouseId));

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);
    public string? FullName => Principal?.FindFirstValue(ClaimTypes.Name);

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()
        ?? Array.Empty<string>();

    private static Guid? TryGuid(string? raw) =>
        Guid.TryParse(raw, out var g) ? g : null;
}
