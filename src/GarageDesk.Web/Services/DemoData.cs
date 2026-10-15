using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>
/// The demo shop "Demo Motors": 10 customers, 15 cars, a year of service history and some due reminders.
/// Every date is worked out from "today", so the dashboard always has overdue and due-soon reminders
/// and the activity chart always has recent months, whenever the data is created.
/// </summary>
public static class DemoData
{
    public const string ShopName = "Demo Motors";
    public const string ShopPhone = "+20 2 2345 6789";
    public const string ShopAddress = "12 Abbas El Akkad St, Nasr City, Cairo";

    // Roughly how far a car in this garage drives per day. Used to wind a car's odometer back to older visits.
    private const int KmPerDay = 27;

    private record Owner(string Name, string Phone, string? Notes = null);

    private record Visit(int DaysAgo, string Package, JobStatus Status = JobStatus.Paid, string? Notes = null);

    private record Car(int OwnerIndex, string Plate, string Make, string Model, int Year, int KmNow, Visit[] Visits);

    private record Due(int CarIndex, string Type, int? InDays, int? KmFromNow, bool Done = false);

    private static readonly Owner[] Owners =
    [
        new("Ahmed Hassan", "01001234567", "Prefers WhatsApp. Pays cash."),
        new("Mohamed Ibrahim", "01112345678"),
        new("Mahmoud Saad", "01223456789", "Drives for Uber, brings both cars in together."),
        new("Omar Khaled", "01098765432"),
        new("Karim Nabil", "01155512345"),
        new("Mostafa Adel", "01266677788"),
        new("Hassan Tarek", "01011122233", "Company cars, invoice needed every time."),
        new("Youssef Sherif", "01577788899"),
        new("Ali Fathy", "01033344455"),
        new("Hany Gamal", "01144455566"),
    ];

    // Three cars of each make. Owners 0, 2, 4, 6 and 8 have two cars; the others have one.
    private static readonly Car[] Cars =
    [
        new(0, "س ن م 4821", "Skoda", "Octavia", 2019, 118_400,
            [new(410, "Oil"), new(275, "Brakes"), new(160, "DSG"), new(40, "Oil"), new(2, "AC", JobStatus.InProgress, "Weak cooling, check the compressor.")]),
        new(0, "ق ر ط 1936", "Hyundai", "Accent", 2017, 142_900,
            [new(340, "Oil"), new(200, "Battery"), new(95, "Tyres"), new(5, "Brakes", JobStatus.Done)]),
        new(1, "ع ل ك 7745", "Volkswagen", "Golf", 2018, 96_200,
            [new(380, "Oil"), new(230, "Plugs"), new(120, "DSG"), new(18, "Oil")]),
        new(2, "أ ه د 3058", "Toyota", "Corolla", 2019, 87_300,
            [new(300, "Oil"), new(170, "AC"), new(60, "Tyres")]),
        new(2, "ب ح ن 6612", "Kia", "Picanto", 2016, 131_500,
            [new(420, "Timing"), new(250, "Oil"), new(90, "Suspension"), new(1, "Diag", JobStatus.InProgress, "Check engine light on.")]),
        new(3, "م ص ر 9047", "Hyundai", "Elantra", 2020, 64_800,
            [new(210, "Oil"), new(80, "Brakes"), new(-2, "Oil", JobStatus.Booked)]),
        new(4, "ك ل ع 2280", "Kia", "Cerato", 2018, 109_700,
            [new(360, "Oil"), new(215, "Plugs"), new(100, "Battery"), new(9, "AC", JobStatus.Done)]),
        new(4, "د ج أ 5531", "Skoda", "Fabia", 2017, 125_300,
            [new(290, "Oil"), new(130, "Coolant"), new(45, "Brakes")]),
        new(5, "ر و ن 8174", "Volkswagen", "Tiguan", 2020, 71_500,
            [new(240, "Oil"), new(110, "DSG"), new(14, "Tyres")]),
        new(6, "ط ب س 1460", "Toyota", "Yaris", 2018, 92_100,
            [new(330, "Oil"), new(185, "Brakes"), new(55, "Oil")]),
        new(6, "ه ن د 7203", "Hyundai", "Tucson", 2019, 83_400,
            [new(270, "Oil"), new(150, "AC"), new(30, "Suspension"), new(-1, "Diag", JobStatus.Booked, "Customer reports a rattle over bumps.")]),
        new(7, "ج م ل 3896", "Kia", "Sportage", 2021, 48_900,
            [new(180, "Oil"), new(65, "Oil")]),
        new(8, "ف ر ص 6025", "Toyota", "Fortuner", 2020, 77_600,
            [new(310, "Oil"), new(205, "Timing"), new(85, "Brakes"), new(3, "Oil", JobStatus.Done)]),
        new(8, "ص ح ب 4378", "Skoda", "Superb", 2021, 52_300,
            [new(140, "Oil"), new(20, "DSG")]),
        new(9, "ل ن و 9512", "Volkswagen", "Passat", 2016, 148_800,
            [new(395, "Oil"), new(265, "Timing"), new(120, "Coolant"), new(7, "Battery")]),
    ];

