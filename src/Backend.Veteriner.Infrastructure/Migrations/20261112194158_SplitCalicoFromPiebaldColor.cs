using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitCalicoFromPiebaldColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020014"),
                column: "Code",
                value: "PIEBALD");

            migrationBuilder.InsertData(
                table: "PetColors",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "Name" },
                values: new object[] { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020034"), "CALICO", 36, true, "Üç Renkli (Calico)" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020034"));

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020014"),
                column: "Code",
                value: "CALICO");
        }
    }
}
