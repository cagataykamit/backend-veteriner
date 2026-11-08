using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingGateAndOptOutLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MarketingOptOutAtUtc",
                table: "OwnerAccountClientLinks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicMarketingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    DeclaredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeclaredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeclarationVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicMarketingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicMarketingSettings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MarketingOptOutLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerAccountClientLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketingOptOutLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketingOptOutLinks_OwnerAccountClientLinks_OwnerAccountClientLinkId",
                        column: x => x.OwnerAccountClientLinkId,
                        principalTable: "OwnerAccountClientLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMarketingSettings_ClinicId",
                table: "ClinicMarketingSettings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicMarketingSettings_TenantId_ClinicId",
                table: "ClinicMarketingSettings",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketingOptOutLinks_OwnerAccountClientLinkId",
                table: "MarketingOptOutLinks",
                column: "OwnerAccountClientLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketingOptOutLinks_TenantId_OwnerAccountClientLinkId",
                table: "MarketingOptOutLinks",
                columns: new[] { "TenantId", "OwnerAccountClientLinkId" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketingOptOutLinks_TokenHash",
                table: "MarketingOptOutLinks",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicMarketingSettings");

            migrationBuilder.DropTable(
                name: "MarketingOptOutLinks");

            migrationBuilder.DropColumn(
                name: "MarketingOptOutAtUtc",
                table: "OwnerAccountClientLinks");
        }
    }
}