    // A mix of overdue (by date or by km), due soon, upcoming and done, so every state shows up.
    private static readonly Due[] Reminders =
    [
        new(2, "Oil change", InDays: -22, KmFromNow: null),                  // overdue by date
        new(9, "Oil change", InDays: -4, KmFromNow: 1_500),                  // overdue by date (km still fine)
        new(3, "Oil change", InDays: 60, KmFromNow: -150),                   // overdue by km first
        new(8, "Oil change", InDays: -9, KmFromNow: null),                   // overdue by date
        new(7, "Brake inspection", InDays: 6, KmFromNow: null),              // due soon by date
        new(0, "Oil change", InDays: 11, KmFromNow: 8_200),                  // due soon by date
        new(11, "Oil change", InDays: 3, KmFromNow: null),                   // due soon by date
        new(12, "Timing belt", InDays: null, KmFromNow: 300),                // due soon by km
        new(14, "Annual inspection", InDays: 45, KmFromNow: null),           // upcoming
        new(6, "Tyre rotation", InDays: null, KmFromNow: 4_000),             // upcoming by km
        new(1, "Oil change", InDays: 60, KmFromNow: 9_000),                  // upcoming
        new(4, "Battery check", InDays: -60, KmFromNow: null, Done: true),   // done, so it stays off the dashboard
    ];

    // What was done on a visit. Prices are plausible Egyptian garage prices in EGP.
    private static readonly Dictionary<string, (JobLineType Type, string Description, decimal Quantity, decimal UnitPrice)[]> Packages = new()
    {
        ["Oil"] = [(JobLineType.Part, "Engine oil 5W-30 (4 L)", 1, 650m), (JobLineType.Part, "Oil filter", 1, 120m), (JobLineType.Labor, "Oil change", 0.5m, 150m)],
        ["Brakes"] = [(JobLineType.Part, "Front brake pads", 1, 900m), (JobLineType.Part, "Brake discs (pair)", 1, 1_800m), (JobLineType.Labor, "Fit brake pads and discs", 1.5m, 200m)],
        ["AC"] = [(JobLineType.Part, "AC gas R134a", 1, 350m), (JobLineType.Part, "Cabin filter", 1, 180m), (JobLineType.Labor, "AC service", 1, 250m)],
        ["Timing"] = [(JobLineType.Part, "Timing belt kit", 1, 2_600m), (JobLineType.Part, "Water pump", 1, 900m), (JobLineType.Labor, "Timing belt replacement", 4, 220m)],
        ["Tyres"] = [(JobLineType.Part, "Tyre 205/55 R16", 4, 1_650m), (JobLineType.Labor, "Wheel alignment and balancing", 1, 300m)],
        ["Battery"] = [(JobLineType.Part, "Battery 70Ah", 1, 2_400m), (JobLineType.Labor, "Battery fit and charging test", 0.5m, 100m)],
        ["Plugs"] = [(JobLineType.Part, "Spark plugs", 4, 180m), (JobLineType.Labor, "Replace spark plugs", 1, 150m)],
        ["Diag"] = [(JobLineType.Labor, "Computer diagnostics", 1, 300m)],
        ["DSG"] = [(JobLineType.Part, "DSG gearbox oil service kit", 1, 3_200m), (JobLineType.Labor, "DSG gearbox service", 2, 250m)],
        ["Suspension"] = [(JobLineType.Part, "Front shock absorbers (pair)", 1, 2_200m), (JobLineType.Labor, "Fit shock absorbers", 2, 220m)],
        ["Coolant"] = [(JobLineType.Part, "Coolant (5 L)", 1, 280m), (JobLineType.Part, "Thermostat", 1, 350m), (JobLineType.Labor, "Cooling system flush", 1, 180m)],
    };

