/*
 * VisuAuth — light/dark theme toggle.
 *
 * Pairs with wwwroot/css/visuauth-brand.css. Two responsibilities:
 *   1. Apply the stored preference to <html data-theme> BEFORE first paint
 *      (the inline bootstrap in the layout <head> does this; this file is the
 *      full version you can also reference standalone).
 *   2. Wire any [data-va-theme-toggle] button to flip + persist the choice.
 *
 * Storage key: "va-theme"  ·  values: "light" | "dark" | (absent = follow OS)
 */
(function () {
    var KEY = "va-theme";
    var root = document.documentElement;

    function stored() {
        try { return localStorage.getItem(KEY); } catch (e) { return null; }
    }

    function systemDark() {
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
    }

    function effective() {
        return stored() || (systemDark() ? "dark" : "light");
    }

    // Apply on load (the layout's inline bootstrap already did this to avoid a
    // flash; running again is harmless and covers htmx full-page swaps).
    function apply(theme) {
        root.setAttribute("data-theme", theme);
    }

    function toggle() {
        var next = effective() === "dark" ? "light" : "dark";
        try { localStorage.setItem(KEY, next); } catch (e) {}
        apply(next);
    }

    // Keep following the OS if the user never made an explicit choice.
    if (window.matchMedia) {
        window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", function () {
            if (!stored()) apply(systemDark() ? "dark" : "light");
        });
    }

    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-va-theme-toggle]");
        if (btn) { e.preventDefault(); toggle(); }
    });

    apply(effective());
})();
