using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitNoChargeConfirmed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NoChargeConfirmedAtUtc",
                table: "Visits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NoChargeConfirmedByUserId",
                table: "Visits",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoChargeConfirmedAtUtc",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "NoChargeConfirmedByUserId",
                table: "Visits");
        }
    }
}
