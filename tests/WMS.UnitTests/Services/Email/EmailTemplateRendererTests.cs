using WMS.BLL.Services.Email;

namespace WMS.UnitTests.Services.Email;

// Phase 30A — verify {{Placeholder}} substitution + HTML encoding of
// caller-supplied values (defence against operator-supplied strings
// containing markup that would break the HTML template).
public class EmailTemplateRendererTests
{
    private static readonly EmailTemplateRenderer Renderer = new();

    [Fact]
    public void Render_TempPassword_SubstitutesAllKnownPlaceholders()
    {
        var result = Renderer.Render(EmailTemplateType.TempPassword,
            new Dictionary<string, string>
            {
                ["UserName"] = "Jane Operator",
                ["TempPassword"] = "AbCd1234!@#$",
                ["LoginUrl"] = "https://wms.example.com/Auth/Login",
                ["IssuedAtUtc"] = "2026-05-18 09:15:00",
            });

        Assert.Contains("Jane Operator", result.HtmlBody);
        Assert.Contains("AbCd1234!@#$", result.HtmlBody);
        Assert.Contains("https://wms.example.com/Auth/Login", result.HtmlBody);
        Assert.Contains("2026-05-18 09:15:00", result.HtmlBody);
        // Plain-text version too
        Assert.Contains("Jane Operator", result.TextBody);
        Assert.Contains("AbCd1234!@#$", result.TextBody);
    }

    [Fact]
    public void Render_HtmlEncodesValuesInHtmlBody()
    {
        // Operator-supplied name containing markup MUST be encoded to
        // prevent breaking out of the HTML template.
        var result = Renderer.Render(EmailTemplateType.Welcome,
            new Dictionary<string, string>
            {
                ["UserName"] = "<script>alert('xss')</script>",
                ["TenantName"] = "ACME & Co",
                ["UserEmail"] = "jane@acme.com",
                ["LoginUrl"] = "https://wms/Auth",
            });

        Assert.DoesNotContain("<script>", result.HtmlBody);
        Assert.Contains("&lt;script&gt;", result.HtmlBody);
        // & in ACME & Co gets encoded
        Assert.Contains("ACME &amp; Co", result.HtmlBody);
    }

    [Fact]
    public void Render_DoesNotEncodeValuesInTextBody()
    {
        // Plain-text body has nothing to escape — values pass through
        // verbatim.
        var result = Renderer.Render(EmailTemplateType.Welcome,
            new Dictionary<string, string>
            {
                ["UserName"] = "<not-actually-markup>",
                ["TenantName"] = "ACME & Co",
                ["UserEmail"] = "jane@acme.com",
                ["LoginUrl"] = "https://wms/Auth",
            });

        Assert.Contains("<not-actually-markup>", result.TextBody);
        Assert.Contains("ACME & Co", result.TextBody);
    }

    [Fact]
    public void Render_MissingPlaceholder_LeftAsLiteral()
    {
        // If a value isn't provided, leave the {{Name}} marker in place
        // so the issue is visible in the rendered email (vs silently
        // empty). Operator will notice + investigate.
        var result = Renderer.Render(EmailTemplateType.Welcome,
            new Dictionary<string, string>
            {
                ["UserName"] = "Jane",
                // missing: TenantName, UserEmail, LoginUrl
            });

        Assert.Contains("{{TenantName}}", result.HtmlBody);
        Assert.Contains("{{UserEmail}}", result.HtmlBody);
    }

    [Theory]
    [InlineData(EmailTemplateType.Welcome)]
    [InlineData(EmailTemplateType.TempPassword)]
    [InlineData(EmailTemplateType.TenantCreated)]
    [InlineData(EmailTemplateType.PasswordReset)]
    public void Render_AllTemplates_ProduceNonEmptyHtmlAndText(EmailTemplateType type)
    {
        var result = Renderer.Render(type, new Dictionary<string, string>());

        // Both formats present + non-trivial length (catches missing
        // embedded resource).
        Assert.False(string.IsNullOrWhiteSpace(result.HtmlBody));
        Assert.False(string.IsNullOrWhiteSpace(result.TextBody));
        Assert.True(result.HtmlBody.Length > 100);
        Assert.True(result.TextBody.Length > 50);
    }

    [Fact]
    public void Render_MissingTemplate_ThrowsClearError()
    {
        // Cast a non-existent enum value to force the missing-resource
        // path. (Won't happen in production code with a typed enum,
        // but defensive.)
        Assert.Throws<InvalidOperationException>(() =>
            Renderer.Render((EmailTemplateType)999, new Dictionary<string, string>()));
    }

    [Fact]
    public void Render_PasswordReset_DoesNotEmit_RawMustacheSectionTags()
    {
        // P0 #2 regression — PasswordReset.html briefly used
        // {{#ActorName}}...{{/ActorName}} Mustache section syntax. The
        // renderer only handles {{Var}} so the section tags rendered
        // as literal text in the email body. Fix dropped the
        // conditional and always supplies ActorName. This test pins
        // both the template change AND the renderer behaviour.
        var result = Renderer.Render(EmailTemplateType.PasswordReset,
            new Dictionary<string, string>
            {
                ["UserName"] = "Jane",
                ["TempPassword"] = "TempPass123",
                ["LoginUrl"] = "https://wms/Auth",
                ["IssuedAtUtc"] = "2026-05-12 10:00:00",
                ["ActorName"] = "your administrator",
            });

        // Neither the HTML nor text body should contain Mustache
        // section markers in any form. If a future template author
        // reintroduces them, this catches it.
        Assert.DoesNotContain("{{#", result.HtmlBody);
        Assert.DoesNotContain("{{/", result.HtmlBody);
        Assert.DoesNotContain("{{#", result.TextBody);
        Assert.DoesNotContain("{{/", result.TextBody);

        // The fix should still render ActorName cleanly.
        Assert.Contains("by your administrator", result.HtmlBody);
    }
}
