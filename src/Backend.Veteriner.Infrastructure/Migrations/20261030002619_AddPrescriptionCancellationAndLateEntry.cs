using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrescriptionCancellationAndLateEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AddedAfterFinalization",
                table: "Prescriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Prescriptions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Prescriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Prescriptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateEntryReason",
                table: "Prescriptions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Prescriptions_Cancel_Consistent",
                table: "Prescriptions",
                sql: "([CancelledAtUtc] IS NULL AND [CancelReason] IS NULL AND [CancelledByUserId] IS NULL) OR ([CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL AND [CancelledByUserId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Prescriptions_LateEntry_Consistent",
                table: "Prescriptions",
                sql: "([AddedAfterFinalization] = 0 AND [LateEntryReason] IS NULL) OR ([AddedAfterFinalization] = 1 AND [LateEntryReason] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Prescriptions_Cancel_Consistent",
                table: "Prescriptions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Prescriptions_LateEntry_Consistent",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "AddedAfterFinalization",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "LateEntryReason",
                table: "Prescriptions");
        }
    }
}
