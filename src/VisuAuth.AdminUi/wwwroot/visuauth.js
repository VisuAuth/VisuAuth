// VisuAuth admin UI — vanilla JS helpers.
//
// Kept dependency-free on purpose. Anything that needs a real interaction
// model is handled by htmx in the markup; this file is for client-only
// micro-interactions (clipboard, focus management, etc.) that do not need
// a roundtrip.
//
// Uses event delegation on `document` so handlers automatically pick up
// nodes that htmx swapped in after page load.

(function () {
    'use strict';

    var COPIED_CLASS = 'va-copied';
    var COPIED_DURATION_MS = 1500;

    // Show / hide an icon via the `hidden` content attribute. We can't use
    // `el.hidden = x` because that IDL property lives on HTMLElement only —
    // it's a no-op on <svg> (SVGElement), which would leave both icons of a
    // swap visible. Toggling the attribute works for HTML and SVG alike; the
    // `svg[hidden] { display: none }` CSS rule makes it bite on SVG too.
    function setHidden(el, hide) {
        if (!el) { return; }
        if (hide) { el.setAttribute('hidden', ''); }
        else { el.removeAttribute('hidden'); }
    }

    document.addEventListener('click', function (event) {
        var button = event.target && event.target.closest && event.target.closest('[data-va-copy]');
        if (!button) {
            return;
        }

        event.preventDefault();

        var wrap = button.closest('.va-copy-wrap');
        var source = wrap ? wrap.querySelector('[data-va-copy-source]') : null;
        if (!source) {
            return;
        }

        var text = (source.textContent || '').trim();
        if (!text) {
            return;
        }

        copyText(text).then(function () {
            flashCopied(button);
        }).catch(function () {
            // Fallback path failed too; do nothing. The user can still
            // select the secret manually — `user-select: all` on the
            // `<code>` element makes that a single click + Ctrl+C.
        });
    });

    function copyText(text) {
        if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
            return navigator.clipboard.writeText(text);
        }
        return new Promise(function (resolve, reject) {
            var textarea = document.createElement('textarea');
            textarea.value = text;
            textarea.setAttribute('readonly', '');
            textarea.style.position = 'absolute';
            textarea.style.left = '-9999px';
            document.body.appendChild(textarea);
            textarea.select();
            try {
                var ok = document.execCommand('copy');
                ok ? resolve() : reject(new Error('execCommand returned false'));
            } catch (e) {
                reject(e);
            } finally {
                document.body.removeChild(textarea);
            }
        });
    }

    function flashCopied(button) {
        button.classList.add(COPIED_CLASS);
        // Restart the CSS animation if the user mashes the button.
        button.style.animation = 'none';
        // eslint-disable-next-line no-unused-expressions
        button.offsetHeight;
        button.style.animation = '';

        clearTimeout(button._vaCopyTimeout);
        button._vaCopyTimeout = setTimeout(function () {
            button.classList.remove(COPIED_CLASS);
        }, COPIED_DURATION_MS);
    }

    //
    // Password show / hide toggle.
    //
    // Any element with `data-va-password-toggle` flips the sibling input
    // inside the same `.va-password-wrap` between type="password" and
    // type="text". Icon swap is driven by the native HTML `hidden`
    // attribute on each SVG — no CSS cascade dependency.
    //
    document.addEventListener('click', function (event) {
        var button = event.target && event.target.closest && event.target.closest('[data-va-password-toggle]');
        if (!button) {
            return;
        }
        event.preventDefault();

        var wrap = button.closest('.va-password-wrap');
        if (!wrap) {
            return;
        }
        var input = wrap.querySelector('input');
        if (!input) {
            return;
        }

        var willReveal = input.type === 'password';
        input.type = willReveal ? 'text' : 'password';

        var eye = button.querySelector('.va-icon-eye');
        var eyeOff = button.querySelector('.va-icon-eye-off');
        setHidden(eye, willReveal);
        setHidden(eyeOff, !willReveal);

        button.setAttribute('aria-label', willReveal ? 'Hide password' : 'Show password');
        button.setAttribute('aria-pressed', willReveal ? 'true' : 'false');
    });

    //
    // Light / dark theme toggle.
    //
    // The inline <head> script applies a stored choice (data-theme on <html>)
    // before first paint to avoid a colour flash. This handler flips the
    // choice on click and persists it; with no stored choice the page follows
    // the OS via the CSS prefers-color-scheme media query. Icon swap uses the
    // native `hidden` attribute — same no-CSS-cascade approach as the password
    // toggle above. The two SVGs:
    //   .va-icon-theme-light  → moon  ("switch to dark"), shown while in light
    //   .va-icon-theme-dark   → sun   ("switch to light"), shown while in dark
    //
    const THEME_KEY = 'va-theme';

    function resolveTheme() {
        const explicit = document.documentElement.dataset.theme;
        if (explicit === 'dark' || explicit === 'light') {
            return explicit;
        }
        // Guard .matches too: if matchMedia is absent the query is undefined.
        const mql = globalThis.matchMedia?.('(prefers-color-scheme: dark)');
        return mql?.matches ? 'dark' : 'light';
    }

    function paintToggle(button, theme) {
        const moon = button.querySelector('.va-icon-theme-light');
        const sun = button.querySelector('.va-icon-theme-dark');
        const dark = theme === 'dark';
        setHidden(moon, dark);
        setHidden(sun, !dark);
        button.setAttribute('aria-pressed', dark ? 'true' : 'false');
    }

    function syncToggles() {
        const theme = resolveTheme();
        for (const button of document.querySelectorAll('[data-va-theme-toggle]')) {
            paintToggle(button, theme);
        }
    }

    document.addEventListener('click', function (event) {
        const button = event.target?.closest?.('[data-va-theme-toggle]');
        if (!button) {
            return;
        }
        event.preventDefault();

        const next = resolveTheme() === 'dark' ? 'light' : 'dark';
        document.documentElement.dataset.theme = next;
        try {
            localStorage.setItem(THEME_KEY, next);
        } catch {
            // Private mode / storage disabled — the toggle still works for the
            // current page; the choice just won't persist across navigations.
        }
        syncToggles();
    });

    // Paint the correct icon once the DOM is ready (the button isn't in the
    // DOM yet when the inline anti-flash script runs in <head>).
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', syncToggles);
    } else {
        syncToggles();
    }

    // Follow OS changes live, but only while the user hasn't pinned a choice.
    // addEventListener is optional-called: older engines that expose only the
    // legacy MediaQueryList.addListener simply skip live-follow (no throw).
    const osTheme = globalThis.matchMedia?.('(prefers-color-scheme: dark)');
    osTheme?.addEventListener?.('change', function () {
        if (!document.documentElement.dataset.theme) {
            syncToggles();
        }
    });
})();
