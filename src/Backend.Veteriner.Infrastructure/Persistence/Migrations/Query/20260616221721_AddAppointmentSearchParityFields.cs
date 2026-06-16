using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Persistence.Migrations.Query
{
    /// <inheritdoc />
    public partial class AddAppointmentSearchParityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientEmail",
                table: "AppointmentReadModels",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientPhoneNormalized",
                table: "AppointmentReadModels",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PetBreed",
                table: "AppointmentReadModels",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PetBreedRefName",
                table: "AppointmentReadModels",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientEmail",
                table: "AppointmentReadModels");

            migrationBuilder.DropColumn(
                name: "ClientPhoneNormalized",
                table: "AppointmentReadModels");

            migrationBuilder.DropColumn(
                name: "PetBreed",
                table: "AppointmentReadModels");

            migrationBuilder.DropColumn(
                name: "PetBreedRefName",
                table: "AppointmentReadModels");
        }
    }
}
