using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicSiteAddressChangeWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AddressChangeWindowStartedAtUtc",
                table: "ClinicSites",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AddressChangesInWindow",
                table: "ClinicSites",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressChangeWindowStartedAtUtc",
                table: "ClinicSites");

            migrationBuilder.DropColumn(
                name: "AddressChangesInWindow",
                table: "ClinicSites");
        }
    }
}
