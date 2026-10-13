# Instructions for AI coding agents (Claude Code, Codex)

Read `SPEC.md` first. It is the source of truth for what to build.

## About the developer
I'm a backend developer (PHP, C#, MySQL) learning to build a SaaS. I want to understand everything you write. At the end of each task, explain in plain English what you changed and why, in 5–10 lines.

## Stack (do not change without asking)
- ASP.NET Core, .NET 10, Razor Pages, single web project + one test project
- EF Core with Npgsql (PostgreSQL on Neon)
- ASP.NET Core Identity for login
- Bootstrap 5, no JavaScript framework, no frontend build step
- xUnit for tests, with EF Core SQLite in-memory so tests and CI never need a real database

Pre-approved NuGet packages (no need to ask): `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.EntityFrameworkCore.Tools`, the ASP.NET Core Identity packages from the template, `Microsoft.EntityFrameworkCore.Sqlite` (test project only), `Microsoft.AspNetCore.Mvc.Testing` (test project only).

## Project layout
```
/src/GarageDesk.Web        Razor Pages app (Pages/, Data/, Models/, Services/)
/tests/GarageDesk.Tests    xUnit tests
SPEC.md  CLAUDE.md  AGENTS.md  README.md
```

## Rules
1. **One milestone at a time.** Only build what the current prompt asks for. Don't start the next feature.
2. **Tenant safety.** Every shop-owned entity has `ShopId`. Use an EF Core global query filter on the current user's ShopId. Never return data across shops. Add a test for this when adding a new entity.
3. **No secrets in code.** The connection string comes from `dotnet user-secrets` locally and from environment variables in Azure. Never commit it.
4. **Migrations.** Use EF Core migrations (`dotnet ef migrations add <Name>`). Never edit an applied migration.
5. **Ask before adding NuGet packages.** Prefer what's built in.
6. **Keep it simple.** No repository pattern, no CQRS, no microservices. DbContext directly in page models is fine for this size.
7. **Always finish green.** Run `dotnet build` and `dotnet test` before saying you're done. Fix failures.
8. **Branch per milestone.** Work on a branch named after the milestone (e.g. `m2-customers-vehicles`), never directly on `main`. Commit in small steps with Conventional Commits messages (`feat: add vehicle search`), then open a pull request to `main`.
9. **Don't commit generated junk.** No `bin/`, `obj/`, `.vs/`, or local database files.

## Commands
```
dotnet build
dotnet test
dotnet run --project src/GarageDesk.Web
dotnet ef migrations add <Name> --project src/GarageDesk.Web
dotnet ef database update --project src/GarageDesk.Web
```
