# Demo video script (about 2 minutes 45 seconds)

A storyboard for recording the GarageDesk walkthrough yourself. The click path uses the seeded
**Demo Motors** shop, so every number below can be reproduced.

**Goal of the video:** in under three minutes a viewer should see that this is a real, finished, tested
product: a shop owner's day from "customer walks in" to "invoice printed", in two languages.

## Before you record

1. **Seed a clean demo shop** (see the README, "Run it locally"):
   `dotnet run --project src/GarageDesk.Web -- --seed-demo`, then `dotnet run --project src/GarageDesk.Web`.
   Use a database where nobody has edited the demo shop yet, so the counts match this script
   (10 customers, 15 vehicles, 7 open job cards, 8 reminders on the bell).
2. **Sign in once** with the demo login and leave the browser on the dashboard in **English**, **light** mode.
3. **Use fake data only.** The seeded phone numbers are fake. For the new customer use `01000000001`.
   The WhatsApp scene opens `wa.me` with the customer's number, so never use a real customer's number.
4. **Clean the screen.**
   - Browser window 1920x1080 at 100% zoom (or 110% if the text looks small once uploaded).
   - Hide the bookmarks bar, extensions and any other tabs; use a clean or guest browser profile.
   - Turn on Do Not Disturb or Focus so no notifications pop up (Windows: Win+N, then Focus).
   - Turn on a cursor highlight so viewers can follow it (PowerToys "Mouse highlighter", or the recorder's option).
5. **Rehearse once** with the stopwatch. Narrate in a calm, slightly slow voice; the lines below are short on purpose.
6. **Record the screen first, voice second** if you prefer: it is easier to talk over a clean take.
   Tools: OBS Studio (free), or Xbox Game Bar (Win+G). Export 1080p, 30 fps MP4.

## Storyboard

| Time | Scene | What you do (exact clicks) | What you say |
|---|---|---|---|
| 0:00-0:10 | **Intro** | Show the sign-in page, type the demo login, **Sign in**. | "This is GarageDesk, a workshop manager for small car service shops in Egypt. Shops like these run on paper and WhatsApp, so I built one place for it all." |
| 0:10-0:30 | **Dashboard** | Land on the dashboard. Slowly move over the four number cards, then the activity chart (hover a month), then the **Reminders due** list. | "Everything here is the shop's own data: revenue this month, open job cards, customers, vehicles, a year of activity, and the reminders that need a call today." |
| 0:30-1:00 | **Add a customer and a car** | **Add customer** (top right) → Name `Sara Mahmoud`, Phone `01000000001` → **Save**. On her page click **Add their vehicle** → Plate `س ح ر 2024`, Make `Hyundai`, Model `Tucson`, Year `2022`, Mileage `45000` → **Save**. | "A new customer in a few seconds. Phone numbers are normalized to the Egyptian format, and plates can be typed in Arabic or English and are still found by search." |
| 1:00-1:20 | **Search** | In the top-bar search type `4821` and press Enter: the customer list narrows to Ahmed Hassan with his Skoda Octavia. | "Search finds a customer by name, by phone or by the plate of one of their cars, from anywhere." |
| 1:20-1:55 | **Job card** | On Sara's vehicle click **Open job card** → keep today's date, mileage `45300` → **Save**. Under the lines click **Add line**: Part, `Engine oil 5W-30 (4 L)`, qty `1`, price `650`; Part, `Oil filter`, qty `1`, price `120`; Labor, `Oil change`, qty `0.5`, price `150`. Point at the total (`845.00 EGP`). | "A job card is one visit. Add parts and labor and the total works itself out, rounded to the piastre." |
| 1:55-2:10 | **Status flow** | Click **Start work**, then **Mark as done**. A banner offers the next oil change: point at it (do not click yet), then click **Mark as paid** and confirm. Point at the lock message. | "Status moves one step at a time. When the job is done it offers the next oil change reminder, and once it's paid the job card is locked." |
| 2:10-2:30 | **Invoice** | Click **Invoice / Print**. Scroll the invoice, then **Print / Save as PDF** to show the browser's print preview (one A4 page, no menus). Cancel the dialog. | "One click gives a clean A4 invoice in EGP. Print it or save it as a PDF, no extra software." |
| 2:30-2:45 | **Reminders and WhatsApp** | Open **Reminders** in the rail. Hover an **Overdue** item and click **Send on WhatsApp**: a WhatsApp page opens with the customer's number and a message already written. Show it, then close the tab (do not send). | "Overdue services are listed with one tap to WhatsApp: the message is already written and can be edited before sending." |
| 2:45-3:00 | **Arabic and dark mode** | Click **العربية** in the top bar: the whole app flips right to left. Open the dashboard, then click the moon icon for dark mode. End on the Arabic dashboard. | "And it works in Arabic, right to left, with light and dark themes. The code and tests are on GitHub. Thanks for watching." |

Total: roughly 2 minutes 45 seconds to 3 minutes. If it runs long, cut the **Search** scene first.

## Tips for a good take
- **Slow down your mouse.** Pause for a beat after every click so viewers can read the result.
- **Do not read the screen aloud;** say what it means for the shop owner.
- **Avoid lingering on "revenue vs last month".** The seeded history is dated relative to the day you seed, so that
  percentage depends on the date and can look odd on the 1st-14th of a month. It is real data, just not a story.
- **Re-recording needs a fresh demo shop:** there is no "reset demo" command yet. Either seed a new Neon branch,
  or delete the customer you added (Sara) and her job card before the next take.
- **If you stumble,** keep going and cut the pause in editing instead of restarting.

## Where the video goes
1. Upload the finished MP4 to YouTube (**Unlisted** or Public) or Loom. Do **not** commit the video to the repository.
2. In `README.md`, replace this line near the top:

   ```markdown
   > **Demo video:** _coming soon (link goes here)_
   ```

   with:

   ```markdown
   > **Demo video:** [Watch the 3-minute walkthrough](https://YOUR-VIDEO-LINK-HERE)
   ```
3. Optional: put a screenshot of the video's first frame next to it using the dashboard image already in
   `docs/screenshots/`.
