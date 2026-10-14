using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVaccinationAdministrationLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VaccinationId",
                table: "Administrations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Administrations_VaccinationId",
                table: "Administrations",
                column: "VaccinationId");

            migrationBuilder.CreateIndex(
                name: "UX_Administrations_TenantId_VaccinationId",
                table: "Administrations",
                columns: new[] { "TenantId", "VaccinationId" },
                unique: true,
                filter: "[VaccinationId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Administrations_Vaccinations_VaccinationId",
                table: "Administrations",
                column: "VaccinationId",
                principalTable: "Vaccinations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Administrations_Vaccinations_VaccinationId",
                table: "Administrations");

            migrationBuilder.DropIndex(
                name: "IX_Administrations_VaccinationId",
                table: "Administrations");

            migrationBuilder.DropIndex(
                name: "UX_Administrations_TenantId_VaccinationId",
                table: "Administrations");

            migrationBuilder.DropColumn(
                name: "VaccinationId",
                table: "Administrations");
        }
    }
}
