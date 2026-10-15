# Installable app (PWA)

GarageDesk can be added to a phone's home screen and opens like an app (no browser bars). There is no separate app
and no build step: it is a web manifest, a small service worker and a few icons.

| File (under `src/GarageDesk.Web/wwwroot`) | What it is |
|---|---|
| `manifest.webmanifest` | Name, colours, `start_url` `/`, `display: standalone`, icons |
| `icons/` | `icon.svg` (the brand mark), PNG 192 and 512, a maskable 512 and the iOS `apple-touch-icon.png` |
| `sw.js` | The service worker (scope `/`) |
| `offline.html` | The page shown when a page cannot be loaded because the device is offline (English and Arabic, no shop data) |
| `js/site.js` (end of file) | Registers the worker and shows a once-per-device "Install" banner |

## What the service worker caches (and what it never touches)

* **Caches:** static files only, from `/css/`, `/js/`, `/lib/` and `/icons/`, plus `offline.html`. Stale-while-revalidate,
  at most 80 entries. A response is stored only if it is a normal 200 that is not HTML, not `no-store`/`private` and has no cookie.
* **Offline:** a page navigation always goes to the network. Only if that fails does the visitor see `offline.html`.
  **No HTML page is ever stored or answered from a cache.** Every page of the app is personal data.
* **Never handled at all** (the browser deals with them exactly as if there were no service worker): the customer share
  pages `/s/...`, the invitation page `/Join/...`, the sign-in pages `/Identity/...`, the shop logo `/shop/...`, the
  health check, the language switch, and anything that is not a same-site GET.

`tests/GarageDesk.Tests/PwaTests.cs` pins these rules (the never-list is checked first, there is exactly one cache write,
the navigation branch has none).

## Installing

* **Android / Chrome / Edge:** a banner offers "Install"; or use the browser menu. The browser needs HTTPS (or localhost).
* **iPhone / iPad (Safari):** Share, then "Add to Home Screen". There is no install event on iOS, so the banner shows the how-to line.
* **Dismissing the banner** is remembered on that device (`localStorage`); once installed it never shows again.

## Updating

Change `CACHE` in `sw.js` (for example `gd-static-v2`) to drop every old cached file at the next visit. File names with a
`?v=` version (CSS and JavaScript) are cached per version, so normal releases need no change.
