using Microsoft.Extensions.DependencyInjection;

namespace VisuAuth;

/// <summary>
/// Fluent composition root for VisuAuth. Returned by
/// <see cref="VisuAuthBuilderExtensions.AddVisuAuth"/> and consumed by the
/// chain extensions (<c>UseAspNetIdentity</c>, <c>EnableMultiTenant</c>,
/// <c>AddAdminUi</c>, <c>AddEndUserUi</c>). The underlying service collection
/// is exposed so adapter authors can plug additional registrations into the
/// same chain without leaving the fluent surface.
/// </summary>
public interface IVisuAuthBuilder
{
    /// <summary>
    /// The service collection that backs this builder. All chain methods
    /// register against it.
    /// </summary>
    IServiceCollection Services { get; }
}
