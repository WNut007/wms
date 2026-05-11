using System.ComponentModel.DataAnnotations;

namespace WMS.Web.ViewModels.SuperAdmin;

public sealed class TenantListRow
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int UserCount { get; set; }   // best-effort, may be 0 if tenant DB unreachable
}

public sealed class TenantListViewModel
{
    public IReadOnlyList<TenantListRow> Rows { get; set; } = Array.Empty<TenantListRow>();
    public int CountActive { get; set; }
    public int CountSuspended { get; set; }
    public int CountInactive { get; set; }
    public string? StatusFilter { get; set; }
}

public sealed class TenantCreateViewModel
{
    [Required, StringLength(20, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Code must be alphanumeric only (2-20 chars).")]
    public string Code { get; set; } = "";

    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    [Required, EmailAddress, StringLength(100)]
    [Display(Name = "Admin email")]
    public string AdminEmail { get; set; } = "";

    [StringLength(200)]
    [Display(Name = "Admin full name (optional)")]
    public string? AdminFullName { get; set; }
}

public sealed class TenantCreateSuccessViewModel
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string AdminEmail { get; set; } = "";
    public string AdminTempPassword { get; set; } = "";
}

public sealed class TenantDetailViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int UserCount { get; set; }
    public string? AdminEmail { get; set; }
}

public sealed class SuspendTenantViewModel
{
    [Required]
    public Guid TenantId { get; set; }

    [Required, StringLength(500, MinimumLength = 3)]
    public string Reason { get; set; } = "";
}
