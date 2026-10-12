using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministrationLateEntryAndVoidOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Administrations_Void_Consistent",
                table: "Administrations");

            migrationBuilder.AddColumn<bool>(
                name: "AddedAfterFinalization",
                table: "Administrations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LateEntryReason",
                table: "Administrations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Administrations",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "VoidStockOutcome",
                table: "Administrations",
                type: "int",
                nullable: true);

            // Mevcut iptaller stoğa iade ile yapılmıştı (1 = StockReturned); tutarlılık CHECK'i eklenmeden önce doldurulur.
            migrationBuilder.Sql("UPDATE [Administrations] SET [VoidStockOutcome] = 1 WHERE [VoidedAtUtc] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Administrations_LateEntry_Consistent",
                table: "Administrations",
                sql: "([AddedAfterFinalization] = 0 AND [LateEntryReason] IS NULL) OR ([AddedAfterFinalization] = 1 AND [LateEntryReason] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Administrations_Void_Consistent",
                table: "Administrations",
                sql: "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL AND [VoidStockOutcome] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL AND [VoidStockOutcome] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Administrations_LateEntry_Consistent",
                table: "Administrations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Administrations_Void_Consistent",
                table: "Administrations");

            migrationBuilder.DropColumn(
                name: "AddedAfterFinalization",
                table: "Administrations");

            migrationBuilder.DropColumn(
                name: "LateEntryReason",
                table: "Administrations");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Administrations");

            migrationBuilder.DropColumn(
                name: "VoidStockOutcome",
                table: "Administrations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Administrations_Void_Consistent",
                table: "Administrations",
                sql: "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL)");
        }
    }
}
