using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
    .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>();

builder.Services.AddRazorPages()
    .AddMvcOptions(options => options.Filters.Add<RequireShopPageFilter>());

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

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

// Makes the auto-generated Program class visible to the test project (WebApplicationFactory<Program>).
public partial class Program { }
