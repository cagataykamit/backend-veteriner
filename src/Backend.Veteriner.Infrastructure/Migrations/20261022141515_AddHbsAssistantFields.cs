using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHbsAssistantFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "HbsMarkedAtUtc",
                table: "Vaccinations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HbsMarkedByUserId",
                table: "Vaccinations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HbsNotRequiredReason",
                table: "Vaccinations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HbsReferenceNumber",
                table: "Vaccinations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HbsStatus",
                table: "Vaccinations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "HbsMarkedAtUtc",
                table: "Prescriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HbsMarkedByUserId",
                table: "Prescriptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HbsNotRequiredReason",
                table: "Prescriptions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HbsReferenceNumber",
                table: "Prescriptions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HbsStatus",
                table: "Prescriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Dose",
                table: "PrescriptionItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LotNumber",
                table: "PrescriptionItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "PrescriptionItems",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_TenantId_ClinicId_HbsStatus",
                table: "Vaccinations",
                columns: new[] { "TenantId", "ClinicId", "HbsStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_TenantId_ClinicId_HbsStatus",
                table: "Prescriptions",
                columns: new[] { "TenantId", "ClinicId", "HbsStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vaccinations_TenantId_ClinicId_HbsStatus",
                table: "Vaccinations");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_TenantId_ClinicId_HbsStatus",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "HbsMarkedAtUtc",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "HbsMarkedByUserId",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "HbsNotRequiredReason",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "HbsReferenceNumber",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "HbsStatus",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "HbsMarkedAtUtc",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "HbsMarkedByUserId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "HbsNotRequiredReason",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "HbsReferenceNumber",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "HbsStatus",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "Dose",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "LotNumber",
                table: "PrescriptionItems");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "PrescriptionItems");
        }
    }
}
