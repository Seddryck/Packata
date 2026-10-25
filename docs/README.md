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

Navigation comes from each page's `title`, `nav_order`, and `nav_exclude` front matter. Documentation collection pages retain `/docs/<name>/`; published standalone guides have explicit permalinks. Use Jekyll's `relative_url` filter for local links so `/Packata` is preserved.

The `docs-pages.yml` workflow builds pull requests and deploys main to GitHub Pages. GitHub Pages must use **GitHub Actions** as its build source. Repository settings are not changed by this branch.
