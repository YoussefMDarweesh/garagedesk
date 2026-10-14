using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using GarageDesk.Web;
using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Local: dotnet user-secrets. Azure: the ConnectionStrings__Default environment variable.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' not found. Set it with: dotnet user-secrets set \"ConnectionStrings:Default\" \"<your Neon connection string>\" --project src/GarageDesk.Web");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// The shop of the signed-in user, read from their login cookie, used by the DbContext's query filters.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentShop, CurrentShop>();

// No email sender exists in the MVP, so accounts can't be email-confirmed: sign-in works right after registering.
builder.Services.AddDefaultIdentity<AppUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
    .AddErrorDescriber<LocalizedIdentityErrorDescriber>();

// Languages: English (default) and Arabic. All translations live in Resources/SharedResource.ar.resx.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
// By default ASP.NET writes anything outside basic Latin as &#x...; entities. Let Arabic through as real text.
builder.Services.AddWebEncoders(options =>
    options.TextEncoderSettings = new TextEncoderSettings(
        UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.Arabic));
builder.Services.AddSingleton<IValidationAttributeAdapterProvider, LocalizedValidationAttributeAdapterProvider>();
builder.Services.AddOptions<MvcOptions>()
    .Configure<IStringLocalizerFactory>((mvc, localizerFactory) =>
        mvc.ModelMetadataDetailsProviders.Add(
            new LocalizedDisplayNameProvider(localizerFactory.Create(typeof(SharedResource)))));

builder.Services.AddRazorPages()
    .AddMvcOptions(options => options.Filters.Add<RequireShopPageFilter>())
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, localizerFactory) => localizerFactory.Create(typeof(SharedResource)));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

// The language comes only from the cookie set by the navbar switcher; anything else falls back to English.
app.UseRequestLocalization(options =>
{
    options.SetDefaultCulture(Cultures.Default)
        .AddSupportedCultures(Cultures.Supported)
        .AddSupportedUICultures(Cultures.Supported);
    options.RequestCultureProviders = [new CookieRequestCultureProvider()];
});

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

// Makes the auto-generated Program class visible to the test project (WebApplicationFactory<Program>).
public partial class Program { }
