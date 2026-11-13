using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEDocumentBuyerAnswer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BuyerAnswerDueUtc",
                table: "EDocuments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BuyerRejectedAtUtc",
                table: "EDocuments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerRejectionReason",
                table: "EDocuments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerAnswerDueUtc",
                table: "EDocuments");

            migrationBuilder.DropColumn(
                name: "BuyerRejectedAtUtc",
                table: "EDocuments");

            migrationBuilder.DropColumn(
                name: "BuyerRejectionReason",
                table: "EDocuments");
        }
    }
}
