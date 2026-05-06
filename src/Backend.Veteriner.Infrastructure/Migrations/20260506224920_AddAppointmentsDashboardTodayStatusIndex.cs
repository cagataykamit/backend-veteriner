using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentsDashboardTodayStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_ClinicId_ScheduledAtUtc",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_ClinicId_ScheduledAtUtc_Status",
                table: "Appointments",
                columns: new[] { "TenantId", "ClinicId", "ScheduledAtUtc", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_ClinicId_ScheduledAtUtc_Status",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_ClinicId_ScheduledAtUtc",
                table: "Appointments",
                columns: new[] { "TenantId", "ClinicId", "ScheduledAtUtc" });
        }
    }
}
