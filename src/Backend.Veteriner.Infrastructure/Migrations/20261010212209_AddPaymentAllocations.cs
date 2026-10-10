using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedAmount",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "MutationSequence",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "Payments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedAmount",
                table: "Charges",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "MutationSequence",
                table: "Charges",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "PaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                    table.CheckConstraint("CK_PaymentAllocations_Amount_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Charges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "Charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Payments_TenantId_RequestId",
                table: "Payments",
                columns: new[] { "TenantId", "RequestId" },
                unique: true,
                filter: "[RequestId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_AllocatedAmount_Range",
                table: "Payments",
                sql: "[AllocatedAmount] >= 0 AND [AllocatedAmount] <= [Amount]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Charges_AllocatedAmount_Range",
                table: "Charges",
                sql: "[AllocatedAmount] >= 0 AND [AllocatedAmount] <= [Amount]");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_ChargeId",
                table: "PaymentAllocations",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_PaymentId",
                table: "PaymentAllocations",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId_ChargeId",
                table: "PaymentAllocations",
                columns: new[] { "TenantId", "ChargeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_TenantId_PaymentId",
                table: "PaymentAllocations",
                columns: new[] { "TenantId", "PaymentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "UX_Payments_TenantId_RequestId",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_AllocatedAmount_Range",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Charges_AllocatedAmount_Range",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "AllocatedAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "MutationSequence",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AllocatedAmount",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "MutationSequence",
                table: "Charges");
        }
    }
}
