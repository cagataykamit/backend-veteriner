using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPartnerDiscountCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DiscountAmountMinor",
                table: "BillingCheckoutSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GrossAmountMinor",
                table: "BillingCheckoutSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesPartners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    LinkedTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesPartners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscountCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesPartnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    NormalizedCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeactivatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountCodes_SalesPartners_SalesPartnerId",
                        column: x => x.SalesPartnerId,
                        principalTable: "SalesPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiscountRedemptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesPartnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CommissionPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    RedeemedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WindowStartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WindowEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountRedemptions_DiscountCodes_DiscountCodeId",
                        column: x => x.DiscountCodeId,
                        principalTable: "DiscountCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiscountRedemptions_SalesPartners_SalesPartnerId",
                        column: x => x.SalesPartnerId,
                        principalTable: "SalesPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiscountRedemptionCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountRedemptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckoutSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingPeriod = table.Column<int>(type: "int", nullable: false),
                    CollectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrossAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    DiscountAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    NetAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    CommissionAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaidNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountRedemptionCollections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountRedemptionCollections_DiscountRedemptions_DiscountRedemptionId",
                        column: x => x.DiscountRedemptionId,
                        principalTable: "DiscountRedemptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountCodes_NormalizedCode",
                table: "DiscountCodes",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscountCodes_SalesPartnerId",
                table: "DiscountCodes",
                column: "SalesPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRedemptionCollections_CheckoutSessionId",
                table: "DiscountRedemptionCollections",
                column: "CheckoutSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRedemptionCollections_DiscountRedemptionId",
                table: "DiscountRedemptionCollections",
                column: "DiscountRedemptionId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRedemptions_DiscountCodeId",
                table: "DiscountRedemptions",
                column: "DiscountCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRedemptions_SalesPartnerId",
                table: "DiscountRedemptions",
                column: "SalesPartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRedemptions_TenantId",
                table: "DiscountRedemptions",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesPartners_Name",
                table: "SalesPartners",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscountRedemptionCollections");

            migrationBuilder.DropTable(
                name: "DiscountRedemptions");

            migrationBuilder.DropTable(
                name: "DiscountCodes");

            migrationBuilder.DropTable(
                name: "SalesPartners");

            migrationBuilder.DropColumn(
                name: "DiscountAmountMinor",
                table: "BillingCheckoutSessions");

            migrationBuilder.DropColumn(
                name: "GrossAmountMinor",
                table: "BillingCheckoutSessions");
        }
    }
}
