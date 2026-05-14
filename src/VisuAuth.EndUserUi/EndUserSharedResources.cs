using System.Diagnostics.CodeAnalysis;

namespace VisuAuth.EndUserUi;

/// <summary>
/// Marker type bound to the end-user UI's JSON translation files at
/// <c>Resources/EndUserSharedResources.en.json</c> +
/// <c>Resources/EndUserSharedResources.pt-BR.json</c>. Razor views and page
/// models resolve <see cref="Microsoft.Extensions.Localization.IStringLocalizer{EndUserSharedResources}"/>
/// to look up translated strings against this assembly.
/// </summary>
/// <remarks>
/// The <c>EndUser</c> prefix is load-bearing: <c>VisuAuth.AdminUi</c>
/// ships its own <c>AdminSharedResources</c> marker, and both
/// assemblies copy their JSONs to <c>{consumer-bin}/Resources/</c>.
/// Distinct filenames keep the two from colliding on disk.
/// </remarks>
[SuppressMessage(
    "Major Code Smell",
    "S2094:Classes should not be empty",
    Justification = "Marker type required by IStringLocalizer<T> — content is the JSON file at Resources/EndUserSharedResources.{culture}.json.")]
public sealed class EndUserSharedResources;
