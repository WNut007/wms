using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WMS.BLL.Services.Email;

namespace WMS.UnitTests.Services.Email;

// Phase 30A — TestMode behaviour. Real SMTP send isn't tested here
// (no Gmail credentials in CI, no outbound SMTP from CI runners);
// that's exercised manually via M3 smoke checklist.
public class GmailSmtpEmailServiceTests
{
    private static GmailSmtpEmailService BuildService(EmailOptions opts) =>
        new(Options.Create(opts), NullLogger<GmailSmtpEmailService>.Instance);

    private static EmailMessage BuildMessage(params string[] to) => new()
    {
        To = to,
        Subject = "Test",
        HtmlBody = "<p>Hello</p>",
        TextBody = "Hello",
    };

    [Fact]
    public async Task SendAsync_TestModeTrue_ReturnsTrue_WithoutSmtp()
    {
        var svc = BuildService(new EmailOptions { TestMode = true });
        var sent = await svc.SendAsync(BuildMessage("recipient@example.com"));

        // No exception thrown despite Username/Password being unset —
        // TestMode short-circuits before the SMTP path.
        Assert.True(sent);
    }

    [Fact]
    public async Task SendAsync_TestModeFalse_MissingCredentials_Throws()
    {
        var svc = BuildService(new EmailOptions
        {
            TestMode = false,
            // Username / Password / FromAddress all empty
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SendAsync(BuildMessage("recipient@example.com")));
        Assert.Contains("Email options incomplete", ex.Message);
        Assert.Contains("Email__Username", ex.Message);
    }

    [Fact]
    public async Task SendAsync_EmptyRecipients_Throws()
    {
        var svc = BuildService(new EmailOptions { TestMode = true });
        var msg = new EmailMessage
        {
            To = Array.Empty<string>(),
            Subject = "Test",
            HtmlBody = "<p>Hello</p>",
            TextBody = "Hello",
        };

        await Assert.ThrowsAsync<ArgumentException>(() => svc.SendAsync(msg));
    }

    [Fact]
    public async Task SendAsync_TestModeFalse_PartialCredentials_Throws()
    {
        // Username set but Password missing.
        var svc = BuildService(new EmailOptions
        {
            TestMode = false,
            Username = "test@gmail.com",
            // Password missing
            FromAddress = "test@gmail.com",
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SendAsync(BuildMessage("recipient@example.com")));
    }
}
