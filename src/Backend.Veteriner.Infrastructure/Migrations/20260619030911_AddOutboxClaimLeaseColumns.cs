using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxClaimLeaseColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClaimToken",
                table: "OutboxMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAtUtc",
                table: "OutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClaimedBy",
                table: "OutboxMessages",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "OutboxMessages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_AppointmentProjection_PendingClaim",
                table: "OutboxMessages",
                columns: new[] { "CreatedAtUtc", "AppointmentSequence", "Id" },
                filter: "[ProcessedAtUtc] IS NULL AND [DeadLetterAtUtc] IS NULL AND [AppointmentId] IS NOT NULL AND [AppointmentSequence] IS NOT NULL");

            migrationBuilder.Sql(
                """
                CREATE NONCLUSTERED INDEX IX_OutboxMessages_AppointmentId_AppointmentSequence_Pending
                ON OutboxMessages (AppointmentId, AppointmentSequence)
                WHERE [ProcessedAtUtc] IS NULL AND [AppointmentId] IS NOT NULL AND [AppointmentSequence] IS NOT NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IX_OutboxMessages_AppointmentId_AppointmentSequence_Pending ON OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_AppointmentProjection_PendingClaim",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ClaimToken",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ClaimedAtUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "OutboxMessages");
        }
    }
}
