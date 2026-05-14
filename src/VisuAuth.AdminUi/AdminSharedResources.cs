using System.Diagnostics.CodeAnalysis;

namespace VisuAuth.AdminUi;

/// <summary>
/// Marker type bound to the admin UI's JSON translation files at
/// <c>Resources/AdminSharedResources.en.json</c> +
/// <c>Resources/AdminSharedResources.pt-BR.json</c>. Razor views and
/// page models resolve <see cref="Microsoft.Extensions.Localization.IStringLocalizer{AdminSharedResources}"/>
/// to look up translated strings against this assembly.
/// </summary>
/// <remarks>
/// The class is intentionally empty and lives at the assembly's root
/// namespace so the JSON localizer factory resolves the file name to
/// <c>AdminSharedResources.{culture}.json</c> (no nested folders).
/// My.Extensions.Localization.Json requires a culture suffix on every
/// file — there is no implicit neutral fallback — so the default
/// language ships as <c>AdminSharedResources.en.json</c>.
///
/// The <c>Admin</c> prefix is load-bearing: <c>VisuAuth.EndUserUi</c>
/// ships its own <c>EndUserSharedResources</c> marker, and both
/// assemblies copy their JSONs to <c>{consumer-bin}/Resources/</c>.
/// Distinct filenames keep the two from colliding on disk.
///
/// The provider behind <c>IStringLocalizer&lt;T&gt;</c> is
/// <c>My.Extensions.Localization.Json</c> — the UI consumes the
/// standard Microsoft <c>IStringLocalizer</c> contract, so swapping
/// the storage for <c>.resx</c>, a database, or any other backend
/// later would not touch the views.
/// </remarks>
[SuppressMessage(
    "Major Code Smell",
    "S2094:Classes should not be empty",
    Justification = "Marker type required by IStringLocalizer<T> — content is the JSON file at Resources/AdminSharedResources.{culture}.json.")]
public sealed class AdminSharedResources;
