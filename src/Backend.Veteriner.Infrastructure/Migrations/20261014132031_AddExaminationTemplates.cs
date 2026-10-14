using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExaminationTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExaminationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    VisitReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Findings = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Plan = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExaminationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExaminationTemplateLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExaminationTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExaminationTemplateLines", x => x.Id);
                    table.CheckConstraint("CK_ExaminationTemplateLines_Quantity_Positive", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ExaminationTemplateLines_ExaminationTemplates_ExaminationTemplateId",
                        column: x => x.ExaminationTemplateId,
                        principalTable: "ExaminationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExaminationTemplateLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTemplateLines_ExaminationTemplateId",
                table: "ExaminationTemplateLines",
                column: "ExaminationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTemplateLines_ProductId",
                table: "ExaminationTemplateLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTemplates_TenantId_IsActive",
                table: "ExaminationTemplates",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTemplates_TenantId_NormalizedName",
                table: "ExaminationTemplates",
                columns: new[] { "TenantId", "NormalizedName" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExaminationTemplateLines");

            migrationBuilder.DropTable(
                name: "ExaminationTemplates");
        }
    }
}
