using System.Data;
using Microsoft.Data.SqlClient;
using WMS.Common.Multitenancy;

namespace WMS.DAL.Multitenancy;

// Thin wrapper around a fixed master connection string. Existing as a
// concrete factory keeps consumers off IConfiguration and mirrors the
// shape of TenantConnectionFactory.
public sealed class MasterConnectionFactory : IMasterConnectionFactory
{
    private readonly string _connectionString;

    public MasterConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "Master connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}
