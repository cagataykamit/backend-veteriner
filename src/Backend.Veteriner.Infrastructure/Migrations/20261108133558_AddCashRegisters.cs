using System;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashRegisters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CashCounts_TenantId_ClinicId_BusinessDate_Active",
                table: "CashCounts");

            migrationBuilder.AddColumn<Guid>(
                name: "CashRegisterId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundCashRegisterId",
                table: "ClientCredits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CashRegisterId",
                table: "CashCounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashMovementCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovementCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashRegisters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsMain = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashRegisters", x => x.Id);
                    table.CheckConstraint("CK_CashRegisters_Main_Active", "[IsMain] = 0 OR [IsActive] = 1");
                    table.ForeignKey(
                        name: "FK_CashRegisters_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashRegisterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CounterpartCashRegisterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MutationSequence = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.CheckConstraint("CK_CashMovements_Amount_Positive", "[Amount] > 0");
                    table.CheckConstraint("CK_CashMovements_Transfer_Consistent", "([Kind] IN (1, 2) AND [TransferId] IS NULL AND [CounterpartCashRegisterId] IS NULL) OR ([Kind] IN (3, 4) AND [TransferId] IS NOT NULL AND [CounterpartCashRegisterId] IS NOT NULL)");
                    table.CheckConstraint("CK_CashMovements_Void_Consistent", "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CashMovements_CashMovementCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "CashMovementCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashRegisters_CashRegisterId",
                        column: x => x.CashRegisterId,
                        principalTable: "CashRegisters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashRegisters_CounterpartCashRegisterId",
                        column: x => x.CounterpartCashRegisterId,
                        principalTable: "CashRegisters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Geriye dönük doldurma (tekrar çalıştırılabilir): her kliniğe Ana kasa, eski nakit ödeme, nakit iade ve sayımlar ona bağlanır.
            migrationBuilder.Sql(CashRegisterBackfill.Sql);

            migrationBuilder.AlterColumn<Guid>(
                name: "CashRegisterId",
                table: "CashCounts",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CashRegisterId",
                table: "Payments",
                column: "CashRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCredits_RefundCashRegisterId",
                table: "ClientCredits",
                column: "RefundCashRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_CashRegisterId",
                table: "CashCounts",
                column: "CashRegisterId");

            migrationBuilder.CreateIndex(
                name: "UX_CashCounts_TenantId_ClinicId_CashRegisterId_BusinessDate_Active",
                table: "CashCounts",
                columns: new[] { "TenantId", "ClinicId", "CashRegisterId", "BusinessDate" },
                unique: true,
                filter: "[VoidedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CashMovementCategories_TenantId_Name",
                table: "CashMovementCategories",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashRegisterId",
                table: "CashMovements",
                column: "CashRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CategoryId",
                table: "CashMovements",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ClinicId",
                table: "CashMovements",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CounterpartCashRegisterId",
                table: "CashMovements",
                column: "CounterpartCashRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_TenantId_ClinicId_CashRegisterId_BusinessDate",
                table: "CashMovements",
                columns: new[] { "TenantId", "ClinicId", "CashRegisterId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_TenantId_TransferId",
                table: "CashMovements",
                columns: new[] { "TenantId", "TransferId" },
                filter: "[TransferId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisters_ClinicId",
                table: "CashRegisters",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "UX_CashRegisters_TenantId_ClinicId_Code",
                table: "CashRegisters",
                columns: new[] { "TenantId", "ClinicId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CashRegisters_TenantId_ClinicId_Main",
                table: "CashRegisters",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true,
                filter: "[IsMain] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_CashCounts_CashRegisters_CashRegisterId",
                table: "CashCounts",
                column: "CashRegisterId",
                principalTable: "CashRegisters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClientCredits_CashRegisters_RefundCashRegisterId",
                table: "ClientCredits",
                column: "RefundCashRegisterId",
                principalTable: "CashRegisters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_CashRegisters_CashRegisterId",
                table: "Payments",
                column: "CashRegisterId",
                principalTable: "CashRegisters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashCounts_CashRegisters_CashRegisterId",
                table: "CashCounts");

            migrationBuilder.DropForeignKey(
                name: "FK_ClientCredits_CashRegisters_RefundCashRegisterId",
                table: "ClientCredits");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_CashRegisters_CashRegisterId",
                table: "Payments");

            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "CashMovementCategories");

            migrationBuilder.DropTable(
                name: "CashRegisters");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CashRegisterId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_ClientCredits_RefundCashRegisterId",
                table: "ClientCredits");

            migrationBuilder.DropIndex(
                name: "IX_CashCounts_CashRegisterId",
                table: "CashCounts");

            migrationBuilder.DropIndex(
                name: "UX_CashCounts_TenantId_ClinicId_CashRegisterId_BusinessDate_Active",
                table: "CashCounts");

            migrationBuilder.DropColumn(
                name: "CashRegisterId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundCashRegisterId",
                table: "ClientCredits");

            migrationBuilder.DropColumn(
                name: "CashRegisterId",
                table: "CashCounts");

            migrationBuilder.CreateIndex(
                name: "UX_CashCounts_TenantId_ClinicId_BusinessDate_Active",
                table: "CashCounts",
                columns: new[] { "TenantId", "ClinicId", "BusinessDate" },
                unique: true,
                filter: "[VoidedAtUtc] IS NULL");
        }
    }
}
