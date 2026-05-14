using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVaccineDefinitionsAndVaccinationDefinitionFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VaccineDefinitionId",
                table: "Vaccinations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VaccineDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SpeciesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DefaultNextDueDays = table.Column<int>(type: "int", nullable: true),
                    IsCore = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VaccineDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VaccineDefinitions_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VaccineDefinitions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_VaccineDefinitionId",
                table: "Vaccinations",
                column: "VaccineDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_VaccineDefinitions_Code",
                table: "VaccineDefinitions",
                column: "Code",
                unique: true,
                filter: "[TenantId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VaccineDefinitions_Name",
                table: "VaccineDefinitions",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_VaccineDefinitions_SpeciesId_IsActive",
                table: "VaccineDefinitions",
                columns: new[] { "SpeciesId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_VaccineDefinitions_TenantId_Code",
                table: "VaccineDefinitions",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "[TenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VaccineDefinitions_TenantId_IsActive",
                table: "VaccineDefinitions",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_Vaccinations_VaccineDefinitions_VaccineDefinitionId",
                table: "Vaccinations",
                column: "VaccineDefinitionId",
                principalTable: "VaccineDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vaccinations_VaccineDefinitions_VaccineDefinitionId",
                table: "Vaccinations");

            migrationBuilder.DropTable(
                name: "VaccineDefinitions");

            migrationBuilder.DropIndex(
                name: "IX_Vaccinations_VaccineDefinitionId",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "VaccineDefinitionId",
                table: "Vaccinations");
        }
    }
}
