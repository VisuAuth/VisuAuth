using Microsoft.AspNetCore.Identity;

namespace Sample.WebApp.Data;

/// <summary>
/// Sample application's user entity. Extends <see cref="IdentityUser"/> with a
/// creation timestamp so the admin UI has something interesting to show.
/// </summary>
public sealed class ApplicationUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
