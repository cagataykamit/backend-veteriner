using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovementSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "StockMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversesMovementId",
                table: "StockMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_StockMovements_Reference",
                table: "StockMovements",
                columns: new[] { "TenantId", "ClinicId", "ProductId", "ReferenceType", "ReferenceId", "MovementType" },
                unique: true,
                filter: "[ReferenceType] IS NOT NULL AND [ReferenceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_StockMovements_TenantId_RequestId",
                table: "StockMovements",
                columns: new[] { "TenantId", "RequestId" },
                unique: true,
                filter: "[RequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_StockMovements_TenantId_ReversesMovementId",
                table: "StockMovements",
                columns: new[] { "TenantId", "ReversesMovementId" },
                unique: true,
                filter: "[ReversesMovementId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_StockMovements_Reference",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "UX_StockMovements_TenantId_RequestId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "UX_StockMovements_TenantId_ReversesMovementId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "ReversesMovementId",
                table: "StockMovements");
        }
    }
}
