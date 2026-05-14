using System.Buffers;
using System.Text;

namespace VisuAuth.AdminUi.Theming;

/// <summary>
/// Pure conversion from a <see cref="VisuAuthTheme"/> bag to the inline
/// <c>:root { --visuauth-…: …; }</c> CSS block that ships in the page
/// layout. Null / blank properties are skipped — those keep the default
/// declared in <c>visuauth.css</c>.
/// </summary>
public static class VisuAuthThemeCssRenderer
{
    /// <summary>
    /// CSS-value characters that could break out of the <c>:root</c> rule
    /// or the surrounding <c>&lt;style&gt;</c> element. Server-only config,
    /// but we throw on any of these so a misconfigured value fails loud
    /// instead of corrupting the rendered HTML.
    ///
    /// <list type="bullet">
    ///   <item><c>&lt;</c> / <c>&gt;</c> — could start <c>&lt;/style&gt;</c>.</item>
    ///   <item><c>{</c> / <c>}</c> — could close the <c>:root</c> block.</item>
    ///   <item><c>;</c> — ends the declaration and lets a second one sneak in.</item>
    ///   <item><c>\</c> — CSS escape sequences.</item>
    /// </list>
    /// </summary>
    private const string ForbiddenChars = "<>{};\\";

    // Cached SearchValues for IndexOfAny — same characters as
    // ForbiddenChars, which stays the source of truth and the
    // human-readable form quoted in error messages.
    private static readonly SearchValues<char> ForbiddenSearch =
        SearchValues.Create(ForbiddenChars);

    // Property → CSS variable. Listed in the same order as the :root block
    // in visuauth.css so the rendered overrides are easy to diff against
    // the defaults.
    private static readonly (string PropertyName, string CssVariable, Func<VisuAuthTheme, string?> Read)[] Map =
    [
        (nameof(VisuAuthTheme.Primary),   "--visuauth-primary",     t => t.Primary),
        (nameof(VisuAuthTheme.PrimaryFg), "--visuauth-primary-fg",  t => t.PrimaryFg),
        (nameof(VisuAuthTheme.Bg),        "--visuauth-bg",          t => t.Bg),
        (nameof(VisuAuthTheme.Fg),        "--visuauth-fg",          t => t.Fg),
        (nameof(VisuAuthTheme.Muted),     "--visuauth-muted",       t => t.Muted),
        (nameof(VisuAuthTheme.Border),    "--visuauth-border",      t => t.Border),
        (nameof(VisuAuthTheme.Surface),   "--visuauth-surface",     t => t.Surface),
        (nameof(VisuAuthTheme.Danger),    "--visuauth-danger",      t => t.Danger),
        (nameof(VisuAuthTheme.Success),   "--visuauth-success",     t => t.Success),
        (nameof(VisuAuthTheme.Radius),    "--visuauth-radius",      t => t.Radius),
        (nameof(VisuAuthTheme.Font),      "--visuauth-font",        t => t.Font),
    ];

    /// <summary>
    /// Returns the <c>:root { … }</c> block for the populated properties,
    /// or an empty string when nothing was configured. Suitable for
    /// emitting raw inside a <c>&lt;style&gt;</c> element.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A property value contains a character from <see cref="ForbiddenChars"/>.
    /// </exception>
    public static string Render(VisuAuthTheme? theme)
    {
        if (theme is null)
        {
            return string.Empty;
        }

        StringBuilder? sb = null;
        foreach (var (propertyName, cssVariable, read) in Map)
        {
            var raw = read(theme);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var value = raw.Trim();
            EnsureSafe(propertyName, value);

            sb ??= new StringBuilder(":root {");
            sb.Append(' ').Append(cssVariable).Append(": ").Append(value).Append(';');
        }

        if (sb is null)
        {
            return string.Empty;
        }

        sb.Append(" }");
        return sb.ToString();
    }

    private static void EnsureSafe(string propertyName, string value)
    {
        var hit = value.AsSpan().IndexOfAny(ForbiddenSearch);
        if (hit >= 0)
        {
            throw new InvalidOperationException(
                $"VisuAuthTheme.{propertyName} contains the forbidden character '{value[hit]}'. "
                + $"Values used as CSS overrides cannot contain any of: {ForbiddenChars}");
        }
    }
}
