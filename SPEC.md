# GarageDesk — Product Spec (MVP)

> Working name. Rename freely.

## 1. Problem
Small car service shops in Egypt (general mechanics, coding/tuning shops, detailing) track jobs on paper, WhatsApp chats, or Excel. They lose service history, forget to remind customers about the next service, and write invoices by hand.

## 2. Who uses it
- **Shop owner** (admin): sets up the shop, sees everything.
- Later (not MVP): staff accounts with limited roles.

Each shop is a separate tenant. One shop must never see another shop's data.

## 3. MVP features

### M1 — Shop account
- Owner signs up with email + password, then creates their shop (name, phone, address, currency fixed to EGP).
- Every record below belongs to exactly one shop.

### M2 — Customers & vehicles
- Customer: name, phone (Egyptian format, stored as +20...), notes.
- Vehicle: plate number, VIN (optional), make, model, year, current mileage, owner (customer).
- Search by customer name, phone, or plate.
- **Done when:** I can add a customer with two cars and find either car by plate.

### M3 — Job cards
- A job card is one visit: vehicle, date in, mileage at visit, status (Booked → In progress → Done → Paid), notes.
- Job lines: type (Labor or Part), description, quantity, unit price. Totals calculated automatically (line total = quantity × unit price, rounded to the piastre; job total = sum of the lines).
- Status moves one step at a time (Booked → In progress → Done → Paid). One step back is allowed to fix a mistake, except out of Paid: a paid job card is final and locked.
- **Done when:** I can open a job for a car, add 2 parts + 1 labor line, and see the correct total.

### M4 — Service history
- Vehicle page shows every past job card, newest first, with totals.
- A job card's mileage reading raises the vehicle's current mileage when it is higher (when the job card is opened or its mileage corrected). A lower reading never lowers it.
- **Done when:** a car with 3 past visits shows all 3 with dates, mileage, and what was done.

### M5 — Reminders + WhatsApp
- Reminder: vehicle, type (e.g. oil change), due date and/or due mileage, done flag.
- Dashboard lists reminders due in the next 14 days or overdue.
- "Send on WhatsApp" button opens `https://wa.me/<phone>?text=<message>` with a pre-filled, editable message. No paid API.
- "Due" rules: a reminder is **overdue** once its date has passed or the vehicle's mileage is past the due mileage; it is **due soon** within 14 days or within 500 km of the due mileage. With both a date and a mileage, whichever comes first counts. The WhatsApp message follows the shop's current language.
- When a job card is marked Done, the page offers to create the next oil change reminder (suggested: 6 months and +10,000 km, both editable).
- **Done when:** an overdue oil-change reminder appears on the dashboard and the button opens WhatsApp with the customer's number and message.

### M6 — Invoice
- Printable invoice page for a job card (shop details, customer, car, lines, total in EGP).
- User prints or saves as PDF from the browser. No PDF library needed.
- **Done when:** the invoice prints cleanly on one A4 page.
- Decisions: the invoice lives at `/Jobs/Invoice/{id}` and is reached from the job card page ("Invoice / Print"). It reuses the job card's own totals code, so the two can never disagree. The invoice number is the job card number (`#id`) and the date is the visit date; there is no separate invoice sequence yet. Payment status is "Paid" only when the job card is Paid. Internal notes are never printed. Printing hides the app chrome, always uses white paper with dark text whatever the theme, repeats the table header on every page and never splits a row or the totals block. A typical job (about 15 lines) fits one A4 page.
- Not included: emailing invoices, invoice numbering sequences, taxes/VAT, discounts, server-side PDF generation.

