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
        // SVGElement does not implement the `hidden` IDL property (it lives on
        // HTMLElement), so `svg.hidden = …` would set a no-op expando and leave
        // the content attribute untouched. Toggle the attribute directly so the
        // CSS `svg[hidden]` rule actually swaps the eye / eye-off icons.
        if (eye) { eye.toggleAttribute('hidden', willReveal); }
        if (eyeOff) { eyeOff.toggleAttribute('hidden', !willReveal); }

        button.setAttribute('aria-label', willReveal ? 'Hide password' : 'Show password');
        button.setAttribute('aria-pressed', willReveal ? 'true' : 'false');
    });

    //
    // Modal dialogs (native <dialog>).
    //
    // [data-va-modal-open="<dialog-id>"] opens the matching <dialog> via
    // showModal() (free backdrop, Esc-to-close, focus trap). [data-va-modal-close]
    // closes the enclosing dialog, and a click on the backdrop (the <dialog>
    // element itself, outside its content) closes it too. Delegated on document
    // so htmx-swapped cards keep working.
    //
    document.addEventListener('click', function (event) {
        var target = event.target;
        if (!target || !target.closest) {
            return;
        }

        var opener = target.closest('[data-va-modal-open]');
        if (opener) {
            event.preventDefault();
            var dialog = document.getElementById(opener.getAttribute('data-va-modal-open'));
            if (dialog && typeof dialog.showModal === 'function') {
                dialog.showModal();
            }
            return;
        }

        var closer = target.closest('[data-va-modal-close]');
        if (closer) {
            event.preventDefault();
            var owning = closer.closest('dialog');
            if (owning) {
                owning.close();
            }
            return;
        }

        // Click on the backdrop (the dialog element, not its inner content).
        if (target.tagName === 'DIALOG' && target.classList.contains('va-modal')) {
            target.close();
        }
    });

    //
    // Language switcher: close the open flag popover on outside-click / Escape.
    //
    // The switcher is a native <details class="va-lang">, so it already toggles
    // and selects with zero JS; this only adds the niceties. Delegated on
    // document so it works after htmx swaps and on both admin and end-user pages.
    //
    document.addEventListener('click', function (event) {
        document.querySelectorAll('details.va-lang[open]').forEach(function (d) {
            if (!d.contains(event.target)) {
                d.removeAttribute('open');
            }
        });
    });

    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') {
            return;
        }
        document.querySelectorAll('details.va-lang[open]').forEach(function (d) {
            d.removeAttribute('open');
            var summary = d.querySelector('summary');
            if (summary) {
                summary.focus();
            }
        });
    });
})();
