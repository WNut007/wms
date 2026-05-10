using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Inbound;

// Phase 20 — model-binding shape for POST /putaway/submit/{stockId}.
// Operator may override the suggested location by scanning a different
// bin (ToLocationCode); when blank, the controller falls back to the
// suggested-location lookup result. Quantity is operator-confirmed —
// can be the full Stock.OnHand (one-shot move) or a partial split (TD —
// multi-location split is deferred per spec).
public sealed class MobilePutawaySubmitViewModel
{
    // The operator may override the suggested target. Resolved to
    // toLocationId server-side (Locations within the operator's
    // current warehouse, IsActive=1, Status='Active'). When blank,
    // the controller re-runs the suggestion and uses its result —
    // saves a hidden field round-trip.
    public string? ToLocationCode { get; set; }

    [Range(typeof(decimal), "0.0001", "9999999999.9999",
        ErrorMessage = "Quantity must be positive.")]
    public decimal Quantity { get; set; }
}
