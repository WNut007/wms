using System.Data;
using Dapper;

namespace WMS.DAL.Common;

// P0 hotfix — Dapper has no built-in mapping for DateOnly / TimeOnly
// (added to .NET in 6.0; Dapper baseline supports DateTime / TimeSpan
// only). Without these handlers, ExecuteAsync throws
// `NotSupportedException: The member X of type System.DateOnly cannot
// be used as a parameter value` on every write path that includes a
// DateOnly column.
//
// Registered ONCE at app startup via Program.cs:
//
//     SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
//     SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
//
// Bug history: silently broken across PO Create (ExpectedDate),
// SO Create (OrderDate / RequestedShipDate), Goods Receipt
// (lot ExpiryDate via ReceiveStockRequest), Mobile Receive
// (lot ExpiryDate), and Lot writes (LotRepository). Tests passed
// throughout because the test suite is Mock-based — the repo's
// ExecuteAsync was never actually called. Surfaced during Block
// 4.5.2 chunk c.1.1 smoke (form submit phase D) when vendor
// dropdown was finally populated and the submit path executed
// end-to-end.
//
// Audit pattern (informal — formalize as a TD): when adding
// DateOnly / TimeOnly to a VM / entity / Request record, verify
// this file's handlers are registered. Same pattern would catch
// a future System.* type Dapper doesn't natively support.

public sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        // SQL Server DATE column maps to DbType.Date. Convert via
        // DateTime at midnight (TimeOnly.MinValue) — the time portion
        // is discarded by SQL Server's DATE storage.
        parameter.DbType = DbType.Date;
        parameter.Value  = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value) =>
        value switch
        {
            DateTime dt   => DateOnly.FromDateTime(dt),
            DateOnly d    => d,
            string s      => DateOnly.Parse(s),
            _ => throw new DataException(
                $"Cannot convert {value?.GetType().FullName ?? "null"} to DateOnly")
        };
}

public sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        // SQL Server TIME column maps to DbType.Time via TimeSpan.
        parameter.DbType = DbType.Time;
        parameter.Value  = value.ToTimeSpan();
    }

    public override TimeOnly Parse(object value) =>
        value switch
        {
            TimeSpan ts => TimeOnly.FromTimeSpan(ts),
            TimeOnly t  => t,
            _ => throw new DataException(
                $"Cannot convert {value?.GetType().FullName ?? "null"} to TimeOnly")
        };
}
