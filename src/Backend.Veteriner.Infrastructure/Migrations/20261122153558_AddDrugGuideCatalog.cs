using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDrugGuideCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LicensedProductId",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActiveIngredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveIngredients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LicensedProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    LicenseHolder = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NormalizedLicenseHolder = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    LicenseNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SpcUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceSyncedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsListedInSource = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensedProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LicensedProductIngredients",
                columns: table => new
                {
                    LicensedProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActiveIngredientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensedProductIngredients", x => new { x.LicensedProductId, x.ActiveIngredientId });
                    table.ForeignKey(
                        name: "FK_LicensedProductIngredients_ActiveIngredients_ActiveIngredientId",
                        column: x => x.ActiveIngredientId,
                        principalTable: "ActiveIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicensedProductIngredients_LicensedProducts_LicensedProductId",
                        column: x => x.LicensedProductId,
                        principalTable: "LicensedProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LicensedProductTargetSpecies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LicensedProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SpeciesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicensedProductTargetSpecies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicensedProductTargetSpecies_LicensedProducts_LicensedProductId",
                        column: x => x.LicensedProductId,
                        principalTable: "LicensedProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LicensedProductTargetSpecies_Species_SpeciesId",
                        column: x => x.SpeciesId,
                        principalTable: "Species",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_LicensedProductId",
                table: "Products",
                column: "LicensedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveIngredients_NormalizedName",
                table: "ActiveIngredients",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicensedProductIngredients_ActiveIngredientId",
                table: "LicensedProductIngredients",
                column: "ActiveIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensedProducts_NormalizedLicenseHolder",
                table: "LicensedProducts",
                column: "NormalizedLicenseHolder");

            migrationBuilder.CreateIndex(
                name: "IX_LicensedProducts_NormalizedName",
                table: "LicensedProducts",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_LicensedProductTargetSpecies_LicensedProductId",
                table: "LicensedProductTargetSpecies",
                column: "LicensedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_LicensedProductTargetSpecies_SpeciesId",
                table: "LicensedProductTargetSpecies",
                column: "SpeciesId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_LicensedProducts_LicensedProductId",
                table: "Products",
                column: "LicensedProductId",
                principalTable: "LicensedProducts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_LicensedProducts_LicensedProductId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "LicensedProductIngredients");

            migrationBuilder.DropTable(
                name: "LicensedProductTargetSpecies");

            migrationBuilder.DropTable(
                name: "ActiveIngredients");

            migrationBuilder.DropTable(
                name: "LicensedProducts");

            migrationBuilder.DropIndex(
                name: "IX_Products_LicensedProductId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LicensedProductId",
                table: "Products");
        }
    }
}
