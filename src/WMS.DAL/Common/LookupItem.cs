namespace WMS.DAL.Common;

// Generic 3-field projection for <select> dropdowns on Phase 7 admin
// Create / Edit forms. Same shape regardless of source table — Id is
// the value posted back, Code is the human-readable option text, Name
// is the longer label / tooltip.
//
// Per-table repos return IReadOnlyList<LookupItem> from their
// GetActiveAsync read. Specific tables (Categories with Path,
// UnitsOfMeasure with Type) can ship dedicated projections later if a
// form needs the extra column; LookupItem covers the common case.
public sealed record LookupItem(
    Guid Id,
    string Code,
    string Name);
