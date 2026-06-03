# Theming

VisuAuth ships four layers of customization, ranked from simplest to most
powerful. Each builds on the one before — pick the lowest layer that meets your
need.

> **This page is being expanded for the v1.0 documentation site, with
> before/after screenshots of each layer.**

## The four layers

1. **CSS custom properties.** Override `--visuauth-primary`, `--visuauth-bg`,
   and friends in your own stylesheet, loaded after ours. The default theme
   lives in `wwwroot/visuauth.css`. A built-in light/dark theme ships out of
   the box.
2. **Programmatic config.** `services.AddVisuAuth().Configure<VisuAuthTheme>(…)`
   generates the CSS variables at runtime — handy when the brand comes from
   configuration.
3. **Razor view override.** Drop your own `.cshtml` into `/Views/VisuAuth/`
   (or a configured root) and VisuAuth falls back to its own when yours is
   absent.
4. **Per-tenant overrides.** Implement `ITenantThemeResolver` and
   `ITenantViewOverrideResolver` to vary theme and templates per tenant at
   runtime. Default no-op resolvers mean apps that never opt in keep the
   fast path.

## Planned outline

- Full token reference for layer 1.
- `VisuAuthTheme` properties for layer 2.
- The view-location expander and route-demotion convention behind layer 3.
- `VisuAuthThemeMerger` precedence rules for layer 4 (tenant wins per
  property, global fills the rest, CSS defaults fill what is still null).
