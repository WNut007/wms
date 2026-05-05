using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.DAL.Multitenancy;
using WMS.Web.Auth;
using WMS.Web.Multitenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// Cookie auth for the 3-step login flow (ADR-008). Cookie name kept
// short so the request header stays small; SlidingExpiration so users
// don't get bounced out mid-shift.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opts =>
    {
        opts.Cookie.Name = "wms.auth";
        opts.Cookie.HttpOnly = true;
        opts.Cookie.SameSite = SameSiteMode.Lax;
        opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opts.LoginPath = "/Auth/Login";
        opts.AccessDeniedPath = "/Auth/Forbidden";
        opts.ExpireTimeSpan = TimeSpan.FromHours(8);
        opts.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// Per-request identity + tenancy reads. Scoped because they capture
// HttpContext, which is itself per-request.
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// Tenant connection factory uses IMemoryCache (5-min sliding) so we hit
// master.Tenants at most once per tenant per 5 minutes. Singleton so the
// cache reference is shared across all requests.
builder.Services.AddSingleton<ITenantConnectionFactory>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var cache = sp.GetRequiredService<IMemoryCache>();
    var master = cfg.GetConnectionString("MasterDb")
        ?? throw new InvalidOperationException(
            "ConnectionString 'MasterDb' is not configured.");
    var template = cfg.GetConnectionString("TenantTemplate")
        ?? throw new InvalidOperationException(
            "ConnectionString 'TenantTemplate' is not configured. " +
            "Expected a template containing '{0}' for the database name.");
    return new TenantConnectionFactory(master, template, cache);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSerilogRequestLogging();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => "Healthy");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
