using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicingIdentityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HbsAnimalId",
                table: "Pets",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingAddressLine",
                table: "Clients",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingDistrict",
                table: "Clients",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingProvince",
                table: "Clients",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "Clients",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NationalId",
                table: "Clients",
                type: "varchar(11)",
                unicode: false,
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PersonType",
                table: "Clients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxNumber",
                table: "Clients",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxOffice",
                table: "Clients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LineKind",
                table: "Charges",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "Charges",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicTaxpayerSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxpayerType = table.Column<int>(type: "int", nullable: true),
                    TaxIdentityNumber = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    TaxOffice = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TaxpayerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Province = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    District = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AddressLine = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DefaultVatRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    WithholdingRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicTaxpayerSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicTaxpayerSettings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicTaxpayerSettings_ClinicId",
                table: "ClinicTaxpayerSettings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicTaxpayerSettings_TenantId_ClinicId",
                table: "ClinicTaxpayerSettings",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicTaxpayerSettings");

            migrationBuilder.DropColumn(
                name: "HbsAnimalId",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "BillingAddressLine",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "BillingDistrict",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "BillingProvince",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "NationalId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "PersonType",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TaxNumber",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TaxOffice",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LineKind",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Charges");
        }
    }
}
