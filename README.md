# GarageDesk

**A simple workshop manager for small car service shops** — customers, cars, job cards, service history, WhatsApp reminders, and printable invoices. Built with ASP.NET Core.

> 🚧 **Status:** in active development. Follow the roadmap below.

## Why
Small car service shops in Egypt run on paper, WhatsApp chats, and Excel. Service history gets lost, customers don't get reminded about their next oil change, and invoices are handwritten. GarageDesk gives a shop owner one place for all of it, free and simple.

I run an automotive software business and manage a fleet of cars, so I've been on both sides of the counter. This project is my answer to the problems I see every week.

## Features (MVP)
- **Shop accounts:** each shop is its own tenant; data never crosses shops
- **Customers & vehicles:** search by name, phone, or plate
- **Job cards:** parts and labor lines with automatic totals, status from Booked to Paid
- **Service history:** every visit for a car, newest first
- **Reminders:** due and overdue services on the dashboard, one-tap WhatsApp message
- **Invoices:** clean A4 print or save as PDF, in EGP

## Roadmap
- [ ] M0 — Project skeleton, CI
- [ ] M1 — Shop accounts & multi-tenancy
- [ ] M2 — Customers & vehicles
- [ ] M3 — Job cards
- [ ] M4 — Service history
- [ ] M5 — Reminders + WhatsApp
- [ ] M6 — Printable invoice
- [ ] M7 — Demo data & deployment
- [ ] M8 — Screenshots, demo video, final README

Full scope and acceptance criteria: [SPEC.md](SPEC.md)

## Tech stack
| Area | Choice |
|---|---|
| Framework | ASP.NET Core (.NET 10), Razor Pages |
| Data | EF Core + PostgreSQL (Neon) |
| Auth | ASP.NET Core Identity |
| UI | Bootstrap 5 |
| Tests | xUnit + SQLite in-memory |
| CI/CD | GitHub Actions → Azure App Service |

## How it's built
This project is built with AI coding agents under my direction: **Claude Code** implements each milestone from [SPEC.md](SPEC.md), **Codex** reviews every pull request against [AGENTS.md](AGENTS.md), and I scope, test, and merge. The rules both agents follow are in [CLAUDE.md](CLAUDE.md).

## Run locally
_Coming in M0._

## License
[MIT](LICENSE)
