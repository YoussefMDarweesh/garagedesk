# Deploying GarageDesk to Azure (free)

The app runs on **Azure App Service, Free (F1) plan**, and uses the **same Neon database** you already use locally.
Every push to `main` runs `.github/workflows/deploy.yml`, which builds, tests and ships the app.

You do this setup once, by hand, in the Azure portal. Nothing here costs money on the Free plan.

## 0. What you need

- A Microsoft account and an Azure account (https://azure.microsoft.com/free). A card is asked for to prove you are a person; the Free (F1) plan does not charge it.
- Your Neon connection string (the one in your user-secrets). Treat it like a password.

## 1. Create the Web App

Azure portal -> **Create a resource** -> **Web App** -> **Create**.

| Field | Value |
|---|---|
| Resource group | **Create new**: `garagedesk-rg` |
| Name | something unique, e.g. `garagedesk-youssef`. It becomes `https://<name>.azurewebsites.net` |
| Publish | **Code** |
| Runtime stack | **.NET 10 (LTS)** |
| Operating System | **Linux** |
| Region | close to your Neon region (less delay between app and database). If the Free plan is greyed out, pick another region. |
| Pricing plan | **Free F1** (click *Explore pricing plans* if it shows something else) |

Open the **Deployment** tab and set:

- **Continuous deployment**: Disable (GitHub Actions does it, from our own workflow file).
- **Basic authentication**: **Enable** (the publish profile in step 5 needs it).

Click **Review + create**, then **Create**. Wait for "Your deployment is complete".

## 2. Give the app its connection string

Web App -> **Settings** -> **Environment variables** -> **App settings** -> **+ Add**. Add both:

| Name | Value |
|---|---|
| `ConnectionStrings__Default` | your Neon connection string (note the **two** underscores; ASP.NET turns `__` into `:`) |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` (so the app knows the visitor came in over HTTPS) |

Click **Apply**, then **Confirm**. The app restarts.

This is the Azure counterpart of `dotnet user-secrets`: the string lives in Azure, never in the repository.

## 3. Create the tables and the demo data (from your PC)

The deployed app and your PC share one Neon database, so you only do this once, from your machine.
Stop the dev server first if it is running.

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

## 4. Tell GitHub which app to deploy to

GitHub repo -> **Settings** -> **Secrets and variables** -> **Actions** -> **Variables** tab -> **New repository variable**:

- Name: `AZURE_WEBAPP_NAME`
- Value: the name you chose in step 1 (just the name, not the full address)

Until this variable exists the deploy job is skipped, so nothing goes red before you finish the setup.

## 5. Give GitHub permission to deploy (publish profile)

1. Azure portal -> your Web App -> **Overview** -> **Download publish profile** (top bar). A `.PublishSettings` file downloads.
2. If the button is greyed out: **Settings** -> **Configuration** -> **General settings** -> **SCM Basic Auth Publishing Credentials** = **On** -> **Save**, then try again.
3. Open the file in Notepad and copy **all** of it.
4. GitHub repo -> **Settings** -> **Secrets and variables** -> **Actions** -> **Secrets** tab -> **New repository secret**:
   - Name: `AZURE_WEBAPP_PUBLISH_PROFILE`
   - Secret: paste the whole file.
5. Delete the downloaded file from your PC. Never commit it.

## 6. Deploy

Merge to `main`, or GitHub -> **Actions** -> **Deploy to Azure** -> **Run workflow**.
When it is green, open `https://<name>.azurewebsites.net` and sign in with the demo login.

## What to expect on the Free plan

- The app **goes to sleep after about 20 minutes** without visitors. The next request takes 10 to 30 seconds. (Free has no "Always on".)
- Neon's free database also sleeps when idle, so the first page after a quiet period can be slow too.
- 60 CPU-minutes per day. Plenty for a demo.
- The demo login is public, so anyone can change the demo data. `--seed-demo` only fills an *empty* shop, so to start over you would first delete the demo shop's rows. A "reset demo" command is a good later addition.

## If something goes wrong

| Symptom | Likely cause |
|---|---|
| Workflow shows as skipped | `AZURE_WEBAPP_NAME` variable is missing (step 4) |
| Deploy step fails with a 401 / "publish profile" error | secret missing or incomplete, or Basic authentication is off (step 5) |
| Site shows "HTTP Error 500.30" or "Application Error" | open Web App -> **Monitoring** -> **Log stream**. Usually the connection string setting is missing or misspelled (step 2). |
| "password authentication failed" in the log | the Neon string was copied wrongly; copy it again from your user-secrets |
| Page loads but is empty / "relation does not exist" | the migrations were not applied to Neon (step 3) |
