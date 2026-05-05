using System.Data;

namespace WMS.Common.Multitenancy;

// Creates a SqlConnection to the master DB. Separate from
// ITenantConnectionFactory because the master DB is a fixed singleton
// — there's no tenantId to look up — and consumers (AuthService,
// PreAuthToken store, LoginAttempts logger) want a dependency that
// doesn't pretend otherwise. Caller owns the returned connection.
public interface IMasterConnectionFactory
{
    IDbConnection CreateConnection();
}
