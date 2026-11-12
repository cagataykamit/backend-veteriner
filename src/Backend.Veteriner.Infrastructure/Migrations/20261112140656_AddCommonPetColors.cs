using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommonPetColors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020016"),
                column: "DisplayOrder",
                value: 98);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020017"),
                column: "DisplayOrder",
                value: 99);

            migrationBuilder.InsertData(
                table: "PetColors",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020018"), "BRINDLE", 20, true, "Kırçıllı" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020019"), "BRINDLE_WHITE", 21, true, "Kırçıllı-Beyaz" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020020"), "YELLOW_WHITE", 22, true, "Sarı-Beyaz" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020021"), "BROWN_BLACK", 23, true, "Kahverengi-Siyah" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020022"), "LIGHT_BROWN", 24, true, "Açık Kahverengi" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020023"), "DUN", 25, true, "Boz" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020024"), "BEIGE", 26, true, "Bej" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020025"), "BLUE", 27, true, "Mavi" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020018"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020019"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020020"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020021"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020022"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020023"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020024"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020025"));

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020016"),
                column: "DisplayOrder",
                value: 16);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020017"),
                column: "DisplayOrder",
                value: 17);
        }
    }
}
