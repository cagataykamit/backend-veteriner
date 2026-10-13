using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchBasedStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BatchBalanceAfter",
                table: "StockMovements",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "StockMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ControlledClass",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Packaging",
                table: "Products",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PharmacologicalForm",
                table: "Products",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentationForm",
                table: "Products",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                table: "Administrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LotNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InitialQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SupplierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DispatchedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransportMethod = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ColdChainMonitor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBatches", x => x.Id);
                    table.CheckConstraint("CK_StockBatches_Quantities", "[InitialQuantity] > 0 AND [RemainingQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_StockBatches_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BatchId",
                table: "StockMovements",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Administrations_BatchId",
                table: "Administrations",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_ClinicId",
                table: "StockBatches",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_ProductId",
                table: "StockBatches",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_TenantId_ClinicId_LotNumber",
                table: "StockBatches",
                columns: new[] { "TenantId", "ClinicId", "LotNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_TenantId_ClinicId_ProductId_ExpiryDate",
                table: "StockBatches",
                columns: new[] { "TenantId", "ClinicId", "ProductId", "ExpiryDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Administrations_StockBatches_BatchId",
                table: "Administrations",
                column: "BatchId",
                principalTable: "StockBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_StockBatches_BatchId",
                table: "StockMovements",
                column: "BatchId",
                principalTable: "StockBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Administrations_StockBatches_BatchId",
                table: "Administrations");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_StockBatches_BatchId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "StockBatches");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_BatchId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Administrations_BatchId",
                table: "Administrations");

            migrationBuilder.DropColumn(
                name: "BatchBalanceAfter",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "ControlledClass",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Packaging",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PharmacologicalForm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PresentationForm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "Administrations");
        }
    }
}
