using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicEnforceWorkingHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnforceWorkingHours",
                table: "ClinicAppointmentSettings",
                type: "bit",
                nullable: false,
                // Mevcut kliniklerde kısıt açık kalır (ClinicAppointmentSettings.DefaultEnforceWorkingHours; mevcut davranış).
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnforceWorkingHours",
                table: "ClinicAppointmentSettings");
        }
    }
}
