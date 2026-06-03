# Contributing

Issues, discussions, and pull requests are welcome.

The authoritative contribution policy lives in
[`CONTRIBUTING.md`](https://github.com/VisuAuth/VisuAuth/blob/main/CONTRIBUTING.md)
at the repository root. In short:

- **Trunk-based.** `main` is the only long-lived branch; every change lands
  through a PR with a `feat/`, `fix/`, `docs/`, `chore/`, … prefix.
- **[Conventional Commits](https://www.conventionalcommits.org)** for commit
  and PR titles. **Squash merge only** — the PR title becomes the commit.
- **English only.** Everything that lands on disk is in English (code,
  comments, docs, commit messages).
- **Tests required.** New behaviour ships with tests; every bug fix ships with
  a regression test.

## Editing these docs

The documentation site is built with [MkDocs Material](https://squidfunk.github.io/mkdocs-material/)
from the plain Markdown under [`docs/`](https://github.com/VisuAuth/VisuAuth/tree/main/docs).
The same files render both on GitHub and on the published site, so keep image
paths repo-relative and avoid Material-only Markdown extensions.

Preview locally:

```bash
pip install mkdocs-material
mkdocs serve   # http://127.0.0.1:8000
```

Screenshots and GIFs live under `docs/assets/`. When adding UI imagery, follow
[`docs/CAPTURE_CHECKLIST.md`](https://github.com/VisuAuth/VisuAuth/blob/main/docs/CAPTURE_CHECKLIST.md)
so captures stay consistent (page, theme, sample data).
