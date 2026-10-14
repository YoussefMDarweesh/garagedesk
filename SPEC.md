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
- Job lines: type (Labor or Part), description, quantity, unit price. Totals calculated automatically.
- **Done when:** I can open a job for a car, add 2 parts + 1 labor line, and see the correct total.

### M4 — Service history
- Vehicle page shows every past job card, newest first, with totals.
- **Done when:** a car with 3 past visits shows all 3 with dates, mileage, and what was done.

### M5 — Reminders + WhatsApp
- Reminder: vehicle, type (e.g. oil change), due date and/or due mileage, done flag.
- Dashboard lists reminders due in the next 14 days or overdue.
- "Send on WhatsApp" button opens `https://wa.me/<phone>?text=<message>` with a pre-filled, editable message. No paid API.
- **Done when:** an overdue oil-change reminder appears on the dashboard and the button opens WhatsApp with the customer's number and message.

### M6 — Invoice
- Printable invoice page for a job card (shop details, customer, car, lines, total in EGP).
- User prints or saves as PDF from the browser. No PDF library needed.
- **Done when:** the invoice prints cleanly on one A4 page.

### M7 — Ship
- Deployed online, demo shop with seed data, README with screenshots and a short demo video.

### UI — Arabic and dark mode
- English is the default language. Arabic (right-to-left) is chosen with the navbar switcher; the choice is kept in a cookie and anything unsupported falls back to English.
- Light and dark themes: the theme follows the OS setting by default, and a navbar toggle overrides it and is remembered in the browser.
- **Done when:** switching to Arabic flips the pages to right-to-left with translated text and validation messages, and the theme can be switched (and survives a reload) without a flash.

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
- `Shop` (Id, Name, Phone, Address)
- `AppUser` (Identity user + ShopId)
- `Customer` (Id, ShopId, Name, Phone, Notes)
- `Vehicle` (Id, ShopId, CustomerId, Plate, Vin, Make, Model, Year, Mileage)
- `JobCard` (Id, ShopId, VehicleId, DateIn, MileageAtVisit, Status, Notes)
- `JobLine` (Id, JobCardId, Type, Description, Quantity, UnitPrice)
- `Reminder` (Id, ShopId, VehicleId, Type, DueDate?, DueMileage?, IsDone)
