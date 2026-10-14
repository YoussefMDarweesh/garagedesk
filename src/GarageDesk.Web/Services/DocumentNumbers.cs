using GarageDesk.Web.Data;
using GarageDesk.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GarageDesk.Web.Services;

/// <summary>
/// Hands out gap-free numbers per shop and document kind (INV-0001, INV-0002, ...).
/// The increment is a single UPDATE inside a transaction: the database locks the counter row until commit,
/// so two requests can never receive the same number, on PostgreSQL and on SQLite alike.
/// </summary>
public static class DocumentNumbers
{
    public const string Invoice = "Invoice";
    public const string InvoicePrefix = "INV";

    public static string Format(string prefix, int number) => $"{prefix}-{number:D4}";

    /// <summary>
    /// Returns the next number. Call it inside a transaction together with the save that uses it,
    /// so a failure rolls the counter back too (no gaps).
    /// </summary>
    public static async Task<int> NextAsync(ApplicationDbContext db, int shopId, string kind, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var updated = await db.NumberSequences
                .Where(s => s.ShopId == shopId && s.Kind == kind)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastValue, x => x.LastValue + 1), ct);

            if (updated == 0)
            {
                // First number for this shop: create the counter. If another request created it first,
                // the unique index rejects ours and the next loop simply increments theirs.
                try
                {
                    db.NumberSequences.Add(new NumberSequence { ShopId = shopId, Kind = kind, LastValue = 0 });
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    db.ChangeTracker.Clear();
                }
                continue;
            }

            return await db.NumberSequences
                .Where(s => s.ShopId == shopId && s.Kind == kind)
                .Select(s => s.LastValue)
                .SingleAsync(ct);
        }

        throw new InvalidOperationException("Could not allocate a document number.");
    }
}

/// <summary>The SQL that gives existing Done/Paid job cards their invoice numbers, oldest first, per shop.</summary>
public static class InvoiceBackfill
{
    // Portable on purpose (PostgreSQL in the migration, SQLite in the test): number = how many issued job cards
    // of the same shop come before this one by (DateIn, Id), including itself.
    public const string AssignNumbersSql = """
        UPDATE "JobCards" SET "InvoiceNumber" = (
            SELECT COUNT(*) FROM "JobCards" AS o
            WHERE o."ShopId" = "JobCards"."ShopId"
              AND o."Status" IN ('Done', 'Paid')
              AND (o."DateIn" < "JobCards"."DateIn" OR (o."DateIn" = "JobCards"."DateIn" AND o."Id" <= "JobCards"."Id")))
        WHERE "Status" IN ('Done', 'Paid')
        """;

    public const string SeedCountersSql = """
        INSERT INTO "NumberSequences" ("ShopId", "Kind", "LastValue")
        SELECT "ShopId", 'Invoice', MAX("InvoiceNumber") FROM "JobCards"
        WHERE "InvoiceNumber" IS NOT NULL GROUP BY "ShopId"
        """;
}

/// <summary>Existing Paid job cards were paid in full before payments existed: give each one a matching cash payment.</summary>
public static class PaymentBackfill
{
    public const string PaidJobsSql = """
        INSERT INTO "Payments" ("ShopId", "JobCardId", "Amount", "Method", "PaidOn", "Note")
        SELECT j."ShopId", j."Id", SUM(ROUND(l."Quantity" * l."UnitPrice", 2)), 'Cash', j."DateIn", 'Recorded before payments existed'
        FROM "JobCards" AS j JOIN "JobLines" AS l ON l."JobCardId" = j."Id"
        WHERE j."Status" = 'Paid'
        GROUP BY j."ShopId", j."Id", j."DateIn"
        HAVING SUM(ROUND(l."Quantity" * l."UnitPrice", 2)) > 0
        """;
}
