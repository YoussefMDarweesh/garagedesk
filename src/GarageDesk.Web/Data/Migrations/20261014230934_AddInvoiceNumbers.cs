using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GarageDesk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InvoiceNumber",
                table: "JobCards",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NumberSequences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShopId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LastValue = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NumberSequences_Shops_ShopId",
                        column: x => x.ShopId,
                        principalTable: "Shops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Existing issued job cards get their numbers, oldest first per shop, and the counters continue from there.
            migrationBuilder.Sql(GarageDesk.Web.Services.InvoiceBackfill.AssignNumbersSql);
            migrationBuilder.Sql(GarageDesk.Web.Services.InvoiceBackfill.SeedCountersSql);

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_ShopId_InvoiceNumber",
                table: "JobCards",
                columns: new[] { "ShopId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NumberSequences_ShopId_Kind",
                table: "NumberSequences",
                columns: new[] { "ShopId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NumberSequences");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_ShopId_InvoiceNumber",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "JobCards");
        }
    }
}
