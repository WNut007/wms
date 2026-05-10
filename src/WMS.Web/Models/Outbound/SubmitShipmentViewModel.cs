using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Outbound;

// Phase 14E — model-binding shape for POST /Shipments/Submit/{id}.
// All fields optional — deferred-default carrier pattern (operator
// may not have carrier or tracking number at ship time).
public sealed class SubmitShipmentViewModel
{
    [Required]
    public Guid Id { get; set; }

    [StringLength(50)]
    public string? CarrierName { get; set; }

    [StringLength(100)]
    public string? TrackingNumber { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}
