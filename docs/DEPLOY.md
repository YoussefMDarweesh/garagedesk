# Deploying GarageDesk with Docker (guide written for Render)

> **Status: not deployed.** No host has been chosen yet, so GarageDesk is not running anywhere online and there is
> no live URL. This guide is written so that deploying is quick **when you decide to**. Nothing in it has been
> run against a real Render account, so treat the dashboard labels as a guide, not a guarantee.

The app is packaged as a **Docker container** (see the `Dockerfile`) and uses a **Neon database**.
On Render's free web service, Render builds the container from this repository and redeploys on every push to
`main`, so no deploy secrets live in GitHub: Render pulls the code itself.

You would do this setup once, by hand.

> **Stop rule:** a free instance should not need payment details. If any host (Render or another) asks you for a
> card or payment details, **stop**: close the page and do not enter anything. Being card-free is a hard
> requirement for this project; a host that needs a card is simply not an option.

## 0. What you need

- A GitHub account (you have one) and this repository.
- Your Neon connection string (the one in your user-secrets). Treat it like a password: type or paste it only into Render's dashboard, never into the repository or a chat.

## 1. Create the Render account

1. Go to https://render.com and click **Get Started**.
2. Choose **GitHub** to sign up. Allow Render to read your repositories. When GitHub asks which repositories, choose **Only select repositories** and pick `garagedesk`.

## 2. Create the web service

Render dashboard -> **New +** -> **Web Service** -> **Build and deploy from a Git repository** -> select `garagedesk` -> **Connect**.

| Field | Value |
|---|---|
| Name | something unique, e.g. `garagedesk-youssef`. It becomes `https://<name>.onrender.com` |
| Language / Runtime | **Docker** (Render finds the `Dockerfile` at the repo root by itself) |
| Branch | `main` |
| Region | the one closest to your Neon database (check the region in the Neon dashboard) |
| Instance Type | **Free** |

Leave "Root Directory", "Dockerfile Path" and the commands empty.

## 3. Environment variables

On the same page open **Environment Variables** (or later: the service -> **Environment**) and add:

| Key | Value |
|---|---|
| `ConnectionStrings__Default` | your Neon connection string (note the **two** underscores; ASP.NET turns `__` into `:`) |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` (so the app knows the visitor is on HTTPS; the Docker image already sets it, this keeps it explicit) |

This is the Render counterpart of `dotnet user-secrets`: the string lives in Render, never in the repository and never inside the Docker image.

## 4. Health check and auto-deploy

Open **Settings** of the service and check:

- **Health Check Path**: `/healthz`. It answers `ok` without using the database, so a sleeping Neon database never makes Render think the app is down.
- **Auto-Deploy**: **On Commit** (this is the default). Every push to `main` redeploys.

Then click **Create Web Service** (or **Save Changes**). The first build takes a few minutes; watch the **Logs** tab until you see the service is **Live**.

## 5. Create the tables and the demo data (from your PC)

The deployed app and your PC share one Neon database, so you only do this once, from your machine.
Stop the local dev server first if it is running.

```bash
dotnet ef database update --project src/GarageDesk.Web
dotnet run --project src/GarageDesk.Web -- --seed-demo
```

The second command creates the **Demo Motors** shop (10 customers, 15 cars, a year of job history and due reminders),
then exits without starting the website. It prints the demo login and a random password **once**. Save it.

To choose the login and password yourself, set them before running it:

```bash
dotnet user-secrets set "Demo:Email" "demo@garagedesk.app" --project src/GarageDesk.Web
dotnet user-secrets set "Demo:Password" "<a password you choose>" --project src/GarageDesk.Web
```

Running `--seed-demo` again is harmless: a shop that already has data is left alone, and an existing password is never changed.

Now open `https://<name>.onrender.com` and sign in with the demo login.

## Optional tip: a separate Neon branch for the public demo

The demo login is meant to be shared, so strangers can change what is in the demo shop.
To keep that away from your own data, give the public site its own copy of the database:

1. Neon dashboard -> your project -> **Branches** -> **Create branch** (e.g. `demo`). It is free and copies the schema.
2. Copy the connection string **of that branch** and use it as `ConnectionStrings__Default` in Render (step 3).
3. Run step 5 once against that branch: temporarily set it with
   `dotnet user-secrets set "ConnectionStrings:Default" "<the demo branch string>" --project src/GarageDesk.Web`,
   run the two commands, then set your own development string back.

Your daily work then stays in your main branch and the public demo lives in its own.

## What to expect on the free plan

- The service **goes to sleep after about 15 minutes** without visitors. The next request wakes it and takes **about a minute** (a "cold start"). Open the link before you show it to someone.
- Neon's free database also sleeps when idle, so the first page after a quiet period can be slow too.
- The container's disk is **ephemeral** (wiped on every restart and deploy). That is fine here: everything, including shop logos, lives in the database.
- Free instances have a monthly hours allowance and limited CPU and memory. Plenty for a demo.
- The demo login is public, so anyone can change the demo data. `--seed-demo` only fills an *empty* shop, so to start over you would first delete the demo shop's rows (or recreate the Neon branch). A "reset demo" command is a good later addition.

## If something goes wrong

| Symptom | Likely cause |
|---|---|
| Build fails in the Render logs | read the last lines of the build log. The same `docker build .` runs in GitHub Actions on every push, so look at the CI run's "docker-build" job first |
| Deploy "failed" / service restarts in a loop, logs show `Connection string 'Default' not found` | the `ConnectionStrings__Default` variable is missing or misspelled (two underscores, exact capitals) |
| Logs show `password authentication failed` or `SSL` errors | the Neon string was copied wrongly; copy it again from your user-secrets. Keep `SSL Mode=Require` in it |
| Site opens but pages show an error / `relation "..." does not exist` | the migrations were not applied to that Neon database (step 5) |
| "Application failed to respond" or "No open ports detected" | the container did not start. Check the Logs tab for the real error, usually one of the two rows above |
| Redirect loop / "too many redirects" | `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is not `true` |
| First load takes a minute, later loads are fast | normal: the free instance was asleep |
| Render asks for a credit card or payment details | stop and do not enter anything. You should not need them for the free instance |
