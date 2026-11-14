using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHospitalizationCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hospitalizations_TenantId_ClinicId_PetId",
                table: "Hospitalizations");

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Hospitalizations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "Hospitalizations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Hospitalizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_TenantId_ClinicId_PetId",
                table: "Hospitalizations",
                columns: new[] { "TenantId", "ClinicId", "PetId" },
                unique: true,
                filter: "[DischargedAtUtc] IS NULL AND [CancelledAtUtc] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Hospitalizations_Cancel_Consistent",
                table: "Hospitalizations",
                sql: "([CancelledAtUtc] IS NULL AND [CancelReason] IS NULL AND [CancelledByUserId] IS NULL) OR ([CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL AND [CancelledByUserId] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Hospitalizations_TenantId_ClinicId_PetId",
                table: "Hospitalizations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Hospitalizations_Cancel_Consistent",
                table: "Hospitalizations");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Hospitalizations");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "Hospitalizations");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Hospitalizations");

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_TenantId_ClinicId_PetId",
                table: "Hospitalizations",
                columns: new[] { "TenantId", "ClinicId", "PetId" },
                unique: true,
                filter: "[DischargedAtUtc] IS NULL");
        }
    }
}
