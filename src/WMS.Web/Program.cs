using FluentValidation;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using WMS.BLL.Services.Auth;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.BLL.Services.Inbound;
using WMS.BLL.Services.Inventory;
using WMS.DAL.Multitenancy;
using WMS.DAL.Repositories.Documents;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Security;
using WMS.Web.Auth;
using WMS.Web.Multitenancy;
using WMS.Web.Services.Storage;

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

// Master DB factory — singleton because the connection string is fixed.
builder.Services.AddSingleton<IMasterConnectionFactory>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var master = cfg.GetConnectionString("MasterDb")
        ?? throw new InvalidOperationException(
            "ConnectionString 'MasterDb' is not configured.");
    return new MasterConnectionFactory(master);
});

builder.Services.AddScoped<IUserRepositoryFactory, UserRepositoryFactory>();
builder.Services.AddScoped<IUserTenantMapRepository, UserTenantMapRepository>();
builder.Services.AddScoped<IWarehouseRepositoryFactory, WarehouseRepositoryFactory>();
builder.Services.AddScoped<IPermissionRepositoryFactory, PermissionRepositoryFactory>();
builder.Services.AddScoped<IStockRepositoryFactory, StockRepositoryFactory>();
builder.Services.AddScoped<IStockMovementRepositoryFactory, StockMovementRepositoryFactory>();
builder.Services.AddScoped<ILotRepositoryFactory, LotRepositoryFactory>();
builder.Services.AddScoped<IPalletRepositoryFactory, PalletRepositoryFactory>();
builder.Services.AddScoped<IProductRepositoryFactory, ProductRepositoryFactory>();
builder.Services.AddScoped<ICustomerRepositoryFactory, CustomerRepositoryFactory>();
builder.Services.AddScoped<IProductCategoryRepositoryFactory, ProductCategoryRepositoryFactory>();
builder.Services.AddScoped<IUomRepositoryFactory, UomRepositoryFactory>();
builder.Services.AddScoped<ICarrierRepositoryFactory, CarrierRepositoryFactory>();
builder.Services.AddScoped<IOwnerRepositoryFactory, OwnerRepositoryFactory>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IReceivingService, ReceivingService>();
builder.Services.AddScoped<IPurchaseOrderRepositoryFactory, PurchaseOrderRepositoryFactory>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<IReceivingHeaderRepositoryFactory, ReceivingHeaderRepositoryFactory>();
builder.Services.AddScoped<IReceivingHeaderService, ReceivingHeaderService>();
builder.Services.AddScoped<IPutawayService, PutawayService>();

// PermissionService — Scoped to match the (Scoped) factory dep. The
// cache itself lives on IMemoryCache (Singleton), so per-request
// instances cost nothing and survive the captive-dependency check.
builder.Services.AddScoped<IPermissionService, PermissionService>();

// Phase 5 document storage. "Local" writes bytes to disk under
// Storage:Local:RootPath and persists metadata to documents.Files in the
// tenant DB. "Mock" keeps the Phase 4 in-memory store (handy for tests
// that don't want real I/O). Anything else is a config typo.
//
// LocalFileStorageService is Scoped because it captures ITenantContext
// (which captures HttpContext) — a singleton would freeze the first
// request's tenant for everyone.
builder.Services.AddOptions<DocumentStorageOptions>()
    .Bind(builder.Configuration.GetSection(DocumentStorageOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddScoped<IDocumentRepositoryFactory, DocumentRepositoryFactory>();

var storageProvider = builder.Configuration[$"{DocumentStorageOptions.SectionName}:Provider"]
                      ?? "Local";
if (storageProvider.Equals("Mock", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IDocumentStorageService, MockDocumentStorageService>();
}
else
{
    builder.Services.AddScoped<IDocumentStorageService, LocalFileStorageService>();
}

// Tenant active-status reader — singleton so its IMemoryCache reference
// is shared. TenantValidationMiddleware reads through this on every
// authenticated request.
builder.Services.AddSingleton<ITenantStatusReader, TenantStatusReader>();

// FluentValidation — server-side validators for Phase 7 admin CRUD
// view-models. DataAnnotations on the view-models drive jQuery
// unobtrusive client-side validation; FluentValidation runs server-side
// for cross-field + business rules ("B2B requires CompanyName + TaxId",
// pre-flight Code uniqueness). Controllers call IValidator<T> manually
// after the ModelState DA pass — keeps each layer pure, no
// auto-validation magic / deprecated FluentValidation.AspNetCore
// package needed.
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// AuthService BCrypt cost factor: 12 in prod (~250ms/hash), 4 in
// Development (~5ms) so dev seed scripts and local testing don't crawl.
// Test suite already uses 4 directly via AuthServiceTests.
builder.Services.AddScoped<IAuthService>(sp => new AuthService(
    sp.GetRequiredService<IUserRepositoryFactory>(),
    sp.GetRequiredService<IUserTenantMapRepository>(),
    sp.GetRequiredService<IMasterConnectionFactory>(),
    sp.GetRequiredService<ILogger<AuthService>>(),
    bcryptCostFactor: builder.Environment.IsDevelopment() ? 4 : 12));

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
app.UseTenantValidation();
app.UseAuthorization();

app.MapGet("/health", () => "Healthy");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
