# Share links: threat model

Share links (v1.2) are the only part of GarageDesk that is reachable **without logging in**. A shop owner sends a
customer a WhatsApp message with a link such as `https://<host>/s/<token>`; the customer opens it and sees one
invoice, one estimate or the status of one car. This page explains what protects those links and what does not.

## What is exposed

| Link kind | The customer sees |
|---|---|
| Invoice | Shop name, logo, address and phone; invoice number and date; their own name; car make, model, year and plate; the lines, totals, discount, VAT, amount received and balance due |
| Estimate | The same without payments, plus the "valid until" date |
| Status | Shop name, logo and phone; plate and model; the four-step status (Booked, In progress, Ready, Paid); the date of the last change; the balance due if there is one |

Never shown: internal notes (job, estimate, customer), payment notes and methods, other customers, other cars,
database ids, staff names or emails, the customer's phone number, mileage readings, links into the app.

This is enforced by construction: the public pages render a small read-only view object
(`PublicShareView`), not the database entities, so a field that is not in that object cannot reach the HTML.

## The token

* 32 random bytes (256 bits) from `RandomNumberGenerator`, written as 43 URL-safe characters. Nothing is derived
  from an id, a time or the shop.
* Only the **SHA-256 hash** of the token is used to find a link (`ShareLinks.TokenHash`, unique index). A copy of
  the database does not contain a usable link.
* The owner must be able to copy the same link again later, so the token is also stored **encrypted** with ASP.NET
  Core Data Protection (`TokenProtected`). The encryption keys are not in the database. If the keys are lost
  (see "Known limits"), old links keep working for customers; the owner just cannot re-display them and a new link is
  created on the next send.
* Links expire after 30 days (`ShareLinks:ExpiryDays`) and can be revoked by the owner at any time. Revocation is a
  database update that is checked on every request, so it takes effect immediately.

## The public request

`GET /s/{token}` (and `GET /s/{token}?handler=logo` for the shop logo):

1. The rate limiter runs first (built in to ASP.NET Core): 60 requests per minute per visitor address by default
   (`ShareLinks:PermitPerMinute`), then `429`.
2. The token must have the exact shape (43 characters of `A-Z a-z 0-9 - _`), otherwise `404` without any database work.
3. The link is looked up by hash only, compared again in constant time, and checked for expiry and revocation.
4. **Unknown, malformed, expired and revoked tokens produce the same bare `404`**: same status, same empty body,
   same headers. There is no oracle that tells an attacker a token "was once valid".
5. Only now are the link's own `ShopId` and `TargetId` used. This is the single place in the app that reads across
   the tenant query filter (`IgnoreQueryFilters`), and every query repeats `ShopId == link.ShopId`
   (job card, vehicle, customer, lines, payments, estimate, shop, logo). A tampered `TargetId` that points at
   another shop's job card finds nothing and answers `404` (test: `A_link_can_never_show_another_shops_data...`).
6. The URL contains nothing but the token. No shop id and no job or estimate id is read from the path, query or form.

Response headers on everything under `/s`, including errors: `Cache-Control: private, no-store`,
`X-Robots-Tag: noindex, nofollow, noarchive`, `Referrer-Policy: no-referrer`, `X-Content-Type-Options: nosniff`.
The page also carries `<meta name="robots">` and `<meta name="referrer">`. The page loads only same-origin files.

The public page is read only: it has no form that changes data. The only POST on it is the language switch
(`/SetCulture`), which sets a cookie and redirects to a local URL.

## The owner side

Creating, listing, copying and revoking links go through the normal signed-in pages and the DbContext's tenant
filter, so an owner can only create links for their own shop's job cards and estimates and can only revoke their own
links (cross-shop attempts answer `404`). "Send on WhatsApp" is a plain `https://wa.me/...` link opened by the
owner's browser; the server sends nothing.

The shop's message template is plain text with four placeholders (`{name}`, `{plate}`, `{shop}`, `{link}`), replaced
in one pass by plain strings. There is no format string, no expression evaluation and inserted values are never
re-scanned, so a customer named `{link}` cannot inject anything.

## Threats and what is done about them

| Threat | Mitigation |
|---|---|
| Guessing a token | 256-bit random token; rate limit; identical 404s |
| Database leak | Only hashes are used for lookup; the stored encrypted copy needs the Data Protection keys, which are not in the database |
| Enumerating shops or objects through the URL | No ids are accepted on the public path |
| Reading another shop's data through a manipulated link row | Every public query is constrained by the link's `ShopId`; test with a tampered `TargetId` |
| A link that stays alive forever | 30-day expiry, owner revoke, immediate effect |
| Search engines or caches keeping a page | `noindex`, `no-store`, no shared caches |
| The token leaking through the `Referer` header | `Referrer-Policy: no-referrer`; no third-party resources |
| Leaking internal data by accident | Pages render a minimal view object, not entities; tests assert that notes, phone numbers and ids are absent |
| Hammering the endpoint | Per-address fixed window rate limit, `429` |
| Counting link-preview bots as customer views | Requests from WhatsApp and common preview crawlers are not counted as views |

## Known limits (be honest about them)

* **Anyone who has the link can see it.** That is the design: a link forwarded in a chat shows the document to
  whoever receives it, until it expires or the owner revokes it. Revoke links of documents that should no longer be
  visible.
* **Data Protection keys are not persisted yet.** On a container host the keys live on the container's disk and are
  lost on redeploy. Effect: the owner cannot re-display links created before the redeploy (customers are
  unaffected). Persisting the keys (the growth item that needs the
  `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` package) fixes this, and also keeps owners signed in.
* **The rate limit is per address.** Behind the host's proxy the address is the visitor's only when forwarded
  headers are trusted (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, already set in the Dockerfile). Visitors sharing
  one address (a mobile carrier's NAT) share one budget.
* **Generated links use the request's Host header.** Set `AllowedHosts` to the real domain in production so a
  forged `Host` header cannot make the app print a link to another domain.
* **No CSP header yet.** The pages use a small inline script (the theme switch). A content security policy with a
  hash for it is a possible follow-up.
* **Link previews:** WhatsApp fetches a link to build its preview. It gets the same public page as a customer (title
  only, which contains the plate or invoice number).
