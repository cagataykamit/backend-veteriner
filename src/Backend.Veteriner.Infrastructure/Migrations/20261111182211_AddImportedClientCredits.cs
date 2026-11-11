using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportedClientCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientCredits_TenantId_SourcePaymentId",
                table: "ClientCredits");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourcePaymentId",
                table: "ClientCredits",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "ClientCredits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientCredits_ImportBatchId_ClientId",
                table: "ClientCredits",
                columns: new[] { "ImportBatchId", "ClientId" },
                unique: true,
                filter: "[ImportBatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClientCredits_TenantId_SourcePaymentId",
                table: "ClientCredits",
                columns: new[] { "TenantId", "SourcePaymentId" },
                unique: true,
                filter: "[SourcePaymentId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ClientCredits_Source_Consistent",
                table: "ClientCredits",
                sql: "([Source] = 2 AND [SourcePaymentId] IS NULL AND [ImportBatchId] IS NOT NULL) OR ([Source] <> 2 AND [SourcePaymentId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientCredits_ImportBatches_ImportBatchId",
                table: "ClientCredits",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientCredits_ImportBatches_ImportBatchId",
                table: "ClientCredits");

            migrationBuilder.DropIndex(
                name: "IX_ClientCredits_ImportBatchId_ClientId",
                table: "ClientCredits");

            migrationBuilder.DropIndex(
                name: "IX_ClientCredits_TenantId_SourcePaymentId",
                table: "ClientCredits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ClientCredits_Source_Consistent",
                table: "ClientCredits");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "ClientCredits");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourcePaymentId",
                table: "ClientCredits",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientCredits_TenantId_SourcePaymentId",
                table: "ClientCredits",
                columns: new[] { "TenantId", "SourcePaymentId" },
                unique: true);
        }
    }
}
