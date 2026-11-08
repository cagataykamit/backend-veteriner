using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentRequestChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "AppointmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequests_TenantId_ClinicId_CreatedAtUtc",
                table: "AppointmentRequests",
                columns: new[] { "TenantId", "ClinicId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppointmentRequests_TenantId_ClinicId_CreatedAtUtc",
                table: "AppointmentRequests");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "AppointmentRequests");
        }
    }
}
