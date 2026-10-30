using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExaminationPackagePrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PackagePrice",
                table: "ExaminationTemplates",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "ChargeSuggestions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PackageChargeSuggestionId",
                table: "Administrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Administrations_PackageChargeSuggestionId",
                table: "Administrations",
                column: "PackageChargeSuggestionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Administrations_ChargeSuggestions_PackageChargeSuggestionId",
                table: "Administrations",
                column: "PackageChargeSuggestionId",
                principalTable: "ChargeSuggestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Administrations_ChargeSuggestions_PackageChargeSuggestionId",
                table: "Administrations");

            migrationBuilder.DropIndex(
                name: "IX_Administrations_PackageChargeSuggestionId",
                table: "Administrations");

            migrationBuilder.DropColumn(
                name: "PackagePrice",
                table: "ExaminationTemplates");

            migrationBuilder.DropColumn(
                name: "PackageChargeSuggestionId",
                table: "Administrations");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProductId",
                table: "ChargeSuggestions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
