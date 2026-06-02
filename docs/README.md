# SimdUnicode documentation site

This folder contains the source for the SimdUnicode documentation site, built with
[DocFX](https://dotnet.github.io/docfx/). The published site lives on the `gh-pages` branch
and is served by GitHub Pages.

## Layout

| Path | Purpose |
|------|---------|
| `docfx.json` | DocFX configuration (API metadata + site build). |
| `index.md` | Landing page (custom HTML hero, benchmark bars). |
| `articles/` | Conceptual docs: getting started, how it works, benchmarks, contributing. |
| `api/` | API reference. The `*.yml` files are **generated** from the source XML comments and are git-ignored. |
| `template/` | Custom theme overriding the `modern` template (`public/main.css`, `public/main.js`). |
| `logo.svg` | Site logo and favicon. |

## Build locally

Install DocFX once:

```bash
dotnet tool update -g docfx
```

Then build and preview with live reload:

```bash
cd docs
docfx docfx.json --serve
# open http://localhost:8080
```

A one-shot build (output in `docs/_site`):

```bash
docfx docs/docfx.json
```

## Publishing

Publishing is automated by [`.github/workflows/docs.yml`](../.github/workflows/docs.yml):

- **Manually** — run the *Documentation* workflow from the GitHub Actions tab.
- **On release** — it runs automatically whenever a GitHub Release is published.

The workflow builds the site and pushes `docs/_site` to the `gh-pages` branch.
Make sure GitHub Pages is configured to serve from the `gh-pages` branch (root) in
**Settings → Pages**.
