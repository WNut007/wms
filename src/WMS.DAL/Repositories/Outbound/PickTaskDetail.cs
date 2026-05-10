using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14C — header + lines aggregate returned by GetByIdAsync /
// GetByNumberAsync. Same QueryMultiple round-trip pattern as
// SalesOrderDetail / TransferOrderDetail.
public sealed record PickTaskDetail(
    PickTask Header,
    IReadOnlyList<PickTaskLine> Lines);
