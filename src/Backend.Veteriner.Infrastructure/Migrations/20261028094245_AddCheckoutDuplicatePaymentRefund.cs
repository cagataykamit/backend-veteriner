using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutDuplicatePaymentRefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DuplicatePaymentDetectedAtUtc",
                table: "BillingCheckoutSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundAttemptCount",
                table: "BillingCheckoutSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RefundFailureReason",
                table: "BillingCheckoutSessions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundStatus",
                table: "BillingCheckoutSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAtUtc",
                table: "BillingCheckoutSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCheckoutSessions_RefundStatus",
                table: "BillingCheckoutSessions",
                column: "RefundStatus",
                filter: "[RefundStatus] <> 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingCheckoutSessions_RefundStatus",
                table: "BillingCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "DuplicatePaymentDetectedAtUtc",
                table: "BillingCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "RefundAttemptCount",
                table: "BillingCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "RefundFailureReason",
                table: "BillingCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "RefundStatus",
                table: "BillingCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "RefundedAtUtc",
                table: "BillingCheckoutSessions");
        }
    }
}
