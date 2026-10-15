using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GarageDesk.Web.Services;

/// <summary>
/// Runs for every Razor Page. A signed-in user without a shop is sent to /CreateShop
/// before they can use anything else. Anonymous visitors are not affected.
/// </summary>
public class RequireShopPageFilter(ICurrentShop currentShop) : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var page = context.ActionDescriptor.ViewEnginePath;
        var isExempt = context.ActionDescriptor.AreaName == "Identity" // login, register, logout, manage account
                       || page == "/CreateShop"
                       || page == "/SetCulture" // language switcher must work before a shop exists
                       || page == "/Error"
                       || page.StartsWith("/Share/", StringComparison.Ordinal); // the customer's public pages

        var needsShop = !isExempt
                        && context.HttpContext.User.Identity?.IsAuthenticated == true
                        && currentShop.ShopId is null;

        if (needsShop)
        {
            context.Result = new RedirectToPageResult("/CreateShop");
            return;
        }

        await next();
    }
}
