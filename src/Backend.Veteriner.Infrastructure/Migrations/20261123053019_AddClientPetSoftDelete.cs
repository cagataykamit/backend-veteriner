using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientPetSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clients_TenantId_Email_PhoneNormalized",
                table: "Clients");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Pets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "Pets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DeletedWithClient",
                table: "Pets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Clients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "Clients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_TenantId_Email_PhoneNormalized",
                table: "Clients",
                columns: new[] { "TenantId", "Email", "PhoneNormalized" },
                unique: true,
                filter: "[Email] IS NOT NULL AND [PhoneNormalized] IS NOT NULL AND [DeletedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clients_TenantId_Email_PhoneNormalized",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "DeletedWithClient",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Clients");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_TenantId_Email_PhoneNormalized",
                table: "Clients",
                columns: new[] { "TenantId", "Email", "PhoneNormalized" },
                unique: true,
                filter: "[Email] IS NOT NULL AND [PhoneNormalized] IS NOT NULL");
        }
    }
}
