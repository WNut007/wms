using FluentValidation;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;
using WMS.Web.Infrastructure;
using WMS.Web.Infrastructure.HealthChecks;
using WMS.Web.Services.Outbound;
using WMS.BLL.Services.Auth;
using WMS.BLL.Services.Security;
using WMS.BLL.Services.SuperAdmin;
using WMS.Web.Auth;
using WMS.Web.Services.SuperAdmin;
using WMS.Common.Auth;
using WMS.Common.Multitenancy;
using WMS.BLL.Services.Counts;
using WMS.BLL.Services.Inbound;
using WMS.BLL.Services.Inventory;
using WMS.BLL.Services.Outbound;
using WMS.BLL.Strategies.Allocation;
using WMS.DAL.Repositories.Counts;
using WMS.DAL.Multitenancy;
using WMS.DAL.Repositories.Documents;
using WMS.DAL.Repositories.Inbound;
using WMS.DAL.Repositories.Inventory;
using WMS.DAL.Repositories.Master;
using WMS.DAL.Repositories.Outbound;
using WMS.DAL.Repositories.Reports;
using WMS.DAL.Repositories.Security;
using WMS.Web.Auth;
using WMS.Web.Multitenancy;
using WMS.Web.Services.Storage;

var builder = WebApplication.CreateBuilder(args);

