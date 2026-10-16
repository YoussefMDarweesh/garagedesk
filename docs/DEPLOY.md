# Going live on MonsterASP.NET (free, no card)

> **Status: written before the first deploy.** The steps follow MonsterASP's own help pages (October 2026), but
> nothing here has been run against a real MonsterASP account yet. Labels in their control panel may differ a little.
> For a Docker host instead (Render and similar), see [DEPLOY-DOCKER.md](DEPLOY-DOCKER.md).

**Why this host:** the free plan needs no payment card, runs ASP.NET Core on .NET 10 the normal way (IIS, no Docker),
and gives a free `https://<name>.runasp.net` address. The database stays on **Neon**. Total cost: **0**.

**What the free plan gives you:** 1 website, 256 MB RAM, limited traffic, servers in Europe only, no email sending.
MonsterASP says the free plan is meant for testing and learning; a paid plan (about $2/month) removes the limits
when real shops depend on it.

> **Stop rules**
> - If any page asks for a payment card, **stop**. The free plan does not need one.
> - Until HTTPS works (step 6), **do not sign up real users or type real passwords** on the site: without HTTPS,
>   a password travels over the internet unencrypted.
> - The Neon connection string and the `.publishSettings` file are passwords. They never go into the repository,
>   a chat, or a screenshot.

## 0. What you need

- Your Neon account, and Visual Studio (the one you already build this project with).
- This repository with `main` up to date, and the `dotnet ef` tool (you already use it).
- **Two-factor sign-in turned on** for GitHub, Neon and (once you have it) MonsterASP. Anyone who gets into one of
  these accounts gets your customers' data.

## 1. A separate production database in Neon

Your PC's development data and the live site's real data must never be the same database.

1. Neon console -> your project. Check the **region** (e.g. `aws-eu-central-1`, Frankfurt).
   - **In Europe:** go to **Branches** -> **Create branch**, name it `production`, parent `main`. If Neon offers
     **schema only** (no data), choose it: production then starts empty, without your test shops.
   - **Not in Europe** (e.g. a US region): create a **new project** named `garagedesk-production` in a Frankfurt
     region instead. MonsterASP's servers are in Europe, and every page runs several queries, so a database on
     another continent would make every page slow. A region can't be changed later.
2. Open the new branch (or project) -> **Connect** -> copy the connection string in the .NET format. Keep
   `SSL Mode=Require` in it. Paste it into a password manager, nowhere else.

## 2. Create the tables in the production database (from your PC)

Stop the local dev server first. Then, in **PowerShell** at the repository root:

```powershell
$env:ConnectionStrings__Default = Read-Host "Production connection string"
dotnet ef database update --project src/GarageDesk.Web
Remove-Item Env:ConnectionStrings__Default
```

- `Read-Host` asks for the string, so it is **not saved** in PowerShell's history (typing it straight into a
  command would be).
- The environment variable wins over your user-secrets for this one window only, so the migrations go to
  production and not to your development database. The last line removes it again.
- All migrations run, including `AddDataProtectionKeys` (see "Why the keys table" below).

**Optional: a public demo shop.** Only if you want a demo login on the live site. Strangers can change a public
demo shop, but the tenant filter keeps them out of every other shop. Same window, before the `Remove-Item` line:
`dotnet run --project src/GarageDesk.Web -- --seed-demo` (it prints the demo password once).

## 3. Create the MonsterASP account and the website

1. Go to https://www.monsterasp.net and sign up for the **Free** plan (no card).
2. In the Control Panel: **Websites** -> create a website and choose a free subdomain, e.g.
   `garagedesk.runasp.net`.
3. Website -> **Scripting**:
   - ASP.NET / .NET: choose **modern .NET (ASP.NET Core)**, not .NET Framework. **Save**.
   - ASP.NET Core hosting model: **InProcess**.
4. Website -> **Scripting** -> **Environment variables**. Add these, then click **Restart application**:

   | Name | Value |
   |---|---|
   | `ConnectionStrings__Default` | the **production** connection string from step 1 (two underscores) |
   | `ASPNETCORE_ENVIRONMENT` | `Production` (never `Development`: that shows error details to visitors) |
   | `Landing__WhatsAppNumber` | optional, your WhatsApp number for the landing page button |

   These stay in MonsterASP's panel, outside the published files, so publishing again never overwrites or exposes them.

