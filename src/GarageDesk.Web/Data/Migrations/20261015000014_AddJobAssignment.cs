using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GarageDesk.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedToUserId",
                table: "JobCards",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_AssignedToUserId",
                table: "JobCards",
                column: "AssignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_JobCards_ShopId_AssignedToUserId",
                table: "JobCards",
                columns: new[] { "ShopId", "AssignedToUserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_JobCards_AspNetUsers_AssignedToUserId",
                table: "JobCards",
                column: "AssignedToUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobCards_AspNetUsers_AssignedToUserId",
                table: "JobCards");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_AssignedToUserId",
                table: "JobCards");

            migrationBuilder.DropIndex(
                name: "IX_JobCards_ShopId_AssignedToUserId",
                table: "JobCards");

            migrationBuilder.DropColumn(
                name: "AssignedToUserId",
                table: "JobCards");
        }
    }
}