// Phase 26 — fail-fast on missing production config (empty MasterDb /
// TenantTemplate). Dev gets a warning + continues; Production throws.
ConfigurationValidator.Validate(builder.Configuration, builder.Environment);

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
//
// Phase 27 — adds a SECOND cookie scheme "SuperAdminAuth" for
// /SuperAdmin/ surfaces. Distinct cookie name (wms.superauth) +
// distinct LoginPath so a compromised tenant cookie can't grant
// access to SuperAdmin surfaces and vice-versa.
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
    })
    .AddCookie(SuperAdminAuthScheme.Name, opts =>
    {
        opts.Cookie.Name = "wms.superauth";
        opts.Cookie.HttpOnly = true;
        opts.Cookie.SameSite = SameSiteMode.Lax;
        opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opts.LoginPath = "/SuperAdmin/Login";
        opts.AccessDeniedPath = "/SuperAdmin/Login";
        opts.ExpireTimeSpan = TimeSpan.FromHours(4);   // shorter than tenant — admin sessions decay faster
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
builder.Services.AddScoped<ILocationRepositoryFactory, LocationRepositoryFactory>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IReceivingService, ReceivingService>();
builder.Services.AddScoped<IPurchaseOrderRepositoryFactory, PurchaseOrderRepositoryFactory>();
builder.Services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
builder.Services.AddScoped<IReceivingHeaderRepositoryFactory, ReceivingHeaderRepositoryFactory>();
builder.Services.AddScoped<IReceivingHeaderService, ReceivingHeaderService>();
builder.Services.AddScoped<IPutawayService, PutawayService>();

// Phase 11A — Stock Adjustments (ADR-013).
builder.Services.AddScoped<IAdjustmentRepositoryFactory, AdjustmentRepositoryFactory>();
builder.Services.AddScoped<IAdjustmentService, AdjustmentService>();

// Phase 12 — Cycle Counts (counts.* domain).
builder.Services.AddScoped<ICycleCountRepositoryFactory, CycleCountRepositoryFactory>();
builder.Services.AddScoped<ICycleCountService, CycleCountService>();

// Phase 13 — Inter-warehouse Transfers (ADR-012).
builder.Services.AddScoped<ITransferOrderRepositoryFactory, TransferOrderRepositoryFactory>();
builder.Services.AddScoped<ITransferOrderService, TransferOrderService>();

// Phase 14A — Outbound Sales Orders (MVP foundation).
builder.Services.AddScoped<ISalesOrderRepositoryFactory, SalesOrderRepositoryFactory>();
builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();

// Phase 14B — Allocation primitive (ADR-005).
builder.Services.AddScoped<IOrderAllocationRepositoryFactory, OrderAllocationRepositoryFactory>();
builder.Services.AddScoped<IAllocationService, AllocationService>();
// Strategy registrations — every IAllocationStrategy here flows into
// the resolver's DI-injected IEnumerable<>. Adding FEFO/Tier/etc.
// later is one more AddScoped line; no service-code change.
builder.Services.AddScoped<IAllocationStrategy, FifoAllocationStrategy>();
builder.Services.AddScoped<IAllocationStrategyResolver, AllocationStrategyResolver>();

// Phase 14C — Pick task generation + execution.
builder.Services.AddScoped<IPickTaskRepositoryFactory, PickTaskRepositoryFactory>();
builder.Services.AddScoped<IPickTaskService, PickTaskService>();

// Phase 14D — Pack task workflow.
builder.Services.AddScoped<IPackTaskRepositoryFactory, PackTaskRepositoryFactory>();
builder.Services.AddScoped<ICartonRepositoryFactory, CartonRepositoryFactory>();
builder.Services.AddScoped<IBoxTypeRepositoryFactory, BoxTypeRepositoryFactory>();
builder.Services.AddScoped<IPackTaskService, PackTaskService>();

// Phase 14E — Ship workflow.
builder.Services.AddScoped<IShipmentRepositoryFactory, ShipmentRepositoryFactory>();
builder.Services.AddScoped<IShipmentService, ShipmentService>();

// Phase 17 (ADR-009) — Pack video.
builder.Services.AddScoped<IPackVideoRepositoryFactory, PackVideoRepositoryFactory>();
builder.Services.AddScoped<IPackVideoService, PackVideoService>();

// Phase 23 — Reports aggregation surface.
builder.Services.AddScoped<IReportRepositoryFactory, ReportRepositoryFactory>();

// Phase 24 — Tenant Admin: Users / Roles / AuditLog.
builder.Services.AddScoped<IUserRoleRepositoryFactory, UserRoleRepositoryFactory>();
builder.Services.AddScoped<IRoleRepositoryFactory, RoleRepositoryFactory>();
builder.Services.AddScoped<IFunctionRepositoryFactory, FunctionRepositoryFactory>();
builder.Services.AddScoped<IAuditLogRepositoryFactory, AuditLogRepositoryFactory>();
builder.Services.AddScoped<ISecurityService, SecurityService>();

// Phase 27 — SuperAdmin (master DB) + tenant provisioning.
builder.Services.AddScoped<ISuperAdminRepository, SuperAdminRepository>();
builder.Services.AddScoped<ISystemAuditLogRepository, SystemAuditLogRepository>();
builder.Services.AddScoped<ISuperAdminAuthService, SuperAdminAuthService>();
builder.Services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();

// Phase 17 — pack-video retention. Binds RetentionDays + CronSchedule
// from "PackVideoRetention" section. Job registered as Scoped so
// Hangfire's per-execution scope provides fresh repo factories.
// Recurring schedule registered post-build (see RecurringJob.AddOr-
// Update call below).
builder.Services.Configure<PackVideoRetentionOptions>(
    builder.Configuration.GetSection(PackVideoRetentionOptions.SectionName));
builder.Services.AddScoped<PackVideoRetentionCleanupJob>();

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
//
// Phase 25 wires IAuditLogRepositoryFactory + ILoginRateLimiter — audits
// LoginSuccess / LoginFailure / AccountLockout to the user's primary
// tenant DB; rate-limits brute-force attempts at 5/min per IP.
builder.Services.AddSingleton<ILoginRateLimiter>(sp =>
    new LoginRateLimiter(sp.GetRequiredService<IMemoryCache>()));
builder.Services.AddScoped<IAuthService>(sp => new AuthService(
    sp.GetRequiredService<IUserRepositoryFactory>(),
    sp.GetRequiredService<IUserTenantMapRepository>(),
    sp.GetRequiredService<IMasterConnectionFactory>(),
    sp.GetRequiredService<ILogger<AuthService>>(),
    bcryptCostFactor: builder.Environment.IsDevelopment() ? 4 : 12,
    auditRepoFactory: sp.GetRequiredService<IAuditLogRepositoryFactory>(),
    rateLimiter: sp.GetRequiredService<ILoginRateLimiter>()));

// Phase 17 — Hangfire on the MasterDb (system DB; single dashboard,
// single job queue across the deployment — NOT per-tenant). Schema
// auto-prepared on first run via PrepareSchemaIfNecessary=true.
// Fire-and-forget jobs work immediately; recurring jobs registered
// after build (see PackVideoRetentionCleanupJob).
var hangfireConn = builder.Configuration.GetConnectionString("MasterDb")
    ?? throw new InvalidOperationException("MasterDb connection string is required for Hangfire.");
builder.Services.AddHangfire(cfg => cfg
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(hangfireConn, new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true,
        PrepareSchemaIfNecessary = true,
        SchemaName = "HangFire",
    }));
builder.Services.AddHangfireServer(opts =>
{
    opts.WorkerCount = Math.Min(4, Environment.ProcessorCount);
    opts.ServerName = $"{Environment.MachineName}:wms-web";
});

// Phase 26 — health checks. /healthz/live = process alive; /healthz/ready
// = process alive + Master DB reachable. Both anonymous; load-balancer
// probes shouldn't need auth.
builder.Services.AddHealthChecks()
    .AddCheck<MasterDbHealthCheck>("master-db", tags: new[] { "ready" });

// Phase 26 — security headers. Bind from SecurityHeaders config section
// so CSP is tunable per environment.
var securityHeaders = new SecurityHeadersOptions();
builder.Configuration.GetSection(SecurityHeadersOptions.SectionName)
    .Bind(securityHeaders);
builder.Services.AddSingleton(securityHeaders);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // Phase 26 — production error handling. UseExceptionHandler catches
    // unhandled exceptions; StatusCodePagesWithReExecute routes 4xx/5xx
    // responses (without a body) through the ErrorController so the
    // operator sees a styled page instead of a blank Kestrel reply.
    // Both gated to non-Development so the dev exception page stays in
    // place locally.
    app.UseExceptionHandler("/Error/500");
    app.UseStatusCodePagesWithReExecute("/Error/{0}");
    app.UseHsts();
}