## 4. Publish from Visual Studio

1. Website -> **Deploy** -> **Visual Studio access**: switch **WebDeploy** to **Enabled**, then
   **Download publish profile**. Save the `.publishSettings` file **outside the repository** (e.g. in Documents).
   It holds the deploy password. Git ignores `*.publishSettings` anyway, as a safety net.
2. Visual Studio -> right-click **GarageDesk.Web** -> **Publish** -> **Import profile** -> pick that file.
3. Check the profile uses the **Release** configuration, then click **Publish**. The site restarts by itself.

## 5. Check that it works

1. `http://<name>.runasp.net/healthz` answers `ok`. This proves the app started. (It doesn't touch the database.)
2. `http://<name>.runasp.net/` shows the landing page, and **Register** opens. Don't submit a real account yet.
3. The first real database test is creating a test account with a throw-away password after HTTPS works (step 6).

If step 1 fails, see the table at the end.

## 6. Turn on HTTPS (support ticket)

On the free plan, HTTPS is turned on by MonsterASP's support, not by a button:

1. Control Panel -> **Support** -> new ticket: *"Please enable HTTPS (Let's Encrypt) for my free website
   `<name>.runasp.net`. The site is published and running."*
2. When they confirm, open `https://<name>.runasp.net/healthz`, and check the browser shows the padlock.
3. Then turn on the panel's **HTTPS redirect** (MonsterASP's advice: only after `https://` works).
4. Now sign up with a throw-away test account and create a customer. If that works, the app reaches Neon.
   You are live.

## Every later update

1. Merge the PR into `main` (CI green), then `git checkout main` and `git pull`.
2. **Backup first** (below), if the update has a migration.
3. If the PR added a migration: run step 2 again (migrations **before** the new code, never after).
4. Publish (step 4, the imported profile is remembered).
5. Check `/healthz` and sign in.

## Backups

Neon's free plan can restore the last **6 hours** only. That doesn't help if you notice a problem the next day. So:

- **Before every migration, and once a week:** Neon -> **Branches** -> **Create branch**, parent `production`,
  name it `backup-2026-10-23` (today's date). It takes a second and costs nothing extra until data changes.
- Keep the **last 3** backup branches and delete older ones. The free plan allows 10 branches per project.
- To restore: put the backup branch's connection string in `ConnectionStrings__Default` in MonsterASP and
  **Restart application**. The site then runs on the backup.
- A backup branch shares storage with its parent until the data changes, so it barely counts against the 1 GB limit.

## Why the keys table

Login cookies, form tokens and share-link tokens are signed with secret keys (ASP.NET Core "Data Protection").
Kept only on the server, those keys are lost on a restart, a redeploy or a recycle by IIS (which can happen often on
shared hosting). Everyone would be logged out, forms would fail with "Bad request", and existing share links would stop
opening. The keys are now stored in the database (`DataProtectionKeys` table), so they survive all of that. The table
is app-wide, not per shop, and the app reads it itself; it needs no attention.

## If something goes wrong

| Symptom | Likely cause |
|---|---|
| **HTTP Error 500.30**, "app failed to start" | Usually `ConnectionStrings__Default` is missing or misspelled (two underscores). Open the panel's **Logs** for the real error, fix the variable, **Restart application** |
| **HTTP Error 500.31** or "framework not found" | The website is set to .NET Framework or an older .NET. Scripting -> choose modern .NET (.NET 10), save |
| `/healthz` works, but pages that use data time out or show a connection error | MonsterASP may block outgoing connections to port 5432 (PostgreSQL). Ask support: *"Please allow outbound TCP 5432 to `*.neon.tech` for `<name>.runasp.net`."* If they can't, the free plan won't work with Neon |
| `relation "..." does not exist` | The migrations didn't run against the **production** database. Repeat step 2 |
| `password authentication failed` / SSL errors | The connection string was copied wrongly. Copy it again from Neon, keep `SSL Mode=Require` |
| Everyone gets logged out after a publish | The `DataProtectionKeys` table is missing (step 2 not run after this update) |
| First page after a quiet hour is slow | Normal: IIS and Neon both sleep when idle. The second page is fast |
| Site stops answering mid-month | The free plan's traffic limit. Check the panel; a paid plan removes it |
| Any page asks for a payment card | Stop. The free plan doesn't need one |
