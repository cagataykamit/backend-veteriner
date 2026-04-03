using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHospitalizationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hospitalizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExaminationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AdmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedDischargeAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DischargedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hospitalizations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_TenantId",
                table: "Hospitalizations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_TenantId_AdmittedAtUtc",
                table: "Hospitalizations",
                columns: new[] { "TenantId", "AdmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_TenantId_ClinicId",
                table: "Hospitalizations",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_TenantId_ClinicId_PetId",
                table: "Hospitalizations",
                columns: new[] { "TenantId", "ClinicId", "PetId" },
                unique: true,
                filter: "[DischargedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_TenantId_ExaminationId",
                table: "Hospitalizations",
                columns: new[] { "TenantId", "ExaminationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_TenantId_PetId",
                table: "Hospitalizations",
                columns: new[] { "TenantId", "PetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Hospitalizations");
        }
    }
}
