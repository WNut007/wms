using WMS.Domain.Entities.Outbound;

namespace WMS.DAL.Repositories.Outbound;

// Phase 14D — header + lines + carton aggregate returned by
// GetByIdAsync / GetByNumberAsync. Carton is nullable because pre-
// Submit (Pending) tasks haven't created their carton yet.
public sealed record PackTaskDetail(
    PackTask Header,
    IReadOnlyList<PackTaskLine> Lines,
    Carton? Carton);
