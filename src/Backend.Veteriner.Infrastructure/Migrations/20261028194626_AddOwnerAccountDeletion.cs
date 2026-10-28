using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerAccountDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OwnerAccounts_Email",
                table: "OwnerAccounts");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "OwnerAccounts",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "OwnerAccounts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CloseReason",
                table: "OwnerAccountClientLinks",
                type: "int",
                nullable: true);

            // Mevcut kapalı bağların hepsi personelce kapatıldı (ADR-041 öncesinde başka yol yoktu).
            migrationBuilder.Sql("UPDATE [OwnerAccountClientLinks] SET [CloseReason] = 0 WHERE [Status] = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerAccounts_Email",
                table: "OwnerAccounts",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OwnerAccounts_Email",
                table: "OwnerAccounts");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "OwnerAccounts");

            migrationBuilder.DropColumn(
                name: "CloseReason",
                table: "OwnerAccountClientLinks");

            // Silinmiş hesapların e-postası yok; zorunlu ve benzersiz sütuna dönüşte her satıra ayrı, ulaşılamaz bir adres yazılır.
            migrationBuilder.Sql(
                "UPDATE [OwnerAccounts] SET [Email] = CONCAT('deleted-', LOWER(CONVERT(nvarchar(36), [Id])), '@deleted.invalid') WHERE [Email] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "OwnerAccounts",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerAccounts_Email",
                table: "OwnerAccounts",
                column: "Email",
                unique: true);
        }
    }
}
