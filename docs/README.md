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

Each ordinary Jekyll page defines its place in navigation through front matter. Use `title`,
`parent` and, for a third level, `grand_parent`. A parent declares `has_children`; `nav_order`
and `nav_exclude` control ordering and visibility.

The former documentation collection is part of this single hierarchy. Explicit permalinks
retain `/docs/why-packata/` and the existing standalone URLs. Build local links with
`relative_url` so they retain the `/Packata` base path. Utility pages also set
`search_exclude: true`.

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

To test .NET 8 or 9, use `net8.0` or `net9.0` consistently in the commands above.
The checker creates a fresh directory under ignored `artifacts/`, isolates configuration,
and compiles the C# block from the Quickstart using source project references.

This verifies the upcoming source version. It does not install a released global tool or
assume that a published NuGet package contains unreleased features. Report the frameworks,
tests, runtime help and builds you actually checked.

Runtime checks use an absolute .NET executable path rather than searching PATH. Standard Windows/Linux/macOS SDK locations work by default; for a custom installation set `DOTNET_HOST_PATH` to its absolute executable path or `DOTNET_ROOT` to the SDK host directory.


The `docs-pages.yml` workflow builds pull requests and deploys main to GitHub Pages. GitHub Pages must use **GitHub Actions** as its build source. Repository settings are not changed by this branch.
