using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChargeFullyPaidAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FullyPaidAtUtc",
                table: "Charges",
                type: "datetime2",
                nullable: true);

            // Mevcut kapalı ücretler: an = son geçerli tahsilin (ödeme veya kredi mahsubu) kayıt anı.
            migrationBuilder.Sql(@"
UPDATE c SET FullyPaidAtUtc = x.LastAt
FROM Charges c
CROSS APPLY (
    SELECT MAX(u.CreatedAtUtc) AS LastAt FROM (
        SELECT a.CreatedAtUtc FROM PaymentAllocations a WHERE a.ChargeId = c.Id AND a.ReversedAtUtc IS NULL
        UNION ALL
        SELECT k.CreatedAtUtc FROM ClientCreditApplications k WHERE k.ChargeId = c.Id AND k.ReversedAtUtc IS NULL
    ) u
) x
WHERE c.Amount > 0 AND c.AllocatedAmount = c.Amount AND x.LastAt IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_TenantId_ClinicId_FullyPaidAtUtc",
                table: "Charges",
                columns: new[] { "TenantId", "ClinicId", "FullyPaidAtUtc" },
                filter: "[FullyPaidAtUtc] IS NOT NULL AND [EDocumentId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Charges_TenantId_ClinicId_FullyPaidAtUtc",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "FullyPaidAtUtc",
                table: "Charges");
        }
    }
}
