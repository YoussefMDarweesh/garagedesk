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
- Deployed online, demo shop with seed data, README with screenshots and a short demo video.

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
| Hosting | Azure App Service Free (F1) | Free, native .NET, deploys from GitHub Actions |
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
