// VisuAuth — header enhancements (account menu + sidebar collapse).
//
// Progressive enhancement only. The account menu is a native
// <details class="va-user-details">, so it already opens/closes with zero JS
// (and works if this file fails to load). This script adds the niceties:
//   1. Close any open account menu on outside-click or Escape.
//   2. Wire the sidebar collapse toggle ([data-va-collapse]) — toggles the
//      `.va-collapsed` class on `.va-shell` and persists the choice.
//
// Theme toggling lives in va-theme-init.js (separate, loaded synchronously).
(function () {
    'use strict';

    // ── 1. Account menu: outside-click + Escape close ──────────────────────
    document.addEventListener('click', function (event) {
        document.querySelectorAll('details.va-user-details[open]').forEach(function (d) {
            if (!d.contains(event.target)) {
                d.removeAttribute('open');
            }
        });
    });

    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') {
            return;
        }
        document.querySelectorAll('details.va-user-details[open]').forEach(function (d) {
            d.removeAttribute('open');
            const summary = d.querySelector('summary');
            if (summary) {
                summary.focus();
            }
        });
    });

    // ── 2. Sidebar collapse toggle + persistence ───────────────────────────
    const COLLAPSE_KEY = 'va-sidebar-collapsed';

    function shell() {
        return document.querySelector('.va-shell');
    }

    function applyCollapsed(collapsed) {
        const el = shell();
        if (el) {
            el.classList.toggle('va-collapsed', collapsed);
        }
    }

    // Restore persisted state on load.
    try {
        applyCollapsed(localStorage.getItem(COLLAPSE_KEY) === '1');
    } catch { /* storage disabled — start expanded */ }

    document.addEventListener('click', function (event) {
        const toggle = event.target.closest?.('[data-va-collapse]');
        if (!toggle) {
            return;
        }
        event.preventDefault();
        const el = shell();
        if (!el) {
            return;
        }
        const collapsed = el.classList.toggle('va-collapsed');
        try {
            localStorage.setItem(COLLAPSE_KEY, collapsed ? '1' : '0');
        } catch { /* not persisted, fine for this session */ }
    });
})();
