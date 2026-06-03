# docs/assets

Images for the documentation site. Paths here are referenced **repo-relative**
from the Markdown under `docs/`, so they resolve both on GitHub and on the
published MkDocs site.

- `screenshots/` — UI screenshots and GIFs (admin dashboard, end-user flows,
  theming, language switcher, …).

The `.svg` files currently in `screenshots/` are **placeholders**. Replace them
with real captures following [`../CAPTURE_CHECKLIST.md`](../CAPTURE_CHECKLIST.md).

When you replace a placeholder with a real `.png` / `.gif`, update the single
`![…](…)` reference in the page that uses it (or keep the `.svg` filename and
overwrite — whichever you prefer; the checklist tracks the canonical name).
