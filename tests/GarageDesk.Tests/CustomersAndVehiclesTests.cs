using System.Net;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Tests;

public class CustomersAndVehiclesTests(GarageDeskWebFactory factory) : IClassFixture<GarageDeskWebFactory>
{
    // ---- adding customers ----

    [Fact]
    public async Task A_customer_is_saved_with_the_phone_in_plus_20_form()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Phone Garage");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, "/Customers/Create",
            ("Input.Name", "Ahmed Hassan"), ("Input.Phone", "0101 234 5678"), ("Input.Notes", "Prefers mornings"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Customers/Details/", response.Headers.Location!.ToString());

        using var db = factory.CreateContextFor(shopId);
        var customer = await db.Customers.SingleAsync();
        Assert.Equal("Ahmed Hassan", customer.Name);
        Assert.Equal("+201012345678", customer.Phone);
        Assert.Equal("Prefers mornings", customer.Notes);
        Assert.Equal(shopId, customer.ShopId);
    }

    [Fact]
    public async Task A_phone_typed_with_arabic_digits_is_stored_with_normal_digits()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Arabic Digits Garage");
        var client = factory.CreateClientFor(userId, shopId);

        await TestData.SubmitFormAsync(client, "/Customers/Create",
            ("Input.Name", "منى السيد"), ("Input.Phone", "٠١١١٢٣٤٥٦٧٨"));

