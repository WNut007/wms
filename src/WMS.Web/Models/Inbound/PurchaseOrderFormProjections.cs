namespace WMS.Web.Models.Inbound;

// Block 4.5.2 chunk d.1 — domain VM → shared form VM projections.
// Create + Edit views call these on Model to feed the _PoForm partial.
// Extension methods keep mapping out of the controllers' action bodies
// and let the partial speak a single contract.
//
// d.1 wires Create only; ToFormViewModel(EditViewModel) is pre-staged
// for d.2 (Edit.cshtml refactor) but no view consumes it yet.
public static class PurchaseOrderFormProjections
{
    public static PurchaseOrderFormViewModel ToFormViewModel(
        this PurchaseOrderCreateViewModel src) =>
        new()
        {
            Mode         = "create",
            PoNumber     = src.PoNumber,
            OwnerId      = src.OwnerId,
            WarehouseId  = src.WarehouseId,
            ExpectedDate = src.ExpectedDate,
            Notes        = src.Notes,
            Lines        = src.Lines,
            Vendors      = src.Vendors,
            Warehouses   = src.Warehouses,
            Products     = src.Products,
            Uoms         = src.Uoms,
            // All editable on Create — defaults already match, but
            // spell it out for readability.
            Locks = new PurchaseOrderFormLocks
            {
                PoNumber        = false,
                Vendor          = false,
                Warehouse       = false,
                ExpectedDate    = false,
                Notes           = false,
                LinesLocked     = false,
                CanAddLines     = true,
                CanRemoveLines  = true,
                CanReorderLines = true,
            },
            Audit = null,
        };

    public static PurchaseOrderFormViewModel ToFormViewModel(
        this PurchaseOrderEditViewModel src) =>
        new()
        {
            Mode          = "edit",
            Id            = src.Id,
            Status        = src.Status,
            PoNumber      = src.PoNumber,
            // OwnerId / WarehouseId not on Edit VM (permanently locked
            // post-create per existing convention). The display
            // strings carry through to the partial's locked rendering.
            VendorCode    = src.VendorCode,
            VendorName    = src.VendorName,
            WarehouseCode = src.WarehouseCode,
            ExpectedDate  = src.ExpectedDate,
            Notes         = src.Notes,
            Lines         = src.Lines,
            Products      = src.Products,
            Uoms          = src.Uoms,
            // Locks today: PoNumber/Vendor/Warehouse ALWAYS locked
            // post-create; ExpectedDate/Notes editable on any non-
            // terminal status; LinesLocked driven by receipt presence.
            // Closed/Cancelled never reach this projection — the Edit
            // controller will redirect them to /Details/{id} in d.2.
            Locks = new PurchaseOrderFormLocks
            {
                PoNumber        = true,
                Vendor          = true,
                Warehouse       = true,
                ExpectedDate    = false,
                Notes           = false,
                LinesLocked     = src.LinesLocked,
                CanAddLines     = !src.LinesLocked,
                CanRemoveLines  = !src.LinesLocked,
                CanReorderLines = !src.LinesLocked,
            },
            // d.5 fills this from entity CreatedAt/By + UpdatedAt/By
            // plus name resolution. d.1 + d.2 leave null.
            Audit = null,
        };
}