// Phase 26 — security headers. Sits as early as possible in the
// pipeline so even error responses carry the hardening headers.
app.UseSecurityHeaders(securityHeaders);

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSerilogRequestLogging();
app.UseRouting();

app.UseAuthentication();
app.UseTenantValidation();
app.UseAuthorization();

// Phase 26 — health probes. /healthz/live is fast (no DB), /healthz/ready
// runs the Master DB check. /health kept for backwards compat with any
// existing monitoring hooks; routes to /healthz/live shape.
app.MapHealthChecks("/healthz/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,    // no checks executed; pure liveness
});
app.MapHealthChecks("/healthz/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteJson,
});
app.MapHealthChecks("/healthz", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteJson,
});
// Backwards-compat with the Phase 17 minimal endpoint.
app.MapGet("/health", () => "Healthy");

// Phase 17 — Hangfire dashboard. Auth filter requires
// IsAuthenticated (MVP — tightening to ADMIN role check is a TD).
app.MapHangfireDashboard("/hangfire", new Hangfire.DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthFilter() },
    DashboardTitle = "WMS Jobs",
    StatsPollingInterval = 5000,
});

// Phase 17 (ADR-009) — register the daily pack-video retention
// cleanup. AddOrUpdate is idempotent across restarts (uses the
// stable JobId from PackVideoRetentionOptions). Cron resolved from
// config; default is "0 3 * * *" (03:00 UTC daily).
{
    var retentionOptions = app.Services.GetRequiredService<IOptions<PackVideoRetentionOptions>>().Value;
    RecurringJob.AddOrUpdate<PackVideoRetentionCleanupJob>(
        retentionOptions.JobId,
        job => job.ExecuteAsync(CancellationToken.None),
        retentionOptions.CronSchedule,
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Phase 27 — bootstrap initial SuperAdmin from config on first run.
// Idempotent + skips silently when InitialSuperAdmin config is absent.
using (var scope = app.Services.CreateScope())
{
    var bootstrapLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        await SuperAdminBootstrap.EnsureAsync(scope.ServiceProvider, bootstrapLogger);
    }
    catch (Exception ex)
    {
        // Don't crash boot — bootstrap failure (DB unreachable, etc.)
        // should be loud in logs but the app still starts so the
        // operator can investigate.
        bootstrapLogger.LogError(ex, "SuperAdmin bootstrap failed — continuing startup.");
    }
}

app.Run();
