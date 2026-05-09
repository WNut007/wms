using System.ComponentModel.DataAnnotations;
using WMS.DAL.Common;

namespace WMS.Web.Models.Master;

// Edit view-model. Code + CustomerType read-only (rendered as readonly
// inputs / hidden fields and never sent to UpdateAsync per the brief —
// CustomerType flip would orphan the B2B-only fields).
public sealed class CustomerEditViewModel
{
    public static readonly IReadOnlyList<string> AllStatuses =
        CustomerCreateViewModel.AllStatuses;
    public static readonly IReadOnlyList<string> AllCustomerTypes =
        CustomerCreateViewModel.AllCustomerTypes;
    public static readonly IReadOnlyList<string> AllTiers =
        CustomerCreateViewModel.AllTiers;

    public Guid Id { get; set; }

    [Display(Name = "Code")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(200)]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [Display(Name = "Customer type")]
    public string CustomerType { get; set; } = "B2C";

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

    [Required(ErrorMessage = "Status is required.")]
    [Display(Name = "Status")]
    public string Status { get; set; } = "Active";

    public IReadOnlyList<LookupItem> Carriers { get; set; } = Array.Empty<LookupItem>();
}
