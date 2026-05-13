namespace VisuAuth.Abstractions.Authentication;

/// <summary>
/// Configuration knobs for the end-user pages (register, forgot / reset
/// password, confirm email). Bound from <c>IOptions&lt;EndUserUiOptions&gt;</c>
/// at runtime.
/// </summary>
public sealed class EndUserUiOptions
{
    /// <summary>
    /// When true, password-reset and email-confirmation tokens are surfaced
    /// inline on the matching pages so a developer (or the sample app's
    /// owner) can click through the flow without wiring an email sender.
    /// Defaults to <c>false</c> — production deployments should leave this
    /// off and plug in their own <c>IEmailSender</c>.
    /// </summary>
    public bool DevelopmentMode { get; set; }
}