        using var db = factory.CreateContextFor(shopId);
        Assert.Equal("+201112345678", (await db.Customers.SingleAsync()).Phone);
    }

    [Fact]
    public async Task An_invalid_phone_is_refused_with_a_clear_message_and_nothing_is_saved()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Bad Phone Garage");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, "/Customers/Create",
            ("Input.Name", "Someone"), ("Input.Phone", "12345"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Enter a valid Egyptian phone number", await TestData.BodyAsync(response));
        using var db = factory.CreateContextFor(shopId);
        Assert.Empty(await db.Customers.ToListAsync());
    }

    [Fact]
    public async Task A_name_is_required()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "No Name Garage");
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, "/Customers/Create",
            ("Input.Name", ""), ("Input.Phone", "01012345678"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("The Name field is required.", await TestData.BodyAsync(response));
    }

    // ---- "Done when: I can add a customer with two cars and find either car by plate" ----

    [Fact]
    public async Task A_customer_with_two_cars_is_found_by_either_plate_however_it_is_typed()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Two Cars Garage");
        var client = factory.CreateClientFor(userId, shopId);

        await TestData.SubmitFormAsync(client, "/Customers/Create",
            ("Input.Name", "Karim Mostafa"), ("Input.Phone", "01234567890"));
        int customerId;
        using (var db = factory.CreateContextFor(shopId))
        {
            customerId = (await db.Customers.SingleAsync()).Id;
        }

        var first = await AddVehicleAsync(client, customerId, "ABC-1234", "Skoda", "Octavia");
        var second = await AddVehicleAsync(client, customerId, "أ ب ج 5678", "Hyundai", "Elantra");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);

        // The customer's page lists both cars.
        var details = await TestData.BodyAsync(await client.GetAsync($"/Customers/Details/{customerId}"));
        Assert.Contains("ABC-1234", details);
        Assert.Contains("أ ب ج 5678", details);

        // Either car is found by its plate, typed loosely (case, spaces, dashes, Arabic digits).
        foreach (var search in new[] { "abc 1234", "ABC1234", "bc-12", "ابج٥٦٧٨", "ا ب ج 5678", "5678" })
        {
            var html = await TestData.BodyAsync(await client.GetAsync($"/Customers?q={Uri.EscapeDataString(search)}"));
            Assert.Contains("Karim Mostafa", html);
        }
    }

    [Fact]
    public async Task Searching_finds_customers_by_name_and_by_phone_in_any_format()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Search Garage");
        await SeedAsync(shopId,
            ("Mona Adel", "+201112223334"),
            ("Omar Fathy", "+201005556667"));
        var client = factory.CreateClientFor(userId, shopId);

        async Task<string> Search(string q) =>
            await TestData.BodyAsync(await client.GetAsync($"/Customers?q={Uri.EscapeDataString(q)}"));

        // name: any case, part of the name
        var byName = await Search("mona");
        Assert.Contains("Mona Adel", byName);
        Assert.DoesNotContain("Omar Fathy", byName);

        // phone: with or without the leading 0, with +20, partial, Arabic digits
        foreach (var phone in new[] { "01112223334", "+20 111 222 3334", "1112223", "0111 222", "٠١١١٢٢٢٣٣٣٤" })
        {
            var html = await Search(phone);
            Assert.Contains("Mona Adel", html);
            Assert.DoesNotContain("Omar Fathy", html);
        }

        var none = await Search("zzzzzz");
        Assert.Contains("No customers match your search.", none);
    }

    [Fact]
    public async Task The_vehicle_list_can_be_searched_by_plate_make_model_and_owner()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Vehicle Search Garage");
        await SeedAsync(shopId, ("Salma Ali", "+201001112223"));
        using (var db = factory.CreateContextFor(shopId))
        {
            var customer = await db.Customers.SingleAsync();
            db.Vehicles.AddRange(
                new Vehicle { CustomerId = customer.Id, Plate = "XYZ 999", PlateKey = "XYZ999", Make = "Toyota", Model = "Corolla", Year = 2018 },
                new Vehicle { CustomerId = customer.Id, Plate = "KIA 111", PlateKey = "KIA111", Make = "Kia", Model = "Sportage", Year = 2021 });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClientFor(userId, shopId);

        async Task<string> Search(string q) =>
            await TestData.BodyAsync(await client.GetAsync($"/Vehicles?q={Uri.EscapeDataString(q)}"));

        var byPlate = await Search("xyz-999");
        Assert.Contains("XYZ 999", byPlate);
        Assert.DoesNotContain("KIA 111", byPlate);

        Assert.Contains("Sportage", await Search("sportage"));
        Assert.Contains("Toyota", await Search("TOYO"));
        Assert.Contains("XYZ 999", await Search("salma"));
    }

    // ---- editing ----

    [Fact]
    public async Task A_customer_can_be_edited_and_the_phone_is_normalized_again()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Edit Garage");
        await SeedAsync(shopId, ("Old Name", "+201001112223"));
        int id;
        using (var db = factory.CreateContextFor(shopId))
        {
            id = (await db.Customers.SingleAsync()).Id;
        }
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, $"/Customers/Edit/{id}",
            ("Input.Name", "New Name"), ("Input.Phone", "0100 999 8877"), ("Input.Notes", ""));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var check = factory.CreateContextFor(shopId);
        var customer = await check.Customers.SingleAsync();
        Assert.Equal("New Name", customer.Name);
        Assert.Equal("+201009998877", customer.Phone);
        Assert.Null(customer.Notes);
    }

    [Fact]
    public async Task A_vehicle_can_be_edited_and_moved_to_another_customer_of_the_same_shop()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Transfer Garage");
        await SeedAsync(shopId, ("Seller", "+201001112223"), ("Buyer", "+201002223334"));
        int vehicleId, buyerId;
        using (var db = factory.CreateContextFor(shopId))
        {
            var seller = await db.Customers.SingleAsync(c => c.Name == "Seller");
            buyerId = (await db.Customers.SingleAsync(c => c.Name == "Buyer")).Id;
            var vehicle = new Vehicle { CustomerId = seller.Id, Plate = "OLD 1", PlateKey = "OLD1", Make = "Fiat", Model = "Tipo", Year = 2017, Mileage = 100 };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleId = vehicle.Id;
        }
        var client = factory.CreateClientFor(userId, shopId);

        var response = await TestData.SubmitFormAsync(client, $"/Vehicles/Edit/{vehicleId}",
            ("Input.CustomerId", buyerId.ToString()), ("Input.Plate", "NEW 2"), ("Input.Make", "Fiat"),
            ("Input.Model", "Tipo"), ("Input.Year", "2017"), ("Input.Mileage", "150000"), ("Input.Vin", "abc123"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var check = factory.CreateContextFor(shopId);
        var saved = await check.Vehicles.SingleAsync();
        Assert.Equal(buyerId, saved.CustomerId);
        Assert.Equal("NEW 2", saved.Plate);
        Assert.Equal("NEW2", saved.PlateKey);
        Assert.Equal(150000, saved.Mileage);
        Assert.Equal("ABC123", saved.Vin);
    }

    [Fact]
    public async Task Vehicle_numbers_are_range_checked()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Range Garage");
        await SeedAsync(shopId, ("Owner", "+201001112223"));
        int customerId;
        using (var db = factory.CreateContextFor(shopId))
        {
            customerId = (await db.Customers.SingleAsync()).Id;
        }
        var client = factory.CreateClientFor(userId, shopId);

        var response = await AddVehicleAsync(client, customerId, "RNG 1", "Kia", "Rio", year: "1800", mileage: "-5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await TestData.BodyAsync(response);
        Assert.Contains("The field Year must be between 1950 and 2100.", html);
        Assert.Contains("must be between 0 and", html);
        using var check = factory.CreateContextFor(shopId);
        Assert.Empty(await check.Vehicles.ToListAsync());
    }

    // ---- one shop must never reach another shop's customers or vehicles (CLAUDE.md rule 2) ----

    [Fact]
    public async Task Shop_B_cannot_see_open_edit_or_change_shop_As_customers_and_vehicles()
    {
        var (userA, shopA) = await TestData.CreateOwnerAsync(factory, "Shop A");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B");
        await SeedAsync(shopA, ("Alice of A", "+201001110001"));
        int customerA, vehicleA;
        using (var db = factory.CreateContextFor(shopA))
        {
            customerA = (await db.Customers.SingleAsync()).Id;
            var vehicle = new Vehicle { CustomerId = customerA, Plate = "AAA 111", PlateKey = "AAA111", Make = "Skoda", Model = "Fabia", Year = 2020 };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleA = vehicle.Id;
        }
        await SeedAsync(shopB, ("Bob of B", "+201002220002"));
        var clientB = factory.CreateClientFor(userB, shopB);

        // Opening or editing A's records by id is simply "not found".
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Customers/Details/{customerA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Customers/Edit/{customerA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Vehicles/Details/{vehicleA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/Vehicles/Edit/{vehicleA}")).StatusCode);

        // Posting an edit for A's records changes nothing (the token comes from B's own create page).
        var token = await TestData.TokenAsync(clientB, "/Customers/Create");
        var tamper = await clientB.PostAsync($"/Customers/Edit/{customerA}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Name"] = "Hacked", ["Input.Phone"] = "01000000000", ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.NotFound, tamper.StatusCode);

        // B's lists and searches never include A's data, even for A's exact name, phone or plate.
        var customers = await TestData.BodyAsync(await clientB.GetAsync("/Customers"));
        Assert.Contains("Bob of B", customers);
        Assert.DoesNotContain("Alice of A", customers);
        foreach (var q in new[] { "Alice", "01001110001", "AAA 111", "AAA111" })
        {
            Assert.Contains("No customers match your search.", await TestData.BodyAsync(await clientB.GetAsync($"/Customers?q={Uri.EscapeDataString(q)}")));
        }
        Assert.DoesNotContain("AAA 111", await TestData.BodyAsync(await clientB.GetAsync("/Vehicles")));
        Assert.Contains("No vehicles match your search.", await TestData.BodyAsync(await clientB.GetAsync("/Vehicles?q=AAA111")));

        // A is untouched.
        using var checkA = factory.CreateContextFor(shopA);
        Assert.Equal("Alice of A", (await checkA.Customers.SingleAsync()).Name);
    }

    [Fact]
    public async Task Shop_B_cannot_attach_a_vehicle_to_shop_As_customer()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "Shop A2");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B2");
        await SeedAsync(shopA, ("Alice of A2", "+201001110001"));
        await SeedAsync(shopB, ("Bob of B2", "+201002220002"));
        int customerA;
        using (var db = factory.CreateContextFor(shopA))
        {
            customerA = (await db.Customers.SingleAsync()).Id;
        }
        var clientB = factory.CreateClientFor(userB, shopB);

        // A forged form that names A's customer as the owner is refused...
        var response = await AddVehicleAsync(clientB, customerA, "EVIL 1", "Kia", "Rio");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Choose the owner.", await TestData.BodyAsync(response));

        // ...and A's customer is not even offered in B's owner list.
        var form = await TestData.BodyAsync(await clientB.GetAsync("/Vehicles/Create"));
        Assert.Contains("Bob of B2", form);
        Assert.DoesNotContain("Alice of A2", form);

        using var checkA = factory.CreateContextFor(shopA);
        Assert.Empty(await checkA.Vehicles.ToListAsync());
        using var checkB = factory.CreateContextFor(shopB);
        Assert.Empty(await checkB.Vehicles.ToListAsync());
    }

    [Fact]
    public async Task A_vehicle_cannot_be_moved_to_another_shops_customer()
    {
        var (_, shopA) = await TestData.CreateOwnerAsync(factory, "Shop A3");
        var (userB, shopB) = await TestData.CreateOwnerAsync(factory, "Shop B3");
        await SeedAsync(shopA, ("Alice of A3", "+201001110001"));
        await SeedAsync(shopB, ("Bob of B3", "+201002220002"));
        int customerA, customerB, vehicleB;
        using (var db = factory.CreateContextFor(shopA))
        {
            customerA = (await db.Customers.SingleAsync()).Id;
        }
        using (var db = factory.CreateContextFor(shopB))
        {
            customerB = (await db.Customers.SingleAsync()).Id;
            var vehicle = new Vehicle { CustomerId = customerB, Plate = "B3 1", PlateKey = "B31", Make = "Kia", Model = "Rio", Year = 2020 };
            db.Vehicles.Add(vehicle);
            await db.SaveChangesAsync();
            vehicleB = vehicle.Id;
        }
        var clientB = factory.CreateClientFor(userB, shopB);

        var response = await TestData.SubmitFormAsync(clientB, $"/Vehicles/Edit/{vehicleB}",
            ("Input.CustomerId", customerA.ToString()), ("Input.Plate", "B3 1"), ("Input.Make", "Kia"),
            ("Input.Model", "Rio"), ("Input.Year", "2020"), ("Input.Mileage", "0"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var check = factory.CreateContextFor(shopB);
        Assert.Equal(customerB, (await check.Vehicles.SingleAsync()).CustomerId);
    }

    // ---- who can open these pages ----

    [Theory]
    [InlineData("/Customers")]
    [InlineData("/Customers/Create")]
    [InlineData("/Vehicles")]
    [InlineData("/Vehicles/Create")]
    public async Task Visitors_who_are_not_signed_in_cannot_open_the_pages(string path)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/Customers")]
    [InlineData("/Vehicles")]
    public async Task A_signed_in_user_without_a_shop_is_sent_to_create_one(string path)
    {
        var userId = await TestData.CreateUserWithoutShopAsync(factory);
        var client = factory.CreateClientFor(userId);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/CreateShop", response.Headers.Location!.ToString());
    }

    // ---- home page and Arabic ----

    [Fact]
    public async Task The_home_page_shows_this_shops_customer_and_vehicle_counts()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Counts Garage");
        await SeedAsync(shopId, ("One", "+201001110001"), ("Two", "+201001110002"));
        using (var db = factory.CreateContextFor(shopId))
        {
            var customer = await db.Customers.FirstAsync();
            db.Vehicles.AddRange(
                new Vehicle { CustomerId = customer.Id, Plate = "C 1", PlateKey = "C1", Make = "a", Model = "b", Year = 2000 },
                new Vehicle { CustomerId = customer.Id, Plate = "C 2", PlateKey = "C2", Make = "a", Model = "b", Year = 2000 },
                new Vehicle { CustomerId = customer.Id, Plate = "C 3", PlateKey = "C3", Make = "a", Model = "b", Year = 2000 });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClientFor(userId, shopId);

        var html = await TestData.BodyAsync(await client.GetAsync("/"));

        // The stat cards: customers and vehicles.
        var flat = System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ");
        Assert.Matches(@"Customers</span>.*?stat-value"">\s*2\s*</p>", flat);
        Assert.Matches(@"Vehicles</span>.*?stat-value"">\s*3\s*</p>", flat);
    }

    [Fact]
    public async Task The_customer_pages_are_translated_when_arabic_is_chosen()
    {
        var (userId, shopId) = await TestData.CreateOwnerAsync(factory, "Arabic Pages Garage");
        var client = factory.CreateClientFor(userId, shopId);
        var token = await TestData.TokenAsync(client, "/Customers");
        await client.PostAsync("/SetCulture", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "ar", ["returnUrl"] = "/", ["__RequestVerificationToken"] = token,
        }));

        var list = await TestData.BodyAsync(await client.GetAsync("/Customers"));
        Assert.Contains("dir=\"rtl\"", list);
        Assert.Contains("إضافة عميل", list);

        var response = await TestData.SubmitFormAsync(client, "/Customers/Create",
            ("Input.Name", "عميل"), ("Input.Phone", "123"));
        Assert.Contains("أدخل رقم هاتف مصريًا صحيحًا", await TestData.BodyAsync(response));
    }

    // ---- helpers ----

    private async Task SeedAsync(int shopId, params (string Name, string Phone)[] customers)
    {
        using var db = factory.CreateContextFor(shopId);
        db.Customers.AddRange(customers.Select(c => new Customer { Name = c.Name, Phone = c.Phone }));
        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> AddVehicleAsync(
        HttpClient client, int customerId, string plate, string make, string model,
        string year = "2020", string mileage = "50000") =>
        TestData.SubmitFormAsync(client, "/Vehicles/Create",
            ("Input.CustomerId", customerId.ToString()), ("Input.Plate", plate), ("Input.Make", make),
            ("Input.Model", model), ("Input.Year", year), ("Input.Mileage", mileage));
}
