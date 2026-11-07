using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVaccinationScheduleItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VaccinationScheduleItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpeciesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VaccineDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartAgeWeeks = table.Column<int>(type: "int", nullable: false),
                    DoseCount = table.Column<int>(type: "int", nullable: false),
                    DoseIntervalWeeks = table.Column<int>(type: "int", nullable: true),
                    RepeatIntervalMonths = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaccinationScheduleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VaccinationScheduleItems_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VaccinationScheduleItems_VaccineDefinitions_VaccineDefinitionId",
                        column: x => x.VaccineDefinitionId,
                        principalTable: "VaccineDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationScheduleItems_SpeciesId",
                table: "VaccinationScheduleItems",
                column: "SpeciesId");

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationScheduleItems_TenantId_SpeciesId_VaccineDefinitionId",
                table: "VaccinationScheduleItems",
                columns: new[] { "TenantId", "SpeciesId", "VaccineDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VaccinationScheduleItems_VaccineDefinitionId",
                table: "VaccinationScheduleItems",
                column: "VaccineDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VaccinationScheduleItems");
        }
    }
}
