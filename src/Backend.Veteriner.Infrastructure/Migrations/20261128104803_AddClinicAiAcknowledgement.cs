using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicAiAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAtUtc",
                table: "ClinicAiSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AcknowledgedByUserId",
                table: "ClinicAiSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcknowledgedVersion",
                table: "ClinicAiSettings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcknowledgedAtUtc",
                table: "ClinicAiSettings");

            migrationBuilder.DropColumn(
                name: "AcknowledgedByUserId",
                table: "ClinicAiSettings");

            migrationBuilder.DropColumn(
                name: "AcknowledgedVersion",
                table: "ClinicAiSettings");
        }
    }
}
