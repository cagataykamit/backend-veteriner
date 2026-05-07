using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderOutboxPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ReminderDispatchLogs_Status_OutboxMessageId_CreatedAtUtc",
                table: "ReminderDispatchLogs",
                columns: new[] { "Status", "OutboxMessageId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDispatchLogs_TenantId_ClinicId_CreatedAtUtc",
                table: "ReminderDispatchLogs",
                columns: new[] { "TenantId", "ClinicId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending_NextAttemptAtUtc_CreatedAtUtc",
                table: "OutboxMessages",
                columns: new[] { "NextAttemptAtUtc", "CreatedAtUtc" },
                filter: "[ProcessedAtUtc] IS NULL AND [DeadLetterAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReminderDispatchLogs_Status_OutboxMessageId_CreatedAtUtc",
                table: "ReminderDispatchLogs");

            migrationBuilder.DropIndex(
                name: "IX_ReminderDispatchLogs_TenantId_ClinicId_CreatedAtUtc",
                table: "ReminderDispatchLogs");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Pending_NextAttemptAtUtc_CreatedAtUtc",
                table: "OutboxMessages");
        }
    }
}
