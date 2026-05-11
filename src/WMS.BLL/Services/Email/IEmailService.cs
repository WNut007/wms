namespace WMS.BLL.Services.Email;

// Phase 30A — outbound email surface. SMTP-backed. Three audience
// patterns matter:
//   - SuperAdmin → tenant admin (welcome, temp password, tenant
//     created)
//   - Tenant admin → tenant user (welcome, password reset)
//   - System → SuperAdmin (alerts, lockout notifications) — future
//
// Implementation lives in GmailSmtpEmailService for v3.0.0; SendGrid
// drop-in is TD-110. TestMode=true logs the email instead of sending
// — safe for CI + local dev without burning Gmail quota or leaking
// test emails.
public interface IEmailService
{
    // Returns true on success (sent or logged in TestMode). Throws on
    // hard SMTP failure (network, auth, malformed). Caller decides
    // whether to retry — Phase 30A doesn't auto-retry (TD-111 covers
    // Hangfire queue + retry).
    Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default);
}
