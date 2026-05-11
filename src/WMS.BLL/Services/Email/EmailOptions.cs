namespace WMS.BLL.Services.Email;

// Phase 30A — bound from "Email" config section.
//
// Production convention: every secret comes from env var, not
// appsettings:
//   Email__Username  = sender Gmail address
//   Email__Password  = Gmail App Password (16-char, NOT account pw —
//                       needs 2FA enabled + App Password generated at
//                       https://myaccount.google.com/apppasswords )
//   Email__FromAddress = same as Username for Gmail (Gmail enforces)
//
// TestMode=true is the safe default. It logs the email to Serilog
// instead of opening an SMTP connection — never accidentally hits
// real recipients during dev / CI / migration test runs.
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = "Gmail";
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "WMS";

    // TestMode = true → log instead of send. Default true so an
    // unconfigured environment never accidentally sends. Production
    // appsettings.Production.json explicitly sets false.
    public bool TestMode { get; set; } = true;

    // Per-attempt SMTP timeout. Gmail typically responds in <2s; 10s
    // gives headroom without holding a request thread too long.
    public int TimeoutSeconds { get; set; } = 10;
}
