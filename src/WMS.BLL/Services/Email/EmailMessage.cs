namespace WMS.BLL.Services.Email;

// Phase 30A — value object for outbound email. To/Cc/Bcc are lists
// so the caller can target multiple recipients without re-sending.
// Subject + HtmlBody + TextBody are all required because:
//  - Subject = mandatory by SMTP spec
//  - HtmlBody = visible content on modern clients
//  - TextBody = fallback for plain-text clients + spam-filter
//                heuristics (multipart/alternative scores better)
public sealed record EmailMessage
{
    public required IReadOnlyList<string> To { get; init; }
    public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Bcc { get; init; } = Array.Empty<string>();
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public required string TextBody { get; init; }

    // For audit / debugging. Logged by EmailService on send; not
    // included in the wire SMTP payload. Lets us trace "which
    // application event generated this email" without grepping
    // subject strings.
    public string? CorrelationId { get; init; }

    // Optional Reply-To. Defaults to FromAddress in config when null.
    public string? ReplyTo { get; init; }
}
