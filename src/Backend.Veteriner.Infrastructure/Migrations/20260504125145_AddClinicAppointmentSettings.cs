using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicAppointmentSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicAppointmentSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DefaultAppointmentDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    SlotIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    AllowOverlappingAppointments = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicAppointmentSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicAppointmentSettings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicAppointmentSettings_ClinicId",
                table: "ClinicAppointmentSettings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicAppointmentSettings_TenantId_ClinicId",
                table: "ClinicAppointmentSettings",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicAppointmentSettings");
        }
    }
}
