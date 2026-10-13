# Build prompts — paste these into Claude Code, one at a time

Workflow for every step:
1. Open Claude Code in the `garagedesk` folder and paste the prompt.
2. Run the app (`dotnet run --project src/GarageDesk.Web`) and click through it yourself. If something's off, tell Claude what you saw.
3. Ask Claude to commit, push the branch, and open a pull request.
4. Ask Codex: *"Review this PR using AGENTS.md."* Paste its findings back to Claude Code to fix.
5. Merge the PR on GitHub.
6. Read Claude's explanation. If you can't explain the change to someone else, ask it to walk you through it.

Tick off the milestone in the README's roadmap when it's merged.

---

## Setup (one time, all free)
- Install the .NET 10 SDK, Git, and VS Code (with the C# Dev Kit extension) or Visual Studio Community.
- Clone the repo: `git clone https://github.com/YoussefMDarweesh/garagedesk.git`
- Create a free Neon project and copy its connection string (you'll set it in M0).

## M0 — Skeleton
> Read SPEC.md and CLAUDE.md. Create the solution: an ASP.NET Core .NET 10 Razor Pages project at src/GarageDesk.Web with Individual Accounts (Identity), switched from SQLite/SQL Server to PostgreSQL with Npgsql, and an xUnit project at tests/GarageDesk.Tests. Add a solution file GarageDesk.sln at the repo root. Read the connection string from user-secrets under "ConnectionStrings:Default" and tell me the exact command to set it. Tests must use SQLite in-memory, never the real database. Add one smoke test that the home page returns 200. Create the first EF migration. Add a GitHub Actions workflow at .github/workflows/ci.yml that runs build and test on every push and pull request. Work on branch m0-skeleton, make sure it builds and the tests pass, then commit, push, and open a pull request.

## M1 — Shop account
> Build milestone M1 from SPEC.md. After registration, the user must create their shop before using anything else. Add ShopId to the user, and set up the global query filter pattern for shop-owned entities so later entities can use it. Add a test that proves one shop can't read another shop's data.

## M2 — Customers & vehicles
> Build milestone M2 from SPEC.md: Customer and Vehicle entities, list/create/edit/details pages, and search by name, phone, or plate. Normalize Egyptian phone numbers to +20 format. Add tests for phone normalization and tenant isolation.

## M3 — Job cards
> Build milestone M3 from SPEC.md: JobCard and JobLine, the status flow, and automatic totals. A job card is opened from a vehicle's page. Add tests for total calculation and status transitions.

## M4 — Service history
> Build milestone M4 from SPEC.md: show the vehicle's full job history on its details page, newest first. Updating a job's mileage should update the vehicle's current mileage if it's higher.

## M5 — Reminders + WhatsApp
> Build milestone M5 from SPEC.md: Reminder entity, a dashboard home page listing due/overdue reminders, and a "Send on WhatsApp" button that opens a wa.me link with a pre-filled, URL-encoded message. When a job card is marked Done, offer to create the next oil-change reminder.

## M6 — Invoice
> Build milestone M6 from SPEC.md: a printable invoice page for a job card using print-friendly CSS. It must fit on one A4 page and hide the navigation when printed.

## M7 — Seed data + deploy
> Add a seed-data option that creates a demo shop "Demo Motors" with 10 customers, 15 cars (mix of Skoda, VW, Hyundai, Kia, Toyota), job history, and some due reminders. Then write a GitHub Actions workflow that deploys to Azure App Service, and give me step-by-step instructions for creating a Free (F1) App Service and setting the connection string as an app setting.

## M8 — README
> Write README.md: problem statement, features, screenshots section (placeholders I'll fill), tech stack, architecture diagram in Mermaid, multi-tenancy explanation, how to run locally, and a "What I'd build next" section.
