using System.Net;
using System.Text.RegularExpressions;
using GarageDesk.Web.Models;
using GarageDesk.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

/// <summary>Growth: the setup wizard and the dashboard checklist that is derived from real data.</summary>
public partial class OnboardingTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    // ---- where a new owner lands ----

    [Fact]
    public async Task Creating_the_shop_sends_the_owner_to_the_wizard()
    {
        var userId = await TestData.CreateUserWithoutShopAsync(factory);
        var client = factory.CreateClientFor(userId);

        var response = await TestData.SubmitFormAsync(client, "/CreateShop",
            ("Input.Name", "Wizard Garage"), ("Input.Phone", "01012345678"), ("Input.Address", "Cairo"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Onboarding", response.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData("/Onboarding", 200, 403, 403)]
    [InlineData("/Onboarding?step=1", 200, 403, 403)]
    [InlineData("/Onboarding?step=2", 200, 403, 403)]
    [InlineData("/Onboarding?step=4", 200, 403, 403)]
    public async Task The_wizard_is_for_owners_only_and_forbidden_is_a_403(string path, int owner, int cashier, int technician)
    {
        var (ownerId, shop) = await TestData.CreateOwnerAsync(factory, "Wizard Roles");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);
        var technicianId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Technician);

        Assert.Equal((HttpStatusCode)owner, (await factory.CreateClientFor(ownerId, shop).GetAsync(path)).StatusCode);
        Assert.Equal((HttpStatusCode)cashier, (await factory.CreateClientFor(cashierId, shop).GetAsync(path)).StatusCode);
        Assert.Equal((HttpStatusCode)technician, (await factory.CreateClientFor(technicianId, shop).GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_visitors_cannot_open_the_wizard()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Onboarding");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_wizard_opens_on_the_first_step_that_still_has_work()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Next Step Garage");
        var client = factory.CreateClientFor(userId, shop);

        Assert.Contains("name=\"upload\"", await TestData.BodyAsync(await client.GetAsync("/Onboarding")));
        Assert.Contains("name=\"Input.Plate\"", await TestData.BodyAsync(await client.GetAsync("/Onboarding?step=2")));

        // A link that jumps to the job step before there is a car lands on the car step instead.
        var early = await TestData.BodyAsync(await client.GetAsync("/Onboarding?step=3"));
        Assert.Contains("name=\"Input.Plate\"", early);

        await TestData.SeedJobAsync(factory, shop, JobStatus.Booked);
        Assert.Contains("Your shop is ready", await TestData.BodyAsync(await client.GetAsync("/Onboarding")));
    }

    // ---- step 1: brand ----

    [Fact]
    public async Task Step_one_saves_the_accent_the_vat_and_the_logo()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Brand Garage");
        var client = factory.CreateClientFor(userId, shop);

        var response = await PostBrandAsync(client, accent: "teal", vat: "14", logo: Png);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("step=2", response.Headers.Location!.ToString());
        using var db = factory.CreateContextFor(shop);
        var stored = await db.Shops.SingleAsync();
        Assert.Equal("teal", stored.AccentColor);
        Assert.Equal(14m, stored.VatPercent);
        Assert.Equal(Png, (await db.ShopLogos.SingleAsync()).Bytes);
    }

    [Fact]
    public async Task Step_one_refuses_a_file_that_is_not_an_image_and_a_vat_out_of_range()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Bad Brand Garage");
        var client = factory.CreateClientFor(userId, shop);

        var notAnImage = await PostBrandAsync(client, accent: "blue", vat: "", logo: "<svg onload=alert(1)>"u8.ToArray(), fileName: "logo.svg", fileType: "image/svg+xml");
        Assert.Equal(HttpStatusCode.OK, notAnImage.StatusCode);
        Assert.Contains("Only PNG, JPEG or WebP", await TestData.BodyAsync(notAnImage));

        var badVat = await PostBrandAsync(client, accent: "blue", vat: "150", logo: null);
        Assert.Equal(HttpStatusCode.OK, badVat.StatusCode);

        using var db = factory.CreateContextFor(shop);
        var stored = await db.Shops.SingleAsync();
        Assert.Null(stored.AccentColor);
        Assert.Null(stored.VatPercent);
        Assert.Empty(await db.ShopLogos.ToListAsync());
    }

    [Fact]
    public async Task An_unknown_accent_name_is_refused()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Odd Accent Garage");
        var client = factory.CreateClientFor(userId, shop);

        var response = await PostBrandAsync(client, accent: "\"><script>", vat: "", logo: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var db = factory.CreateContextFor(shop);
        Assert.Null((await db.Shops.SingleAsync()).AccentColor);
    }

    // ---- step 2: first customer and car ----

    [Fact]
    public async Task Step_two_creates_the_customer_and_the_car_in_this_shop_only()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "First Car Garage");
        var (_, otherShop) = await TestData.CreateOwnerAsync(factory, "Other Garage");
        var client = factory.CreateClientFor(userId, shop);

        var response = await PostCarAsync(client, name: "Mona Samir", phone: "0101 234 5678", plate: "أ ب ج 1234");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("step=3", response.Headers.Location!.ToString());

        using var db = factory.CreateContextFor(shop);
        var customer = await db.Customers.Include(c => c.Vehicles).SingleAsync();
        Assert.Equal("+201012345678", customer.Phone);
        var vehicle = Assert.Single(customer.Vehicles);
        Assert.Equal("ابج1234", vehicle.PlateKey);
        Assert.Equal(shop, vehicle.ShopId);

        using var other = factory.CreateContextFor(otherShop);
        Assert.Empty(await other.Customers.ToListAsync());
        Assert.Empty(await other.Vehicles.ToListAsync());
    }

    [Fact]
    public async Task Posting_step_two_twice_does_not_duplicate_the_person_or_the_car()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Double Click Garage");
        var client = factory.CreateClientFor(userId, shop);

        await PostCarAsync(client, plate: "XYZ 111");
        var again = await PostCarAsync(client, plate: "XYZ 111");

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        using var db = factory.CreateContextFor(shop);
        Assert.Single(await db.Customers.ToListAsync());
        Assert.Single(await db.Vehicles.ToListAsync());
    }

    [Fact]
    public async Task A_second_car_for_the_same_phone_reuses_the_customer()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Two Cars Garage");
        var client = factory.CreateClientFor(userId, shop);

        await PostCarAsync(client, plate: "AAA 111");
        await PostCarAsync(client, name: "Same Person", plate: "BBB 222");

        using var db = factory.CreateContextFor(shop);
        Assert.Single(await db.Customers.ToListAsync());
        Assert.Equal(2, await db.Vehicles.CountAsync());
    }

    [Fact]
    public async Task A_plate_that_belongs_to_someone_else_is_refused_and_nothing_is_created()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Plate Clash Garage");
        await TestData.SeedJobAsync(factory, shop, plate: "CLASH 1", phone: "+201000000001");
        var client = factory.CreateClientFor(userId, shop);

        var response = await PostCarAsync(client, name: "New Person", phone: "01099999999", plate: "clash-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already exists in your shop", await TestData.BodyAsync(response));
        using var db = factory.CreateContextFor(shop);
        Assert.Single(await db.Customers.ToListAsync());
    }

    [Fact]
    public async Task A_bad_phone_shows_the_step_again_and_creates_nothing()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Bad Phone Garage");
        var client = factory.CreateClientFor(userId, shop);

        var response = await PostCarAsync(client, phone: "12345");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter a valid Egyptian phone number", await TestData.BodyAsync(response));
        using var db = factory.CreateContextFor(shop);
        Assert.Empty(await db.Customers.ToListAsync());
        Assert.Empty(await db.Vehicles.ToListAsync());
    }

    // ---- step 3: first job card ----

    [Fact]
    public async Task Step_three_opens_a_booked_job_card_for_the_car_with_the_shops_vat()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "First Job Garage");
        var client = factory.CreateClientFor(userId, shop);
        await PostBrandAsync(client, accent: "orange", vat: "14", logo: null);
        await PostCarAsync(client, plate: "JOB 100", mileage: "45000");
        int vehicleId;
        using (var db = factory.CreateContextFor(shop))
        {
            vehicleId = (await db.Vehicles.SingleAsync()).Id;
        }

        var response = await PostJobAsync(client, vehicleId, "Oil change and a noise from the front");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Jobs/Details/", response.Headers.Location!.ToString());
        using var check = factory.CreateContextFor(shop);
        var job = await check.JobCards.SingleAsync();
        Assert.Equal(vehicleId, job.VehicleId);
        Assert.Equal(JobStatus.Booked, job.Status);
        Assert.Equal(45000, job.MileageAtVisit);
        Assert.Equal(14m, job.VatPercent);
        Assert.Equal("Oil change and a noise from the front", job.Notes);
        Assert.Contains(await check.JobAuditEntries.ToListAsync(), a => a.Kind == JobAuditKind.Created);
    }

    [Fact]
    public async Task Step_three_cannot_use_another_shops_vehicle()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Mine Garage");
        var (_, otherShop) = await TestData.CreateOwnerAsync(factory, "Theirs Garage");
        await TestData.SeedJobAsync(factory, otherShop, plate: "THEIRS 1");
        int foreignVehicle;
        using (var theirs = factory.CreateContextFor(otherShop))
        {
            foreignVehicle = (await theirs.Vehicles.SingleAsync()).Id;
        }
        var client = factory.CreateClientFor(userId, shop);

        var response = await PostJobAsync(client, foreignVehicle, "sneaky");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var mine = factory.CreateContextFor(shop);
        Assert.Empty(await mine.JobCards.ToListAsync());
        using var theirsAfter = factory.CreateContextFor(otherShop);
        Assert.Single(await theirsAfter.JobCards.ToListAsync());
    }

    // ---- the dashboard checklist ----

    [Fact]
    public async Task The_checklist_is_derived_from_real_data_and_survives_a_refresh()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Checklist Garage");
        var client = factory.CreateClientFor(userId, shop);

        var empty = await TestData.BodyAsync(await client.GetAsync("/"));
        Assert.Contains("data-setup-checklist", empty);
        Assert.Equal(["false", "false", "false", "false", "false"], ItemStates(empty));

        await PostCarAsync(client, plate: "LIST 1");

        var afterCar = await TestData.BodyAsync(await client.GetAsync("/"));
        Assert.Equal(["false", "false", "true", "true", "false"], ItemStates(afterCar));
        // A refresh shows exactly the same: nothing is kept in the session, a cookie or a flag.
        Assert.Equal(ItemStates(afterCar), ItemStates(await TestData.BodyAsync(await client.GetAsync("/"))));

        await PostBrandAsync(client, accent: "green", vat: "14", logo: null);
        var branded = await TestData.BodyAsync(await client.GetAsync("/"));
        Assert.Equal(["true", "true", "true", "true", "false"], ItemStates(branded));
    }

    [Fact]
    public async Task The_checklist_goes_away_once_a_customer_a_car_and_a_job_exist()
    {
        var (userId, shop) = await TestData.CreateOwnerAsync(factory, "Done Garage");
        await TestData.SeedJobAsync(factory, shop);
        var client = factory.CreateClientFor(userId, shop);

        var home = await TestData.BodyAsync(await client.GetAsync("/"));

        Assert.DoesNotContain("data-setup-checklist", home);
    }

    [Fact]
    public async Task A_cashier_sees_only_the_steps_they_can_do()
    {
        var (_, shop) = await TestData.CreateOwnerAsync(factory, "Cashier Checklist");
        var cashierId = await TestData.CreateStaffAsync(factory, shop, ShopRole.Cashier);

        var home = await TestData.BodyAsync(await factory.CreateClientFor(cashierId, shop).GetAsync("/"));

        Assert.Equal(["false", "false", "false"], ItemStates(home));
        Assert.DoesNotContain("href=\"/Onboarding", home);
        Assert.DoesNotContain("/ShopSettings\"", home.Split("data-setup-checklist")[1].Split("</section>")[0]);
    }

    [Fact]
    public async Task One_shops_data_never_ticks_another_shops_checklist()
    {
        var (_, busy) = await TestData.CreateOwnerAsync(factory, "Busy Garage");
        await TestData.SeedJobAsync(factory, busy);
        var (newOwner, fresh) = await TestData.CreateOwnerAsync(factory, "Fresh Garage");

        using var db = factory.CreateContextFor(fresh);
        var progress = await SetupProgress.LoadAsync(db);

        Assert.False(progress.HasCustomer);
        Assert.False(progress.HasVehicle);
        Assert.False(progress.HasJob);
        Assert.Equal(1, progress.NextWizardStep);
        Assert.Contains("data-setup-checklist", await TestData.BodyAsync(await factory.CreateClientFor(newOwner, fresh).GetAsync("/")));
    }

    [Fact]
    public void The_next_wizard_step_follows_the_data()
    {
        Assert.Equal(1, new SetupProgress(false, false, false, false, false, false).NextWizardStep);
        Assert.Equal(2, new SetupProgress(true, false, false, false, false, false).NextWizardStep);
        Assert.Equal(2, new SetupProgress(false, false, true, false, false, false).NextWizardStep);
        Assert.Equal(2, new SetupProgress(false, false, false, true, false, false).NextWizardStep);
        Assert.Equal(3, new SetupProgress(false, false, false, true, true, false).NextWizardStep);
        Assert.Equal(4, new SetupProgress(false, false, false, true, true, true).NextWizardStep);
    }

    // ---- helpers ----

    private static List<string> ItemStates(string html) =>
        ChecklistItem().Matches(html).Select(m => m.Groups[1].Value).ToList();

    [GeneratedRegex("data-setup-item=\"[a-z]+\" data-done=\"(true|false)\"")]
    private static partial Regex ChecklistItem();

    private static async Task<HttpResponseMessage> PostBrandAsync(
        HttpClient client, string accent, string vat, byte[]? logo, string fileName = "logo.png", string fileType = "image/png")
    {
        var token = await TestData.TokenAsync(client, "/Onboarding?step=1");
        var form = new MultipartFormDataContent
        {
            { new StringContent(accent), "accent" },
            { new StringContent(vat), "vatPercent" },
            { new StringContent(token), "__RequestVerificationToken" },
        };
        if (logo is not null)
        {
            var file = new ByteArrayContent(logo);
            file.Headers.ContentType = new(fileType);
            form.Add(file, "upload", fileName);
        }
        return await client.PostAsync("/Onboarding?handler=Brand", form);
    }

    private static async Task<HttpResponseMessage> PostCarAsync(
        HttpClient client, string name = "Test Person", string phone = "01012345678", string plate = "ABC 123", string mileage = "1000")
    {
        var token = await TestData.TokenAsync(client, "/Onboarding?step=2");
        return await client.PostAsync("/Onboarding?handler=Customer", new FormUrlEncodedContent(
        [
            new("Input.Name", name), new("Input.Phone", phone), new("Input.Plate", plate),
            new("Input.Make", "Skoda"), new("Input.Model", "Octavia"), new("Input.Year", "2019"), new("Input.Mileage", mileage),
            new("__RequestVerificationToken", token),
        ]));
    }

    private static async Task<HttpResponseMessage> PostJobAsync(HttpClient client, int vehicleId, string notes)
    {
        var token = await TestData.TokenAsync(client, "/Onboarding?step=2");
        return await client.PostAsync("/Onboarding?handler=Job", new FormUrlEncodedContent(
        [
            new("vehicleId", vehicleId.ToString()), new("notes", notes), new("__RequestVerificationToken", token),
        ]));
    }
}
