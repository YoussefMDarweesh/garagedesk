# GarageDesk design system

How the UI is put together, and why. Everything lives in `src/GarageDesk.Web/wwwroot/css/site.css` (one file,
no build step) plus a little vanilla JavaScript in `wwwroot/js/site.js`.

## The idea

A mechanic's bench, not a SaaS template.

- **Chrome is dark navy, like a tool chest.** The floating rail and the hero cards are the only dark surfaces in
  light mode, so the eye goes to the navigation and to the one thing that matters on a page.
- **One strong accent: safety orange.** It is used for the active nav item, primary buttons, the chart line and the
  car. Everything else is neutral, so orange always means "this".
- **A big calm white sheet** on a tinted backdrop, very rounded cards (16-30 px) and soft shadows. Whitespace
  does the separating; borders are quiet.
- **Distinctly "garage":** a car silhouette we drew ourselves (top-down, `_CarSilhouette.cshtml`), licence plates
  as real UI elements, kilometres and service intervals instead of generic "metrics".

## Tokens

All colour, radius, shadow, spacing and motion values are CSS variables at the top of `site.css`.

| Group | Examples |
|---|---|
| Brand | `--accent`, `--accent-strong` (text/buttons, AA contrast), `--accent-soft`, `--ink`, `--ink-2` |
| Surfaces | `--backdrop`, `--sheet`, `--surface`, `--surface-2`, `--surface-3`, `--line` |
| Text | `--text`, `--text-2`, `--text-3` |
| State | `--ok`, `--warn`, `--bad`, `--info` and their `-soft` backgrounds |
| Shape | `--r-xs` ... `--r-xl`, `--r-pill` |
| Depth | `--sh-1`, `--sh-2`, `--sh-3`, `--ring` (focus) |
| Motion | `--ease`, `--dur-1/2/3` |

Light and dark are two palettes of the same tokens (`[data-bs-theme="dark"]`). Dark is deep navy, not black.
Bootstrap's own variables are mapped onto the tokens, so its grid, forms and dropdowns follow the theme.

### Per-shop accent

A shop picks one of six presets (orange, blue, green, red, purple, teal) in **Shop settings**. It is stored in
`Shop.AccentColor`, carried in the login cookie as a claim and applied as `<html data-accent="...">`, which
re-points the accent tokens. Only names from `Accents.Presets` are ever written into the page.

## Components

App shell (floating rail, top bar, bottom tab bar on phones) · stat cards · activity chart (inline SVG, drawn in
`site.js`) · timeline · status tiles with meters · vehicle hero · data tables (sticky header, avatars, chips) ·
pager · segmented control · toasts · confirm dialog · empty states · split-screen auth pages.

## Behaviour rules

- **RTL:** logical properties only (`inline-start/end`, `margin-inline`, ...). A test fails if a physical
  `left`/`right` property sneaks into the stylesheet. Phone numbers, VINs and amounts are isolated `ltr`.
- **Motion:** hover lifts, the chart draw-in and toast slide-ins are all switched off under
  `prefers-reduced-motion`.
- **Accessibility:** visible focus ring on everything, `aria-current` on navigation, an accessible data table
  behind the chart, keyboard arrows move the chart's highlighted month, toasts use `aria-live`.
- **No JavaScript?** Pages still work: forms post, links link, server messages stay visible in place, and the
  chart's numbers are in the table.
- **Responsive:** designed down to 360 px; the rail becomes a bottom tab bar below 768 px.

## Fonts

The UI uses a system font stack (`Inter` / Segoe UI Variable / system-ui, and IBM Plex Sans Arabic / Segoe UI
for Arabic). Self-hosted `woff2` files can be dropped into `wwwroot/fonts/` and declared with `@font-face`
without touching anything else; the variables `--font-sans` and `--font-ar` are the only switch.

## Screenshots

See `docs/screenshots/`.
