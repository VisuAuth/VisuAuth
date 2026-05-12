using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Common;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.AdminUi.Pages.Admin.Users;

/// <summary>
/// User detail page. Read-only sections render via <c>_DetailContent</c>;
/// mutations (profile edit, lockout, password reset, 2FA reset, session
/// revocation) post to dedicated handlers and refresh the detail content
/// via htmx.
/// </summary>
public sealed class DetailModel(IUserStore userStore) : PageModel
{
    private readonly IUserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));

    [BindProperty(SupportsGet = true, Name = "id")]
    public string Id { get; set; } = string.Empty;

    [BindProperty]
    public ProfileForm Profile { get; set; } = new();

    public UserDetail Detail { get; private set; } = default!;

    public UserBackendCapabilities Capabilities => _userStore.Capabilities;

    /// <summary>When true, the profile section renders in edit mode.</summary>
    public bool ProfileEditMode { get; private set; }

    /// <summary>Shown once at the top of the page after a successful mutation.</summary>
    public string? ActionMessage { get; private set; }

    /// <summary>One-time temporary password surfaced by <c>ResetPasswordAsync</c>.</summary>
    public string? LastTemporaryPassword { get; private set; }

    /// <summary>Validation / Identity errors from the last mutation attempt.</summary>
    public IReadOnlyList<string> ActionErrors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        => await LoadAndRenderAsync(partialOnHtmx: "_DetailContent", cancellationToken);

    public async Task<IActionResult> OnGetEditProfileAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        if (loaded is null)
        {
            return NotFound();
        }

        ProfileEditMode = true;
        Profile = ProfileForm.From(Detail);
        return Partial("_ProfileSection", this);
    }

    public async Task<IActionResult> OnGetViewProfileAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        return loaded is null ? NotFound() : Partial("_ProfileSection", this);
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync(CancellationToken cancellationToken)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        if (loaded is null)
        {
            return NotFound();
        }

        var command = new UpdateUserCommand
        {
            Email = NormalizeOrNull(Profile.Email),
            UserName = NormalizeOrNull(Profile.UserName),
            PhoneNumber = Profile.PhoneNumber ?? string.Empty,
        };

        var result = await _userStore.UpdateAsync(Id, command, cancellationToken);

        if (!result.IsSuccess)
        {
            ProfileEditMode = true;
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? "Update failed."];
            // Reload to make sure the form repopulates with the canonical state.
            await LoadDetailAsync(cancellationToken);
            return Partial("_DetailContent", this);
        }

        await LoadDetailAsync(cancellationToken);
        ActionMessage = "Profile updated.";
        return Partial("_DetailContent", this);
    }

    public Task<IActionResult> OnPostLockAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.SetEnabledAsync(Id, enabled: false, cancellationToken),
            success: "Account locked.",
            cancellationToken);

    public Task<IActionResult> OnPostUnlockAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.SetEnabledAsync(Id, enabled: true, cancellationToken),
            success: "Account unlocked.",
            cancellationToken);

    public Task<IActionResult> OnPostResetPasswordAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.ResetPasswordAsync(Id, cancellationToken),
            success: "Temporary password generated. Hand it to the user — it will not be shown again.",
            cancellationToken,
            onSuccess: r =>
            {
                if (r.Metadata.TryGetValue("temporaryPassword", out var pwd))
                {
                    LastTemporaryPassword = pwd;
                }
            });

    public Task<IActionResult> OnPostResetTwoFactorAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.ResetTwoFactorAsync(Id, cancellationToken),
            success: "Two-factor disabled and authenticator key reset.",
            cancellationToken);

    public Task<IActionResult> OnPostRevokeSessionsAsync(CancellationToken cancellationToken)
        => ExecuteAsync(
            () => _userStore.RevokeSessionsAsync(Id, cancellationToken),
            success: "All sessions revoked.",
            cancellationToken);

    private async Task<IActionResult> ExecuteAsync(
        Func<Task<UserResult>> action,
        string success,
        CancellationToken cancellationToken,
        Action<UserResult>? onSuccess = null)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        if (loaded is null)
        {
            return NotFound();
        }

        var result = await action();

        if (!result.IsSuccess)
        {
            ActionErrors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? "Action failed."];
        }
        else
        {
            ActionMessage = success;
            onSuccess?.Invoke(result);
        }

        // Always reload — security stamp, lockout end, 2FA flag, etc. may have moved.
        await LoadDetailAsync(cancellationToken);
        return Partial("_DetailContent", this);
    }

    private async Task<IActionResult> LoadAndRenderAsync(string partialOnHtmx, CancellationToken cancellationToken)
    {
        var loaded = await LoadDetailAsync(cancellationToken);
        if (loaded is null)
        {
            return NotFound();
        }

        Profile = ProfileForm.From(Detail);

        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return Partial(partialOnHtmx, this);
        }

        return Page();
    }

    private async Task<UserDetail?> LoadDetailAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            return null;
        }

        var detail = await _userStore.GetDetailAsync(Id, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        Detail = detail;
        return detail;
    }

    private static string? NormalizeOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Form posted by the profile edit panel.</summary>
    public sealed class ProfileForm
    {
        public string? Email { get; set; }
        public string? UserName { get; set; }
        public string? PhoneNumber { get; set; }

        public static ProfileForm From(UserDetail detail) => new()
        {
            Email = detail.Email,
            UserName = detail.UserName,
            PhoneNumber = detail.PhoneNumber,
        };
    }
}