### M7 — Ship
- Goal: a demo shop with seed data, a deployable package, and a README with screenshots and a short demo video. **Status: done except hosting.** The app is deployment-ready (Docker image, health check, seeder, deploy guide) but is **not deployed**: no host has been chosen, so there is no live URL, and the README says so. The demo video is recorded by hand from `docs/DEMO_SCRIPT.md`.
- Decisions: the demo shop is created by `dotnet run --project src/GarageDesk.Web -- --seed-demo`, which makes the shop "Demo Motors", its owner login and the data, then exits without starting the website. The data is 10 customers, 15 cars (three each of Skoda, Volkswagen, Hyundai, Kia and Toyota), about 50 job cards over the last year (every status) and a mix of overdue, due-soon, upcoming and finished reminders. All dates are relative to today, so the dashboard always has due reminders. It only fills an empty shop, so running it twice changes nothing, and it refuses to run if migrations are pending. The login is `Demo:Email` (default `demo@garagedesk.app`) with `Demo:Password`; without a password setting a random one is printed once. No password is stored in the repository.
- Deployment (packaging only, not hosted yet): the app is packaged as a Docker container (`Dockerfile`: SDK build stage, ASP.NET runtime stage, non-root user, listens on Render's `PORT`, no secrets in the image) so that it can run on a Docker host such as Render's free web service, with the Neon database. The guide assumes a host that auto-deploys from `main` on every push, so no deploy secrets need to live in GitHub; `ci.yml` builds, tests and also builds the image and calls `/healthz`. The connection string is the `ConnectionStrings__Default` environment variable in Render's dashboard, and `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes the app trust the proxy's HTTPS headers. `/healthz` answers `ok` without touching the database, so a sleeping Neon never fails the health check. Migrations are applied by hand from a developer machine (`dotnet ef database update`), never automatically on startup. Step by step: `docs/DEPLOY.md`.
- Not included: a "reset demo" command, applying migrations at startup, staging environments, a custom domain.
- README (M8): problem statement, screenshots of the seeded demo shop, features, two Mermaid diagrams (architecture and the multi-tenancy rule), key decisions, test summary, run-locally steps, an honest deployment status, how the AI agents were used, known limitations and what comes next. The demo video script is `docs/DEMO_SCRIPT.md`. The demo video itself is not in the repository.

### UI — Arabic and dark mode
- English is the default language. Arabic (right-to-left) is chosen with the navbar switcher; the choice is kept in a cookie and anything unsupported falls back to English.
- Light and dark themes: the theme follows the OS setting by default, and a navbar toggle overrides it and is remembered in the browser.
- **Done when:** switching to Arabic flips the pages to right-to-left with translated text and validation messages, and the theme can be switched (and survives a reload) without a flash.

### Shop logo
- A shop owner can upload their shop's logo (PNG, JPEG or WebP, up to 512 KB; SVG is not accepted) and remove it again, from a "Shop settings" page.
- The logo is shown in the navigation bar and on the home page, and is reachable by other pages (e.g. the invoice) at `/shop/logo`.
- Each shop only ever sees its own logo; the URL carries no shop id.
- **Done when:** I can upload a logo and see it in the navbar, a second shop cannot fetch it, and a non-image or oversized file is refused with a clear message.

### UI — app shell and dashboard
- Signed-in pages share one app shell: a floating navigation rail (a bottom tab bar on phones), a top bar with search, the due-reminders bell, language and light/dark switches and the user menu, and the shop logo.
- The home page is a dashboard of real, shop-scoped numbers: customers, vehicles, open job cards, revenue this month (paid job cards by visit date), a revenue/job-card activity chart by month, due reminders and the recent service history, with friendly empty states.
- A vehicle's page shows a hero card, status tiles (kilometres and days since the last service, reminders, visits) computed only from data we already store, and its service-history timeline.
- A shop can choose its accent colour from a small preset palette in Shop settings.
- Design notes: `docs/DESIGN.md`.

### v1.2 — Share links and the customer status page
- The owner sends a customer a WhatsApp message with a **link** (`/s/{token}`); the customer opens it with no login and sees one invoice, one estimate or the status of one car (Booked, In progress, Ready, Paid, last updated, balance due).
- Links are secret (256-bit random token, only its SHA-256 hash is used to find the link), expire after 30 days and can be revoked at once. Unknown, expired and revoked links give the same 404. The public path takes no shop or object id. Pages are read-only, minimal, `noindex`, `no-store`, no referrer, and rate limited per visitor address. Details and limits: `docs/SECURITY_SHARE_LINKS.md`.
- **Send on WhatsApp** buttons on the job card (status, invoice), the invoice and the estimate create or reuse the live link and open `wa.me` with the message; nothing is sent by the server. A link manager on the job card and the estimate lists live links with copy, views, last viewed and revoke.
- The message wording is editable per shop in Shop settings (English and Arabic, placeholders `{name}`, `{plate}`, `{shop}`, `{link}`); empty means the built-in wording. After a job is marked Done a toast offers "Tell the customer on WhatsApp".
- The customer's page uses the language the link was sent in, with a language switch, right-to-left in Arabic, dark mode and print.

## 4. Not in the MVP
Online payments, inventory/stock, multi-branch, staff roles, mobile app, SMS, email notifications.

## 5. Tech decisions (all free)
| Area | Choice | Why |
|---|---|---|
| Framework | ASP.NET Core (.NET 10 LTS), Razor Pages | One project, server-rendered, easiest .NET web stack |
| Data | EF Core + PostgreSQL on Neon (free plan) | Same database in dev and production, no install |
| Auth | ASP.NET Core Identity (individual accounts) | Built in, no extra service |
| UI | Bootstrap 5 (comes with the template) | No frontend build step |
| Languages | Built-in ASP.NET Core localization, one `.resx` table (English default, Arabic) | No extra packages |
| Theme | Bootstrap 5.3 `data-bs-theme` (light/dark) | No extra CSS framework |
| Multi-tenancy | `ShopId` on every shop-owned table + EF Core global query filter | Simplest safe SaaS pattern |
| Hosting | Not chosen yet. The app is packaged as a Docker container, ready for a host such as Render (guide: docs/DEPLOY.md) | A card-free free tier is the constraint; the database stays on Neon |
| CI | GitHub Actions: build + test on every push | Free for public repos |
| Tests | xUnit | Standard for .NET |

## 6. Data model (first version)
- `Shop` (Id, Name, Phone, Address, AccentColor?)
- `ShopLogo` (Id, ShopId, Bytes, ContentType, UpdatedAtUtc), one per shop, stored in the database
- `AppUser` (Identity user + ShopId)
- `Customer` (Id, ShopId, Name, Phone, Notes)
- `Vehicle` (Id, ShopId, CustomerId, Plate, PlateKey, Vin, Make, Model, Year, Mileage) — `PlateKey` is the plate reduced to letters/digits, used for searching
- `JobCard` (Id, ShopId, VehicleId, DateIn, MileageAtVisit, Status, Notes)
- `JobLine` (Id, ShopId, JobCardId, Type, Description, Quantity, UnitPrice) — carries its own `ShopId` like every shop-owned row
- `Reminder` (Id, ShopId, VehicleId, Type, DueDate?, DueMileage?, IsDone)
- `ShareLink` (Id, ShopId, Kind, TargetId, TokenHash, TokenProtected?, Language, CreatedAtUtc, ExpiresAtUtc, RevokedAtUtc?, LastViewedAtUtc?, ViewCount): a secret public link to one invoice, estimate or car status. `Shop` also has `ShareMessageEn?` / `ShareMessageAr?`, and `JobCard` has `StatusChangedAtUtc?`.

## 7. v1.3 "My team": staff accounts, roles, technicians, commissions
- Roles: **Owner** (everything), **Cashier** (customers, vehicles, job cards, payments, invoices, estimates, receivables; no settings, staff or reports), **Technician** (only job cards assigned to them and their vehicles' history; may move a job Booked, In progress, Done and note parts and labor; never sees prices, totals, payments or customer phone numbers). One role, one shop per person. The person who registers a shop is its Owner. Details and enforcement: `docs/ROLES.md`.
- Team page (owner): invite with a one-time link (hashed, expiring, single use), change role, activate or deactivate (deactivated users cannot sign in and their sessions stop), commission percent. The last active owner cannot be demoted or deactivated.
- Job cards have an optional assignee (an active technician or owner of the same shop). "My jobs" lists the signed-in person's job cards.
- Commissions report (owner, by month): labor total of Paid job cards assigned to the person (by visit month, before discount and VAT) times their percent.
- History (owner): an append-only list per job card of who created it, changed its status, took or voided a payment, or assigned it.
- Data model additions: `AppUser` (Role, IsActive, FullName, CommissionPercent), `StaffInvite` (Id, ShopId, TokenHash, Role, FullName?, Phone?, CreatedByUserId, CreatedAtUtc, ExpiresAtUtc, UsedAtUtc?, UsedByUserId?, RevokedAtUtc?), `JobCard.AssignedToUserId?`, `JobAuditEntry` (Id, ShopId, JobCardId, Kind, Detail, UserId?, UserName, AtUtc).

## 8. v1.4 "Stock and profit": parts, expenses, profit, cash close, export
- **Parts catalog and stock.** `Part` (name, SKU?, unit, cost price, sell price, quantity on hand, low-stock alert?, supplier?, active). A job line, a technician note or an estimate line can pick a part. Adding a catalog line to a job card takes its quantity from stock (refused when the shelf does not hold enough, unless the owner turned on "allow negative stock") and copies the part's current cost onto the line; taking the line off puts the quantity back. Finishing a job changes nothing more, and a Paid job card is locked, so its stock cannot move. An estimate never touches stock; converting it to a job card takes the quoted catalog parts once (and is refused, creating nothing, when the shelf cannot cover them).
- **The stock ledger.** Every change of a quantity is a `StockMovement` (Opening, Purchase, JobUse, JobReturn, Adjustment), added in the same save as the new quantity; movements are append-only. The quantity has a concurrency check, so two simultaneous changes cannot overwrite each other. Receiving stock sets the part's cost to the new price and can record a "Parts purchase" expense.
- **Expenses.** `Expense` (date, category, amount, note, paid from the drawer?) and an editable list of `ExpenseCategory` (Rent, Salaries, Parts purchase, Utilities, Other to start with; used ones are archived, not deleted). Monthly list with totals by category.
- **Profit.** Monthly profit is cash basis: payments received in the month (by `PaidOn`) minus expenses dated in the month. Per job: (subtotal - discount, before VAT) - the cost recorded on its catalog part lines. Labor has no cost; free-text parts have none known and the page says how many.
- **Daily cash close.** For a day: payments received by method, expenses paid, expected cash = opening cash (typed, suggested from the last close) + cash received - expenses paid from the drawer. The owner types the counted cash; the difference is saved with the figures as a `CashClose`, which is immutable. A correction is a new close; the newest one for a day counts, and the page flags a close whose payments or expenses changed afterwards.
- **CSV export.** Payments, expenses and job cards for up to 366 days: UTF-8 with a byte order mark, comma separated, CRLF, header titles in the UI language, and cells starting with `=`, `+`, `-`, `@` (or a tab or carriage return) prefixed with a single quote.
- **Who sees what.** Owner: everything. Cashier: the catalog with sell prices and quantities, the running-low list, using parts on job cards; never cost, stock value, profit, expenses, cash close or export. Technician: part names and quantities in the picker on their own job pages and nothing with money; a catalog part they add is priced from the catalog on the server.
- **Data model additions:** `Part`, `StockMovement` (Id, ShopId, PartId, Kind, QuantityChange, QuantityAfter, UnitCost?, JobCardId?, Note, UserId?, UserName, AtUtc), `ExpenseCategory`, `Expense`, `CashClose` (Id, ShopId, Date, OpeningCash, Cash/Card/Transfer/WalletReceived, ExpensesTotal, CashExpenses, ExpectedCash, CountedCash, Difference, Note?, ClosedByUserId?, ClosedByName, ClosedAtUtc), `JobLine.PartId?` / `UnitCost?` / `AddedByUserId?`, `EstimateLine.PartId?`, `Shop.AllowNegativeStock`, and job history kinds `PartUsed` / `PartReturned`. Every new entity carries `ShopId` and the tenant filter.
