using System.Data;

namespace WMS.Common.Multitenancy;

// Creates a SqlConnection to the *tenant's* DB given its Id. Looks up
// master.Tenants for the database name + applies a configured connection
// string template, caching the resolved string. Caller owns the returned
// connection — open + dispose per Dapper conventions.
public interface ITenantConnectionFactory
{
    IDbConnection CreateConnection(Guid tenantId);
}
