# Documentation website

Packata uses Jekyll 4.4 and Just the Docs 0.12.0, the same theme as Expressif and DubUrl. The Packata palette lives in `_sass/color_schemes/packata.scss`, with small branding overrides in `_sass/custom/custom.scss`.

From the repository root, preview the site with Docker:

```powershell
docker compose -f jekyll-compose.yml up -d
```

Open http://localhost:4000/Packata/. The preview keeps the production base path so internal links can be checked. Set `JEKYLL_PORT` to use another local port. Stop the preview with `docker compose -f jekyll-compose.yml down`.

Build the production site:

```powershell
docker compose -f jekyll-compose.yml run --rm jekyll bash -lc "bundle install && JEKYLL_ENV=production bundle exec jekyll build --strict_front_matter --disable-disk-cache"
```

With Ruby 3.3 and Bundler installed, run `bundle install` and `bundle exec jekyll build --strict_front_matter` directly from `docs/`.

Navigation uses ordinary Jekyll pages with `title`, `parent`, optional `grand_parent`, `has_children`, `nav_order`, and `nav_exclude` front matter. The old documentation collection is folded into this single hierarchy; `/docs/why-packata/` and all existing standalone URLs retain explicit permalinks. Use Jekyll's `relative_url` filter for local links so `/Packata` is preserved. Utility pages also set `search_exclude: true`.

## Coverage and Quickstart checks

Run from the repository root with Node.js 22+:

```console
node .github/scripts/Validate-Documentation.mjs
node .github/scripts/Validate-Documentation.mjs --site
```

The second command requires a completed Jekyll build. The checks cover source command registrations, inherited options and aliases, positional arguments, navigation parents, legacy URLs, rendered internal links/fragments and sample downloads. Keep `<!-- cli:command path -->` markers attached to the matching reference section; aliases can share one marked section.

For runtime help and executable examples, first build the CLI for an installed framework:

```console
dotnet build src/Packata.Cli/Packata.Cli.csproj -f net10.0
node .github/scripts/Validate-Documentation.mjs --cli src/Packata.Cli/bin/Debug/net10.0/packata.dll
node .github/scripts/Test-DocumentationQuickstarts.mjs net10.0
```

Use `net8.0` or `net9.0` consistently to test those frameworks. The Quickstart checker creates a fresh directory under ignored `artifacts/`, uses an isolated configuration directory, and compiles the actual documented C# block with source project references. It validates the upcoming source version rather than installing a released global tool or assuming a published NuGet version contains unreleased features. Report which frameworks, tests, runtime help and builds were actually checked.

Runtime checks use an absolute .NET executable path rather than searching PATH. Standard Windows/Linux/macOS SDK locations work by default; for a custom installation set `DOTNET_HOST_PATH` to its absolute executable path or `DOTNET_ROOT` to the SDK host directory.

The [migration/coverage inventory](project-documentation.md) maps every existing page and feature group to its destination. Detailed new tutorials and generated API pages remain outside the MVP.

The `docs-pages.yml` workflow builds pull requests and deploys main to GitHub Pages. GitHub Pages must use **GitHub Actions** as its build source. Repository settings are not changed by this branch.
