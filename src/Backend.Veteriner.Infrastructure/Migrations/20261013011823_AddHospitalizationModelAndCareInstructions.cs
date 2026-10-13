using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHospitalizationModelAndCareInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DischargeNote",
                table: "Hospitalizations",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Hospitalizations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Hospitalizations",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "VisitId",
                table: "Hospitalizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CareInstructions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HospitalizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExaminationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VisitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareInstructions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareInstructions_TenantId_ClinicId",
                table: "CareInstructions",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_CareInstructions_TenantId_ExaminationId",
                table: "CareInstructions",
                columns: new[] { "TenantId", "ExaminationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CareInstructions_TenantId_HospitalizationId",
                table: "CareInstructions",
                columns: new[] { "TenantId", "HospitalizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CareInstructions_TenantId_PetId",
                table: "CareInstructions",
                columns: new[] { "TenantId", "PetId" });

            migrationBuilder.CreateIndex(
                name: "IX_CareInstructions_TenantId_VisitId",
                table: "CareInstructions",
                columns: new[] { "TenantId", "VisitId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareInstructions");

            migrationBuilder.DropColumn(
                name: "DischargeNote",
                table: "Hospitalizations");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Hospitalizations");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Hospitalizations");

            migrationBuilder.DropColumn(
                name: "VisitId",
                table: "Hospitalizations");
        }
    }
}
