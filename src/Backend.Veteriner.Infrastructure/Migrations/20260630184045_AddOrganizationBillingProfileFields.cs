using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationBillingProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantBillingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LegalCompanyName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TaxOffice = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TaxNumber = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    CompanyPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceProvince = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InvoiceDistrict = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InvoiceNeighborhood = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    InvoiceStreet = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    InvoiceBuildingName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InvoiceBuildingNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    InvoiceDoorNo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantBillingProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantBillingProfiles_TenantId",
                table: "TenantBillingProfiles",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantBillingProfiles");
        }
    }
}
