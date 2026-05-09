using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Master;

// View-model for /Customers/Create. 4-step Alpine stepper per Phase 7
// brief: Identity → Contact → Commercial → Status. The view-model is
// flat (no per-step nesting) — the stepper is a UI affordance, not a
// data shape. CustomerCreateValidator enforces the B2B/B2C
// cross-field rule.
public sealed class CustomerCreateViewModel
{
    // CHECK CK_Customers_Status — must mirror migration 018.
    public static readonly IReadOnlyList<string> AllStatuses =
        new[] { "Active", "Inactive", "Suspended", "Draft" };

    // CHECK CK_Customers_Type — must mirror migration 018.
    public static readonly IReadOnlyList<string> AllCustomerTypes =
        new[] { "B2B", "B2C" };

    // CHECK CK_Customers_Tier — must mirror migration 018.
    // Empty value treated as NULL.
    public static readonly IReadOnlyList<string> AllTiers =
        new[] { "Tier1", "Tier2", "Tier3", "Tier4" };

    // --- Step 1 Identity ----------------------------------------------

    [Required(ErrorMessage = "Code is required.")]
    [StringLength(20, ErrorMessage = "Code must be at most 20 characters.")]
    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200, ErrorMessage = "Name must be at most 200 characters.")]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Customer type is required.")]
    [Display(Name = "Customer type")]
    public string CustomerType { get; set; } = "B2C";

    // --- Step 2 Contact -----------------------------------------------

    [StringLength(200)]
    [Display(Name = "Company name")]
    public string? CompanyName { get; set; }

    [StringLength(20)]
    [Display(Name = "Tax ID")]
    public string? TaxId { get; set; }

    [EmailAddress(ErrorMessage = "Email format is invalid.")]
    [StringLength(100)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [StringLength(20)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [StringLength(50)]
    [Display(Name = "Country")]
    public string? Country { get; set; }

    // --- Step 3 Commercial --------------------------------------------

    [Display(Name = "Tier")]
    public string? CustomerTier { get; set; }

    [Display(Name = "Annual revenue")]
    public decimal? AnnualRevenue { get; set; }

    [Display(Name = "Orders per month")]
    public int? OrdersPerMonth { get; set; }

    [Display(Name = "Average order value")]
    public decimal? AvgOrderValue { get; set; }

    [Display(Name = "Key account")]
    public bool IsKeyAccount { get; set; }

    [Display(Name = "Strategic")]
    public bool IsStrategic { get; set; }

    [Display(Name = "Allocation priority")]
    public int? AllocationPriority { get; set; }

    [Display(Name = "Safety stock (days)")]
    public int? SafetyStockDays { get; set; }

    [Range(0, 100, ErrorMessage = "Promised fill rate must be between 0 and 100.")]
    [Display(Name = "Promised fill rate (%)")]
    public decimal? PromisedFillRate { get; set; }

    [Display(Name = "Preferred carrier")]
    public Guid? PreferredCarrierId { get; set; }

    [StringLength(50)]
    [Display(Name = "Default payment terms")]
    public string? DefaultPaymentTerms { get; set; }

    // --- Step 4 Status ------------------------------------------------

    [Required(ErrorMessage = "Status is required.")]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Active";

    // Lookup data populated by controller for the carrier <select>.
    public IReadOnlyList<LookupItem> Carriers { get; set; } = Array.Empty<LookupItem>();
}
