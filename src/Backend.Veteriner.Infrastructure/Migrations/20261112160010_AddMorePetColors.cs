using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMorePetColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "PetColors",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020026"), "BLACK_YELLOW", 28, true, "Siyah-Sarı" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020027"), "GRAY_BLACK", 29, true, "Gri-Siyah" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020028"), "BROWN_CREAM", 30, true, "Kahverengi-Krem" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020029"), "BEIGE_WHITE", 31, true, "Bej-Beyaz" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020030"), "BROWN_BRINDLE", 32, true, "Kahverengi-Kırçıllı" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020031"), "CHAMPAGNE", 33, true, "Şampanya" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020032"), "CHOCOLATE", 34, true, "Çikolata" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020033"), "ORANGE_WHITE", 35, true, "Turuncu-Beyaz" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020026"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020027"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020028"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020029"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020030"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020031"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020032"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020033"));
        }
    }
}
