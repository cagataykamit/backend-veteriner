using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPetStatusAndClientContactPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DeceasedOn",
                table: "Pets",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Pets",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Pets",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PreferredContactChannel",
                table: "Clients",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RemindersOptedOut",
                table: "Clients",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Pets_TenantId_Status",
                table: "Pets",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Pets_TenantId_Status",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "DeceasedOn",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "PreferredContactChannel",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "RemindersOptedOut",
                table: "Clients");
        }
    }
}
