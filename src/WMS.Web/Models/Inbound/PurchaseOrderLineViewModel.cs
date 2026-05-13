using System.ComponentModel.DataAnnotations;

namespace WMS.Web.Models.Inbound;

// Inline-grid row on PO Create + Edit forms. The grid is Alpine-
// driven (x-for over `lines` array, x-model per cell with indexed
// names so ASP.NET Core model binder reconstructs the list).
//
// ReceivedQuantity is read-only on Edit (server-rendered + display-
// only); validators / repo never overwrite it.
public sealed class PurchaseOrderLineViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Line number must be positive.")]
    public int LineNumber { get; set; }

    [Required(ErrorMessage = "Product is required.")]
    public Guid ProductId { get; set; }

    [Required(ErrorMessage = "Unit of measure is required.")]
    public Guid UomId { get; set; }

    [Range(typeof(decimal), "0.0001", "9999999999.9999",
        ErrorMessage = "Expected quantity must be positive.")]
    public decimal ExpectedQuantity { get; set; }

    // Edit-form display only; absent on Create. Set by Edit GET from
    // entity. Used by the lock decision (any line with > 0 → lines
    // section locked) and rendered as read-only column.
    public decimal ReceivedQuantity { get; set; }

    // d.2.3.c — drag-reorder persistence. Hidden input round-trips
    // line.displayOrder from the Alpine state (maintained by
    // _renumberDisplayOrder after every reorder / insert / remove).
    // Controller compares posted vs DB DisplayOrder to classify
    // reorder ops; locked rows go through LineReorders, unlocked
    // through LineUpdates.
    public int DisplayOrder { get; set; }
}
