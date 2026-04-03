using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabResultsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExaminationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TestName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ResultText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Interpretation = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_TenantId",
                table: "LabResults",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_TenantId_ClinicId",
                table: "LabResults",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_TenantId_ExaminationId",
                table: "LabResults",
                columns: new[] { "TenantId", "ExaminationId" });

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_TenantId_PetId",
                table: "LabResults",
                columns: new[] { "TenantId", "PetId" });

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_TenantId_ResultDateUtc",
                table: "LabResults",
                columns: new[] { "TenantId", "ResultDateUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabResults");
        }
    }
}
