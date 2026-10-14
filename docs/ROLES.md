# Team, roles and commissions (v1.3)

GarageDesk shops have three roles. A person has exactly one role in exactly one shop; the shop is still the tenant boundary.

| Page or action | Owner | Cashier | Technician |
|---|---|---|---|
| Dashboard (numbers, reminders) | yes (with revenue and chart) | yes (no revenue, no chart) | no, goes to My jobs |
| Customers, vehicles, reminders | yes | yes | no |
| Job cards with prices, payments, invoice, discounts | yes | yes | no |
| Estimates, who owes me | yes | yes | no |
| My jobs (assigned job cards, status, parts and labor notes, vehicle history) | yes | yes | yes, their own only |
| Shop settings (logo, accent, VAT) | yes | no | no |
| My team (invite, roles, deactivate, commission percent) | yes | no | no |
| Commissions report, job card History panel | yes | no | no |

## How it is enforced

* **Policies.** Every Razor Page declares `[Authorize(Policy = ...)]` (`Owner`, `Staff` = owner or cashier, `Team` = anyone, `SignedIn`) or is on the explicit anonymous allow-list in `RolesAndPoliciesTests`. A test enumerates all pages and fails when one has neither.
* **The role is read from the database on every request** (`ICurrentStaff`), not trusted from the login cookie. Changing someone's role or deactivating them takes effect on their next request; their security stamp is also rotated so old cookies stop validating (the check interval is one minute).
* **Forbidden is a 403** with a small page. A technician asking for a colleague's job also gets 403; a job or vehicle of another shop is 404 (the tenant query filter).
* **Technicians have their own pages** (`/Work`): they are built field by field without price, total, payment or phone data, so there is nothing to hide with CSS. The priced pages (`/Jobs`, `/Vehicles`, ...) are refused by policy. A paid job card is shown to them as Done.
* **The last active owner** cannot be demoted or deactivated (checked inside a transaction that locks the shop row, so two owners cannot do it at the same time).
* **Invitations** are 256-bit random tokens in a link; only the SHA-256 hash is stored. They work once, expire after 7 days, can be cancelled, and unknown, expired, used and cancelled links all give the same 404. The role and shop come from the invitation, never from the form.
* **Assignment** accepts only an active technician or owner of the same shop; the check is on the server.

## Commission rule

For each job card that is **Paid**, assigned to the person, and whose visit date is in the month: add up its **labor** lines (quantity x unit price, rounded to the piastre per line, before discount and VAT). The commission is that total times the person's commission percent, rounded to the piastre. Parts never earn commission. The percent used is the person's current percent.

## Demo accounts

`dotnet run --project src/GarageDesk.Web -- --seed-demo` creates `cashier@garagedesk.app` and `technician@garagedesk.app` in Demo Motors. Passwords: `Demo:CashierPassword` / `Demo:TechnicianPassword`, or random and printed once.
