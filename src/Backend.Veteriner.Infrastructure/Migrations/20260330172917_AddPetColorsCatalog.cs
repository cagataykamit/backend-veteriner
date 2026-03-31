using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPetColorsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ColorId",
                table: "Pets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PetColors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PetColors", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PetColors",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020001"), "BLACK", 1, true, "Siyah" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020002"), "WHITE", 2, true, "Beyaz" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020003"), "BROWN", 3, true, "Kahverengi" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020004"), "GRAY", 4, true, "Gri" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020005"), "YELLOW", 5, true, "Sarı" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020006"), "CREAM", 6, true, "Krem" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020007"), "RED", 7, true, "Kızıl" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020008"), "ORANGE", 8, true, "Turuncu" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020009"), "BLACK_WHITE", 9, true, "Siyah-Beyaz" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020010"), "BROWN_WHITE", 10, true, "Kahverengi-Beyaz" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020011"), "GRAY_WHITE", 11, true, "Gri-Beyaz" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020012"), "SPOTTED", 12, true, "Benekli" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020013"), "STRIPED", 13, true, "Çizgili" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020014"), "CALICO", 14, true, "Alacalı" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020015"), "MULTI_COLOR", 15, true, "Çok Renkli" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020016"), "OTHER", 16, true, "Diğer" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020017"), "UNKNOWN", 17, true, "Bilinmiyor" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pets_ColorId",
                table: "Pets",
                column: "ColorId");

            migrationBuilder.CreateIndex(
                name: "IX_PetColors_Code",
                table: "PetColors",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Pets_PetColors_ColorId",
                table: "Pets",
                column: "ColorId",
                principalTable: "PetColors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Pets_PetColors_ColorId",
                table: "Pets");

            migrationBuilder.DropTable(
                name: "PetColors");

            migrationBuilder.DropIndex(
                name: "IX_Pets_ColorId",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "ColorId",
                table: "Pets");
        }
    }
}
