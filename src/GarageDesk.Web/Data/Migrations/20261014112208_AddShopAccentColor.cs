using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageDesk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShopAccentColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccentColor",
                table: "Shops",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccentColor",
                table: "Shops");
        }
    }
}
