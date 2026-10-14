using Microsoft.AspNetCore.Authorization;
using GarageDesk.Web.Data;
using GarageDesk.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace GarageDesk.Web.Pages.Receivables;

[Authorize(Policy = Policies.Staff)]
public class IndexModel(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer) : PageModel
{
    public List<CustomerBalance> Balances { get; private set; } = [];

    public decimal TotalOwed => Balances.Sum(b => b.Owed);

    public string? ShopName => User.FindFirst(CurrentShop.NameClaimType)?.Value;

    public async Task OnGetAsync() => Balances = await OutstandingBalances.LoadAsync(db);

    /// <summary>A WhatsApp link with the reminder pre-filled, in the shop's current language.</summary>
    public string? WhatsAppFor(CustomerBalance balance) =>
        WhatsAppLink.Build(balance.Customer.Phone,
            localizer["Hello {0}, this is {1}: your balance for {2} is {3} EGP. Please pay when convenient. Thank you!",
                balance.Customer.Name, ShopName ?? "GarageDesk", balance.Jobs == 1 ? localizer["Job cards"] + " (1)" : localizer["Job cards"] + $" ({balance.Jobs})",
                MoneyFormat.Amount(balance.Owed)]);
}
