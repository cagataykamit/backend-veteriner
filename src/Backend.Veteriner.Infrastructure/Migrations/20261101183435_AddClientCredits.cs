using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditedAmount",
                table: "Payments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ClientCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UsedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RefundedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RefundMethod = table.Column<int>(type: "int", nullable: true),
                    RefundReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MutationSequence = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientCredits", x => x.Id);
                    table.CheckConstraint("CK_ClientCredits_Amount_Positive", "[Amount] > 0");
                    table.CheckConstraint("CK_ClientCredits_Refund_Consistent", "([RefundedAtUtc] IS NULL AND [RefundedAmount] IS NULL AND [RefundedByUserId] IS NULL) OR ([RefundedAtUtc] IS NOT NULL AND [RefundedAmount] > 0 AND [RefundedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_ClientCredits_UsedAmount_Range", "[UsedAmount] >= 0 AND [UsedAmount] <= [Amount]");
                    table.ForeignKey(
                        name: "FK_ClientCredits_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientCredits_Payments_SourcePaymentId",
                        column: x => x.SourcePaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClientCreditApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReversedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReverseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientCreditApplications", x => x.Id);
                    table.CheckConstraint("CK_ClientCreditApplications_Amount_Positive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_ClientCreditApplications_Charges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "Charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientCreditApplications_ClientCredits_CreditId",
                        column: x => x.CreditId,
                        principalTable: "ClientCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_CreditedAmount_Range",
                table: "Payments",
                sql: "[CreditedAmount] >= 0 AND [AllocatedAmount] + [CreditedAmount] <= [Amount]");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCreditApplications_ChargeId",
                table: "ClientCreditApplications",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCreditApplications_CreditId",
                table: "ClientCreditApplications",
                column: "CreditId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCreditApplications_TenantId_ChargeId",
                table: "ClientCreditApplications",
                columns: new[] { "TenantId", "ChargeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientCreditApplications_TenantId_CreditId",
                table: "ClientCreditApplications",
                columns: new[] { "TenantId", "CreditId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientCreditApplications_TenantId_RequestId",
                table: "ClientCreditApplications",
                columns: new[] { "TenantId", "RequestId" },
                filter: "[RequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCredits_ClientId",
                table: "ClientCredits",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCredits_SourcePaymentId",
                table: "ClientCredits",
                column: "SourcePaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCredits_TenantId_ClientId_ClinicId",
                table: "ClientCredits",
                columns: new[] { "TenantId", "ClientId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientCredits_TenantId_SourcePaymentId",
                table: "ClientCredits",
                columns: new[] { "TenantId", "SourcePaymentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientCreditApplications");

            migrationBuilder.DropTable(
                name: "ClientCredits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_CreditedAmount_Range",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CreditedAmount",
                table: "Payments");
        }
    }
}
