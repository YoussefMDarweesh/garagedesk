/* GarageDesk service worker (see docs/PWA.md).
 *
 * What it does, and only this:
 *   1. Keeps copies of the app's STATIC files (CSS, JavaScript, libraries, icons) so the app opens fast and its
 *      look survives a bad connection.
 *   2. When a page cannot be fetched because the phone is offline, shows /offline.html instead of the browser's
 *      dinosaur.
 *
 * What it must never do:
 *   - Store a page. Every HTML page of the app is personal (a shop's customers, prices, invoices), so no HTML
 *     response is ever put in a cache, and nothing is ever answered from a cache except the offline page itself.
 *   - Touch the customer share pages (/s/...), the invitation page (/Join), the sign-in pages (/Identity/...), the
 *     shop logo (/shop/...) or anything that is not a plain GET to this site. Those requests are left completely to
 *     the browser, exactly as if no service worker existed.
 */
"use strict";

const CACHE = "gd-static-v1";
const OFFLINE_URL = "/offline.html";
const MAX_ENTRIES = 80;

// Only files under these folders are ever cached (they are the same for every user of the site).
const STATIC_PREFIXES = ["/css/", "/js/", "/lib/", "/icons/"];

// Never handled by the service worker at all. (Checked first, so it wins over everything else.)
const NEVER_EXACT = ["/s", "/join", "/identity", "/shop", "/healthz", "/setculture"];
const NEVER_PREFIXES = ["/s/", "/join/", "/identity/", "/shop/"];

const lower = (path) => path.toLowerCase();
const isNever = (path) => NEVER_EXACT.includes(lower(path)) || NEVER_PREFIXES.some((p) => lower(path).startsWith(p));
const isStatic = (path) => STATIC_PREFIXES.some((p) => path.startsWith(p));

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE)
      .then((cache) => cache.addAll([OFFLINE_URL, "/icons/icon-192.png"]))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => k.startsWith("gd-") && k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

// A response may be stored only when it is a normal, successful, shareable file.
function storable(response) {
  if (!response || response.status !== 200 || response.type !== "basic") return false;
  const cacheControl = (response.headers.get("Cache-Control") || "").toLowerCase();
  if (cacheControl.includes("no-store") || cacheControl.includes("private")) return false;
  if (response.headers.has("Set-Cookie")) return false;
  const type = (response.headers.get("Content-Type") || "").toLowerCase();
  return !type.startsWith("text/html"); // never a page
}

async function trim(cache) {
  const keys = await cache.keys();
  const surplus = keys.length - MAX_ENTRIES;
  for (let i = 0; i < surplus; i++) {
    // The offline page is the one entry that must stay.
    if (new URL(keys[i].url).pathname !== OFFLINE_URL) await cache.delete(keys[i]);
  }
}

async function staleWhileRevalidate(request) {
  const cache = await caches.open(CACHE);
  const cached = await cache.match(request);
  const network = fetch(request)
    .then(async (response) => {
      if (storable(response)) {
        await cache.put(request, response.clone());
        await trim(cache);
      }
      return response;
    })
    .catch(() => cached);
  return cached || network;
}

self.addEventListener("fetch", (event) => {
  const request = event.request;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;
  if (isNever(url.pathname)) return;

  if (request.mode === "navigate") {
    // Always the network. Only if it fails (offline) is the generic offline page shown. The page itself is never stored.
    event.respondWith(fetch(request).catch(() => caches.match(OFFLINE_URL)));
    return;
  }

  if (isStatic(url.pathname)) {
    event.respondWith(staleWhileRevalidate(request));
  }
});
