using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Persistence.Migrations.Query
{
    /// <inheritdoc />
    public partial class AddPetReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PetReadModels",
                columns: table => new
                {
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientFullName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ClientFullNameNormalized = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameNormalized = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SpeciesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpeciesName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SpeciesNameNormalized = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BreedId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Breed = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    BreedRefName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ColorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ColorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ColorNameNormalized = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Gender = table.Column<int>(type: "int", nullable: true),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    LastEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastEventOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PetReadModels", x => x.PetId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PetReadModels_TenantId_ClientFullNameNormalized_PetId",
                table: "PetReadModels",
                columns: new[] { "TenantId", "ClientFullNameNormalized", "PetId" });

            migrationBuilder.CreateIndex(
                name: "IX_PetReadModels_TenantId_ClientId",
                table: "PetReadModels",
                columns: new[] { "TenantId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_PetReadModels_TenantId_ColorId",
                table: "PetReadModels",
                columns: new[] { "TenantId", "ColorId" });

            migrationBuilder.CreateIndex(
                name: "IX_PetReadModels_TenantId_NameNormalized_PetId",
                table: "PetReadModels",
                columns: new[] { "TenantId", "NameNormalized", "PetId" });

            migrationBuilder.CreateIndex(
                name: "IX_PetReadModels_TenantId_SpeciesId",
                table: "PetReadModels",
                columns: new[] { "TenantId", "SpeciesId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PetReadModels");
        }
    }
}
