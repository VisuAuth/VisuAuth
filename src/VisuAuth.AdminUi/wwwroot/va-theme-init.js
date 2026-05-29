// VisuAuth — light/dark anti-flash initializer.
//
// Loaded SYNCHRONOUSLY (no defer) from the <head> of both the admin and
// end-user layouts so it runs during head parse, before <body> paints. It
// applies a previously-stored theme choice to <html data-theme> so there's no
// colour flash on navigation. With no stored choice the attribute stays unset
// and the CSS prefers-color-scheme media query governs (follow the OS).
//
// The interactive toggle (button click + persistence + icon swap) lives in
// visuauth.js, which is deferred — fine, because that work happens after paint.
(function () {
    'use strict';
    try {
        const stored = localStorage.getItem('va-theme');
        if (stored === 'dark' || stored === 'light') {
            document.documentElement.dataset.theme = stored;
        }
    } catch {
        // Storage unavailable (private mode / disabled) — fall back to the OS
        // theme via the CSS prefers-color-scheme media query.
    }
})();
