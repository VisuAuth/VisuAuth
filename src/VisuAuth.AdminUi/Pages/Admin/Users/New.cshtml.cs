using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using VisuAuth.Abstractions.Capabilities;
using VisuAuth.Abstractions.Users;

namespace VisuAuth.AdminUi.Pages.Admin.Users;

/// <summary>
/// Create-user form. On success the admin lands on the new user's detail page
/// so they can immediately follow up with role assignment, lockout, etc.
/// </summary>
public sealed class NewModel(IUserStore userStore) : PageModel
{
    private readonly IUserStore _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));

    [BindProperty]
    public CreateUserForm Form { get; set; } = new();

    public UserBackendCapabilities Capabilities => _userStore.Capabilities;

    /// <summary>Validation / business errors from the most recent submission.</summary>
    public IReadOnlyList<string> Errors { get; private set; } = [];

    /// <summary>Temporary password surfaced when the admin leaves the password field blank.</summary>
    public string? GeneratedPassword { get; private set; }

    /// <summary>Newly created user id. Drives the post-create banner with the link to detail.</summary>
    public string? CreatedUserId { get; private set; }

    public IActionResult OnGet()
    {
        // Surfacing "this backend does not support registration" early lets the
        // admin see why the form is unavailable without first attempting a POST.
        if (!Capabilities.SupportsRegistration)
        {
            Errors = ["This backend does not support user creation."];
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Capabilities.SupportsRegistration)
        {
            Errors = ["This backend does not support user creation."];
            return Page();
        }

        if (!ModelState.IsValid)
        {
            Errors = ModelState
                .Where(kvp => kvp.Value?.Errors.Count > 0)
                .SelectMany(kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage))
                .ToList();
            return Page();
        }

        var command = new CreateUserCommand
        {
            Email = Form.Email?.Trim() ?? string.Empty,
            UserName = string.IsNullOrWhiteSpace(Form.UserName) ? null : Form.UserName.Trim(),
            Password = string.IsNullOrEmpty(Form.Password) ? null : Form.Password,
            PhoneNumber = string.IsNullOrWhiteSpace(Form.PhoneNumber) ? null : Form.PhoneNumber.Trim(),
            EmailConfirmed = Form.EmailConfirmed,
        };

        var result = await _userStore.CreateAsync(command, cancellationToken);

        if (!result.IsSuccess)
        {
            Errors = result.ValidationErrors.Count > 0
                ? result.ValidationErrors
                : [result.Error ?? "Failed to create user."];
            return Page();
        }

        CreatedUserId = result.UserId;
        if (result.Metadata.TryGetValue("temporaryPassword", out var temp))
        {
            GeneratedPassword = temp;
        }

        // When a temporary password was generated, keep the admin on this page
        // so they can copy the password before navigating away. The page renders
        // the temp password panel plus a link to the detail. When the admin
        // supplied a password, no secrets to surface — redirect straight to detail.
        if (GeneratedPassword is null && CreatedUserId is not null)
        {
            return Redirect($"/visuauth/admin/users/{CreatedUserId}");
        }

        // Clear the form so the success view does not re-populate it.
        Form = new CreateUserForm();
        return Page();
    }

    public sealed class CreateUserForm
    {
        public string? Email { get; set; }

        public string? UserName { get; set; }

        public string? Password { get; set; }

        public string? PhoneNumber { get; set; }

        public bool EmailConfirmed { get; set; } = true;
    }
}
