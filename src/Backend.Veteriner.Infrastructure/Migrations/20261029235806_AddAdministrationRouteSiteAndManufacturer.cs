using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrationRouteSiteAndManufacturer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Route",
                table: "Vaccinations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "Vaccinations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "Products",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Route",
                table: "Administrations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "Administrations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Route",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "Manufacturer",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Route",
                table: "Administrations");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "Administrations");
        }
    }
}
