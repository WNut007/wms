namespace WMS.Web.Models.Inbound;

// Phase 18 — model-binding shape for POST /receive/submit/{poId}.
// Mobile spec drops Phase 1's flat ReceiveFormModel in favor of a
// per-line entry list (one card per PO line in the UI).
//
// All fields nullable — operator may leave a line blank to skip,
// service drops zero-qty rows. Cross-field rules (lot+expiry pair,
// LotAndSerial product blocking) live in the controller.
public sealed class MobileReceiveSubmitViewModel
{
    public string? Notes { get; set; }
    public List<MobileReceiveLineEntry> Lines { get; set; } = new();
}

public sealed class MobileReceiveLineEntry
{
    public Guid PoLineId { get; set; }
    public decimal? ReceivedQuantity { get; set; }
    public string? LocationCode { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? PalletNumber { get; set; }
}
