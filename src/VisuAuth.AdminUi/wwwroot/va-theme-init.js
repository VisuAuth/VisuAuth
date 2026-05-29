// VisuAuth — light/dark theme initializer + toggle.
//
// Loaded SYNCHRONOUSLY (no defer) from the <head> of both layouts so a stored
// choice is applied to <html data-theme> before first paint — no colour flash.
// It also wires any [data-va-theme-toggle] button: a click flips and persists
// the choice. With no stored choice the attribute stays unset and the CSS
// prefers-color-scheme media query governs (and follows OS changes live, with
// no JS). The sun/moon icon swap is pure CSS (see visuauth.css), so this never
// touches the icons.
//
// Storage key: "va-theme"  ·  values: "light" | "dark" | (absent = follow OS)
(function () {
    'use strict';

    const KEY = 'va-theme';

    function stored() {
        try {
            return localStorage.getItem(KEY);
        } catch {
            // Storage unavailable (private mode / disabled) — follow the OS.
            return null;
        }
    }

    // Anti-flash: apply the persisted choice immediately. Leaving the attribute
    // unset when there's no choice lets the CSS media query pick the OS theme.
    const initial = stored();
    if (initial === 'dark' || initial === 'light') {
        document.documentElement.dataset.theme = initial;
    }

    document.addEventListener('click', function (event) {
        const button = event.target?.closest?.('[data-va-theme-toggle]');
        if (!button) {
            return;
        }
        event.preventDefault();

        const root = document.documentElement;
        const isDark = root.dataset.theme
            ? root.dataset.theme === 'dark'
            : (globalThis.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false);
        const next = isDark ? 'light' : 'dark';

        root.dataset.theme = next;
        try {
            localStorage.setItem(KEY, next);
        } catch {
            // Storage disabled — the toggle still works for this page; the
            // choice just won't persist across navigations.
        }
    });
})();
