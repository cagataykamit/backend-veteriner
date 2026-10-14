using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChargeSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChargeSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExaminationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VisitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SuggestedUnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WaiveReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargeSuggestions", x => x.Id);
                    table.CheckConstraint("CK_ChargeSuggestions_Quantity_Positive", "[Quantity] > 0");
                    table.CheckConstraint("CK_ChargeSuggestions_Status_Consistent", "([Status] = 0 AND [ChargeId] IS NULL AND [WaiveReason] IS NULL AND [ResolvedAtUtc] IS NULL) OR ([Status] = 1 AND [ChargeId] IS NOT NULL AND [WaiveReason] IS NULL AND [ResolvedAtUtc] IS NOT NULL) OR ([Status] = 2 AND [ChargeId] IS NULL AND [WaiveReason] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL) OR ([Status] = 3 AND [ChargeId] IS NULL AND [WaiveReason] IS NULL AND [ResolvedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ChargeSuggestions_Charges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "Charges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChargeSuggestions_ChargeId",
                table: "ChargeSuggestions",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeSuggestions_TenantId_ClinicId_Status_OccurredAtUtc",
                table: "ChargeSuggestions",
                columns: new[] { "TenantId", "ClinicId", "Status", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChargeSuggestions_TenantId_ExaminationId_Status",
                table: "ChargeSuggestions",
                columns: new[] { "TenantId", "ExaminationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChargeSuggestions_TenantId_PetId_Status",
                table: "ChargeSuggestions",
                columns: new[] { "TenantId", "PetId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChargeSuggestions_TenantId_VisitId_Status",
                table: "ChargeSuggestions",
                columns: new[] { "TenantId", "VisitId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ChargeSuggestions_TenantId_ChargeId",
                table: "ChargeSuggestions",
                columns: new[] { "TenantId", "ChargeId" },
                unique: true,
                filter: "[ChargeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ChargeSuggestions_TenantId_SourceType_SourceId",
                table: "ChargeSuggestions",
                columns: new[] { "TenantId", "SourceType", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChargeSuggestions");
        }
    }
}
