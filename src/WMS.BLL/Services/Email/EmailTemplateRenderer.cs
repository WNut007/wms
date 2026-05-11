using System.Reflection;
using System.Text.RegularExpressions;

namespace WMS.BLL.Services.Email;

// Phase 30A — minimal template rendering. The brief asked for Razor
// .cshtml templates; the deliberate deviation is to use plain text /
// HTML files with {{Placeholder}} substitution to avoid pulling in
// RazorLight or wiring up the MVC Razor view engine inside BLL.
//
// Trade-off documented:
//   + No new NuGet dependency
//   + Pure-function rendering — trivially testable
//   + Templates are still standalone files (not C# string literals)
//   + Designers / non-devs can edit them without touching code
//   - No control flow (no @if / @foreach) — for any conditional
//     branch in v3.1+, swap this for a real engine
//
// Convention:
//   Templates live as embedded resources at Services/Email/Templates/
//   *.html and *.txt. Names match the EmailType enum value.
//   Each EmailType has BOTH .html and .txt versions; the renderer
//   loads both so the caller can build a multipart/alternative
//   message in one call.
//
// Placeholders are {{Name}}. HtmlEncode applied automatically to
// every replacement value so caller-supplied strings (operator names,
// emails) can't break out of the HTML template.
public sealed class EmailTemplateRenderer
{
    private static readonly Regex PlaceholderRegex = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);
    private static readonly Assembly TemplateAssembly = typeof(EmailTemplateRenderer).Assembly;
    private const string TemplateNamespace = "WMS.BLL.Services.Email.Templates";

    public RenderedTemplate Render(EmailTemplateType type, IReadOnlyDictionary<string, string> values)
    {
        var html = LoadTemplate($"{type}.html");
        var text = LoadTemplate($"{type}.txt");

        return new RenderedTemplate(
            HtmlBody: Substitute(html, values, htmlEncode: true),
            TextBody: Substitute(text, values, htmlEncode: false));
    }

    private static string LoadTemplate(string fileName)
    {
        var resourceName = $"{TemplateNamespace}.{fileName}";
        using var stream = TemplateAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Email template '{resourceName}' not found as embedded resource. " +
                $"Make sure the .csproj includes <EmbeddedResource Include='Services\\Email\\Templates\\*.html' /> " +
                $"and the file is committed.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Substitute(
        string template,
        IReadOnlyDictionary<string, string> values,
        bool htmlEncode) =>
        PlaceholderRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (!values.TryGetValue(key, out var value)) return match.Value;
            return htmlEncode ? System.Net.WebUtility.HtmlEncode(value) : value;
        });
}

public sealed record RenderedTemplate(string HtmlBody, string TextBody);

public enum EmailTemplateType
{
    Welcome,
    TempPassword,
    TenantCreated,
    PasswordReset,
}
