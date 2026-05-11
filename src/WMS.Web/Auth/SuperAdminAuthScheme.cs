namespace WMS.Web.Auth;

// Phase 27 — second auth scheme constant for /SuperAdmin/.
//
// Used by SuperAdminAuthController.SignInAsync (issues the
// wms.superauth cookie) + RequireSuperAdminAttribute (resolves the
// scheme on incoming SuperAdmin requests).
public static class SuperAdminAuthScheme
{
    public const string Name = "SuperAdminAuth";
}
