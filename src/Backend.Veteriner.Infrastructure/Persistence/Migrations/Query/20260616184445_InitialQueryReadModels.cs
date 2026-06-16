using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Persistence.Migrations.Query
{
    /// <inheritdoc />
    public partial class InitialQueryReadModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppointmentReadModels",
                columns: table => new
                {
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SpeciesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpeciesName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ClientPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ScheduledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ScheduledEndUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    AppointmentType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReadModels", x => x.AppointmentId);
                });

            migrationBuilder.CreateTable(
                name: "ClinicClientActivityReadModels",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ClientPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LastAppointmentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicClientActivityReadModels", x => new { x.TenantId, x.ClinicId, x.ClientId });
                });

            migrationBuilder.CreateTable(
                name: "ClinicDailyAppointmentStatsReadModels",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ScheduledCount = table.Column<int>(type: "int", nullable: false),
                    CompletedCount = table.Column<int>(type: "int", nullable: false),
                    CancelledCount = table.Column<int>(type: "int", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    LastEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicDailyAppointmentStatsReadModels", x => new { x.TenantId, x.ClinicId, x.LocalDate });
                });

            migrationBuilder.CreateTable(
                name: "ClinicPetActivityReadModels",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SpeciesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpeciesName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LastAppointmentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicPetActivityReadModels", x => new { x.TenantId, x.ClinicId, x.PetId });
                });

            migrationBuilder.CreateTable(
                name: "ProcessedProjectionEvents",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedProjectionEvents", x => new { x.EventId, x.ConsumerName });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReadModels_TenantId_ClientId",
                table: "AppointmentReadModels",
                columns: new[] { "TenantId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReadModels_TenantId_ClinicId_ScheduledAtUtc_AppointmentId",
                table: "AppointmentReadModels",
                columns: new[] { "TenantId", "ClinicId", "ScheduledAtUtc", "AppointmentId" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReadModels_TenantId_ClinicId_Status_ScheduledAtUtc",
                table: "AppointmentReadModels",
                columns: new[] { "TenantId", "ClinicId", "Status", "ScheduledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReadModels_TenantId_PetId",
                table: "AppointmentReadModels",
                columns: new[] { "TenantId", "PetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicClientActivityReadModels_TenantId_ClinicId_LastAppointmentAtUtc_ClientId",
                table: "ClinicClientActivityReadModels",
                columns: new[] { "TenantId", "ClinicId", "LastAppointmentAtUtc", "ClientId" },
                descending: new[] { false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicPetActivityReadModels_TenantId_ClinicId_LastAppointmentAtUtc_PetId",
                table: "ClinicPetActivityReadModels",
                columns: new[] { "TenantId", "ClinicId", "LastAppointmentAtUtc", "PetId" },
                descending: new[] { false, false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentReadModels");

            migrationBuilder.DropTable(
                name: "ClinicClientActivityReadModels");

            migrationBuilder.DropTable(
                name: "ClinicDailyAppointmentStatsReadModels");

            migrationBuilder.DropTable(
                name: "ClinicPetActivityReadModels");

            migrationBuilder.DropTable(
                name: "ProcessedProjectionEvents");
        }
    }
}