    /// <summary>
    /// Fills the shop that <paramref name="db"/> is acting as. Does nothing (and returns false) if the shop already
    /// has customers, so running it twice never creates duplicates.
    /// </summary>
    public static async Task<bool> PopulateAsync(ApplicationDbContext db, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (await db.Customers.AnyAsync(cancellationToken))
        {
            return false;
        }

        var customers = Owners.Select(o => new Customer
        {
            Name = o.Name,
            Phone = EgyptianPhone.TryNormalize(o.Phone, out var phone) ? phone : throw new InvalidOperationException($"Bad demo phone {o.Phone}"),
            Notes = o.Notes,
        }).ToList();
        db.Customers.AddRange(customers);

        var vehicles = new List<Vehicle>();
        var created = new List<JobCard>();
        foreach (var car in Cars)
        {
            var vehicle = new Vehicle
            {
                Customer = customers[car.OwnerIndex],
                Plate = car.Plate,
                PlateKey = PlateNormalizer.Key(car.Plate),
                Make = car.Make,
                Model = car.Model,
                Year = car.Year,
                Mileage = car.KmNow,
            };
            vehicles.Add(vehicle);
            db.Vehicles.Add(vehicle);

            foreach (var visit in car.Visits)
            {
                var job = new JobCard
                {
                    Vehicle = vehicle,
                    DateIn = today.AddDays(-visit.DaysAgo),
                    // Wind the odometer back from today's reading; a booking has no new reading yet.
                    MileageAtVisit = visit.DaysAgo <= 0
                        ? car.KmNow
                        : Math.Max(1_000, (car.KmNow - visit.DaysAgo * KmPerDay) / 10 * 10),
                    Status = visit.Status,
                    Notes = visit.Notes,
                };
                job.Lines.AddRange(Packages[visit.Package].Select(l => new JobLine
                {
                    Type = l.Type,
                    Description = l.Description,
                    Quantity = l.Quantity,
                    UnitPrice = l.UnitPrice,
                }));
                db.JobCards.Add(job);
                created.Add(job);
            }
        }

        foreach (var due in Reminders)
        {
            var car = Cars[due.CarIndex];
            db.Reminders.Add(new Reminder
            {
                Vehicle = vehicles[due.CarIndex],
                Type = due.Type,
                DueDate = due.InDays is { } days ? today.AddDays(days) : null,
                DueMileage = due.KmFromNow is { } km ? car.KmNow + km : null,
                IsDone = due.Done,
            });
        }

        // Issued job cards get invoice numbers, oldest first, like the app would have given them.
        var issued = created.Where(j => j.Status is JobStatus.Done or JobStatus.Paid).OrderBy(j => j.DateIn).ToList();
        for (var i = 0; i < issued.Count; i++)
        {
            issued[i].InvoiceNumber = i + 1;
        }
        db.NumberSequences.Add(new NumberSequence { Kind = DocumentNumbers.Invoice, LastValue = issued.Count });

        // Paid job cards were paid in full; finished ones that are not paid yet have a deposit, so "who owes me" has rows.
        var methods = new[] { PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.Wallet };
        var paidIndex = 0;
        foreach (var job in created.Where(j => j.Status == JobStatus.Paid))
        {
            db.Payments.Add(new Payment { JobCard = job, Amount = job.Total, Method = methods[paidIndex++ % methods.Length], PaidOn = job.DateIn });
        }
        foreach (var job in created.Where(j => j.Status == JobStatus.Done).Take(2))
        {
            db.Payments.Add(new Payment { JobCard = job, Amount = JobTotals.Round(job.Total / 2), Method = PaymentMethod.Cash, PaidOn = today.AddDays(-1), Note = "Deposit" });
        }

        // One estimate waiting for the customer's answer.
        var quote = new Estimate
        {
            Vehicle = vehicles[2],
            Number = 1,
            CreatedOn = today.AddDays(-2),
            ValidUntil = today.AddDays(12),
            Status = EstimateStatus.Sent,
            Notes = "Brake discs worn, quote for the full front axle.",
        };
        quote.Lines.Add(new EstimateLine { Type = JobLineType.Part, Description = "Front brake discs (pair)", Quantity = 1, UnitPrice = 1_800m });
        quote.Lines.Add(new EstimateLine { Type = JobLineType.Part, Description = "Front brake pads", Quantity = 1, UnitPrice = 900m });
        quote.Lines.Add(new EstimateLine { Type = JobLineType.Labor, Description = "Fit brake pads and discs", Quantity = 1.5m, UnitPrice = 200m });
        db.Estimates.Add(quote);
        db.NumberSequences.Add(new NumberSequence { Kind = DocumentNumbers.Estimate, LastValue = 1 });

        // v1.4: the parts catalog and its stock ledger, expenses and recent cash closes.
        await DemoStockData.AddAsync(db, today, created, quote, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
