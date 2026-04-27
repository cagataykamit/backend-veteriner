using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderSettingsAndDispatchLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReminderDispatchLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReminderType = table.Column<int>(type: "int", nullable: false),
                    SourceEntityType = table.Column<int>(type: "int", nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    RecipientName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReminderDueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DedupeKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OutboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderDispatchLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantReminderSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentRemindersEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AppointmentReminderHoursBefore = table.Column<int>(type: "int", nullable: false),
                    VaccinationRemindersEnabled = table.Column<bool>(type: "bit", nullable: false),
                    VaccinationReminderDaysBefore = table.Column<int>(type: "int", nullable: false),
                    EmailChannelEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantReminderSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDispatchLogs_TenantId_CreatedAtUtc",
                table: "ReminderDispatchLogs",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDispatchLogs_TenantId_DedupeKey",
                table: "ReminderDispatchLogs",
                columns: new[] { "TenantId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDispatchLogs_TenantId_ReminderType_Status",
                table: "ReminderDispatchLogs",
                columns: new[] { "TenantId", "ReminderType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDispatchLogs_TenantId_SourceEntityType_SourceEntityId",
                table: "ReminderDispatchLogs",
                columns: new[] { "TenantId", "SourceEntityType", "SourceEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantReminderSettings_TenantId",
                table: "TenantReminderSettings",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReminderDispatchLogs");

            migrationBuilder.DropTable(
                name: "TenantReminderSettings");
        }
    }
}
