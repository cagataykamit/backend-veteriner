using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEDocumentAutoIssuePause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoIssuePauseReason",
                table: "ClinicEDocumentSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoIssuePausedAtUtc",
                table: "ClinicEDocumentSettings",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoIssuePauseReason",
                table: "ClinicEDocumentSettings");

            migrationBuilder.DropColumn(
                name: "AutoIssuePausedAtUtc",
                table: "ClinicEDocumentSettings");
        }
    }
}
