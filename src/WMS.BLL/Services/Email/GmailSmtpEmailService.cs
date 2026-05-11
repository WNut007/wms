using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WMS.BLL.Services.Email;

// Phase 30A — System.Net.Mail-based SMTP sender.
//
// SmtpClient is marked obsolete in .NET 8 (SYSLIB0014) — Microsoft
// recommends MailKit for new code. We accept the obsolete warning
// because:
//   - Phase 30A is local + Gmail test; full-scale email is TD-110 (SendGrid)
//   - One less NuGet dep to vet at v3.0.0 launch
//   - The Send path is small (~30 LOC); MailKit migration is mechanical
//
// TestMode behaviour:
//   - TestMode=true: log to Information level + return true. NEVER opens
//     a network connection. Safe default for dev / CI / fresh installs.
//   - TestMode=false: real send via SmtpClient. Throws on SMTP failure;
//     caller chooses whether to retry (TD-111 = Hangfire queue + retry).
#pragma warning disable SYSLIB0014   // System.Net.Mail.SmtpClient obsolete
public sealed class GmailSmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<GmailSmtpEmailService> _logger;

    public GmailSmtpEmailService(
        IOptions<EmailOptions> options,
        ILogger<GmailSmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (message.To.Count == 0)
            throw new ArgumentException("EmailMessage.To must contain at least one recipient.", nameof(message));

        if (_options.TestMode)
        {
            // Structured log so operators can grep for emails-that-would-have-sent.
            _logger.LogInformation(
                "[EMAIL TestMode=true — NOT SENT] Subject={Subject} To={To} CorrelationId={CorrelationId}",
                message.Subject,
                string.Join(", ", message.To),
                message.CorrelationId ?? "(none)");
            return true;
        }

        if (string.IsNullOrWhiteSpace(_options.Username) ||
            string.IsNullOrWhiteSpace(_options.Password) ||
            string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new InvalidOperationException(
                "Email options incomplete. Set Email:Username, Email:Password, and Email:FromAddress " +
                "(via env vars Email__Username / Email__Password / Email__FromAddress).");
        }

        using var smtp = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.UseStartTls,
            Credentials = new NetworkCredential(_options.Username, _options.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = _options.TimeoutSeconds * 1000,
        };

        using var mail = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = message.Subject,
            Body = message.HtmlBody,
            IsBodyHtml = true,
            BodyEncoding = System.Text.Encoding.UTF8,
            SubjectEncoding = System.Text.Encoding.UTF8,
        };

        foreach (var to in message.To)     mail.To.Add(to);
        foreach (var cc in message.Cc)     mail.CC.Add(cc);
        foreach (var bcc in message.Bcc)   mail.Bcc.Add(bcc);

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            mail.ReplyToList.Add(new MailAddress(message.ReplyTo));

        // Attach the text body as the multipart/alternative fallback so
        // plain-text clients render correctly + spam-filter heuristics
        // score the message better.
        var textView = AlternateView.CreateAlternateViewFromString(
            message.TextBody,
            System.Text.Encoding.UTF8,
            "text/plain");
        mail.AlternateViews.Add(textView);

        var htmlView = AlternateView.CreateAlternateViewFromString(
            message.HtmlBody,
            System.Text.Encoding.UTF8,
            "text/html");
        mail.AlternateViews.Add(htmlView);

        try
        {
            await smtp.SendMailAsync(mail, ct);
            _logger.LogInformation(
                "Email sent. Subject={Subject} To={To} CorrelationId={CorrelationId}",
                message.Subject,
                string.Join(", ", message.To),
                message.CorrelationId ?? "(none)");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Email send FAILED. Subject={Subject} To={To} CorrelationId={CorrelationId} Provider={Provider}",
                message.Subject,
                string.Join(", ", message.To),
                message.CorrelationId ?? "(none)",
                _options.Provider);
            throw;
        }
    }
}
#pragma warning restore SYSLIB0014
