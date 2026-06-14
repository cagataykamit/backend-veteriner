using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentsTenantClinicPetDashboardCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_ClinicId_PetId",
                table: "Appointments",
                columns: new[] { "TenantId", "ClinicId", "PetId" })
                .Annotation("SqlServer:Include", new[] { "ScheduledAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_ClinicId_PetId",
                table: "Appointments");
        }
    }
}
