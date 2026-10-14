using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authorization;
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

// The biggest thing anyone uploads is a 512 KB logo, so cap request bodies well below Kestrel's 30 MB default.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2 * 1024 * 1024);

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
    .AddErrorDescriber<LocalizedIdentityErrorDescriber>()
    .AddSignInManager<GarageSignInManager>(); // a deactivated user cannot sign in

// Roles and policies (v1.3). The role is read from the database per request (ICurrentStaff), never trusted from the cookie.
builder.Services.AddScoped<ICurrentStaff, CurrentStaff>();
builder.Services.AddScoped<IAuthorizationHandler, ActiveMemberHandler>();
builder.Services.AddScoped<IAuthorizationHandler, ShopRoleHandler>();
builder.Services.AddAuthorization(Policies.Register);
builder.Services.ConfigureApplicationCookie(options =>
{
    // A signed-in user who may not open a page gets a real 403, not a redirect to a login or "not found" page.
    options.Events.OnRedirectToAccessDenied = context => ForbiddenResponse.WriteAsync(context.HttpContext);
});
// Re-check the login cookie against the database (security stamp) every minute instead of every 30 minutes, so a
// deactivated user's old cookie stops working quickly. (The policies above already refuse them on every request.)
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

// Languages: English (default) and Arabic. All translations live in Resources/SharedResource.ar.resx.
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
// By default ASP.NET writes anything outside basic Latin as &#x...; entities. Let Arabic through as real text.
builder.Services.AddWebEncoders(options =>
    options.TextEncoderSettings = new TextEncoderSettings(
        UnicodeRanges.BasicLatin, UnicodeRanges.Latin1Supplement, UnicodeRanges.Arabic));
builder.Services.AddSingleton<IValidationAttributeAdapterProvider, LocalizedValidationAttributeAdapterProvider>();
builder.Services.AddOptions<MvcOptions>()
    .Configure<IStringLocalizerFactory>((mvc, localizerFactory) =>
    {
        var localizer = localizerFactory.Create(typeof(SharedResource));
        mvc.ModelMetadataDetailsProviders.Add(new LocalizedDisplayNameProvider(localizer));

        // The messages for text that can't be read as a number, etc.
        var messages = mvc.ModelBindingMessageProvider;
        messages.SetValueMustBeANumberAccessor(field => localizer["The field {0} must be a number.", field].Value);
        messages.SetAttemptedValueIsInvalidAccessor((value, field) => localizer["The value '{0}' is not valid for {1}.", value, field].Value);
        messages.SetValueIsInvalidAccessor(value => localizer["The value '{0}' is invalid.", value].Value);
    });

// v1.2: secret public share links (/s/{token}), their rate limit and the owner-side link service.
builder.Services.AddShareLinks(builder.Configuration);

builder.Services.AddRazorPages()
    .AddRazorPagesOptions(options =>
    {
        // Everything under these folders needs a signed-in user (and, via RequireShopPageFilter, a shop).
        options.Conventions.AuthorizeFolder("/Customers");
        options.Conventions.AuthorizeFolder("/Vehicles");
        options.Conventions.AuthorizeFolder("/Jobs");
        options.Conventions.AuthorizeFolder("/Reminders");
        options.Conventions.AuthorizeFolder("/Receivables");
        options.Conventions.AuthorizeFolder("/Estimates");
    })
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

// Behind Render's proxy the container only ever speaks plain HTTP and the proxy does the TLS. Two things make that safe:
// ASPNETCORE_FORWARDEDHEADERS_ENABLED=true (set in the Dockerfile) makes ASP.NET trust X-Forwarded-Proto, so the app
// knows the visitor is on HTTPS; and with no HTTPS port bound in the container, UseHttpsRedirection has nowhere to
// redirect to, so it can't loop. Locally (the https launch profile) it still sends http to https.
app.UseHttpsRedirection();

// The language comes only from the cookie set by the navbar switcher; anything else falls back to English.
// Only the *words* switch to Arabic (UI culture). Numbers and dates keep the English format in every language:
// browsers always post decimals as "1.5", and Arabic's own separator would make ASP.NET reject that.
app.UseRequestLocalization(options =>
{
    options.SetDefaultCulture(Cultures.Default)
        .AddSupportedCultures(Cultures.Default)
        .AddSupportedUICultures(Cultures.Supported);
    options.RequestCultureProviders = [new CookieRequestCultureProvider()];
});

app.UseRouting();

app.UseShareLinkProtections();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapShopLogo();

// The platform's health check. It touches nothing (no database, no login): Neon may be asleep, and a sleeping
// database must not make the platform think the app is down.
app.MapGet("/healthz", (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Text("ok");
});

// "-- --seed-demo" creates the Demo Motors shop and exits; otherwise this is a normal web server.
if (args.Contains(DemoDataCommand.Flag))
{
    Environment.ExitCode = await DemoDataCommand.RunAsync(app.Services, app.Configuration, Console.Out);
}
else
{
    app.Run();
}

// Makes the auto-generated Program class visible to the test project (WebApplicationFactory<Program>).
public partial class Program { }
