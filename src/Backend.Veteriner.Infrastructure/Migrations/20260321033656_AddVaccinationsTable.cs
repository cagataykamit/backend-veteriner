using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVaccinationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Vaccinations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExaminationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VaccineName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vaccinations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_TenantId",
                table: "Vaccinations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_TenantId_AppliedAtUtc",
                table: "Vaccinations",
                columns: new[] { "TenantId", "AppliedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_TenantId_ClinicId",
                table: "Vaccinations",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_TenantId_DueAtUtc",
                table: "Vaccinations",
                columns: new[] { "TenantId", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_TenantId_ExaminationId",
                table: "Vaccinations",
                columns: new[] { "TenantId", "ExaminationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_TenantId_PetId",
                table: "Vaccinations",
                columns: new[] { "TenantId", "PetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_TenantId_Status",
                table: "Vaccinations",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Vaccinations");
        }
    }
}
