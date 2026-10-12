using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceVoidAndReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Payments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAtUtc",
                table: "PaymentAllocations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Charges",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoidedAtUtc",
                table: "Charges",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                table: "Charges",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Void_Consistent",
                table: "Payments",
                sql: "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Voided_Unallocated",
                table: "Payments",
                sql: "[VoidedAtUtc] IS NULL OR [AllocatedAmount] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Charges_Void_Consistent",
                table: "Charges",
                sql: "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Charges_Voided_Unallocated",
                table: "Charges",
                sql: "[VoidedAtUtc] IS NULL OR [AllocatedAmount] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Void_Consistent",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Voided_Unallocated",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Charges_Void_Consistent",
                table: "Charges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Charges_Voided_Unallocated",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReversedAtUtc",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "VoidedAtUtc",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "Charges");
        }
    }
}
