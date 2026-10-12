using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabResultStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LabResults_TenantId_ResultDateUtc",
                table: "LabResults");

            migrationBuilder.AlterColumn<string>(
                name: "ResultText",
                table: "LabResults",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 8000);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ResultDateUtc",
                table: "LabResults",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "LabResults",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "LabResults",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "LabResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAtUtc",
                table: "LabResults",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Eski kayitlarin istek zamani bilinmez; timeline konumu degismesin diye sonuc tarihine esitlenir.
            migrationBuilder.Sql("UPDATE [LabResults] SET [RequestedAtUtc] = [ResultDateUtc]");

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "LabResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "LabResults",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "LabResults",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "VisitId",
                table: "LabResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_TenantId_RequestedAtUtc",
                table: "LabResults",
                columns: new[] { "TenantId", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_TenantId_Status",
                table: "LabResults",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sonuc tarihi bos kayitlar (istendi/iptal) NOT NULL'a donmeden once istek zamanini alir.
            migrationBuilder.Sql("UPDATE [LabResults] SET [ResultDateUtc] = [RequestedAtUtc] WHERE [ResultDateUtc] IS NULL");

            migrationBuilder.DropIndex(
                name: "IX_LabResults_TenantId_RequestedAtUtc",
                table: "LabResults");

            migrationBuilder.DropIndex(
                name: "IX_LabResults_TenantId_Status",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "RequestedAtUtc",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "VisitId",
                table: "LabResults");

            migrationBuilder.AlterColumn<string>(
                name: "ResultText",
                table: "LabResults",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 8000,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ResultDateUtc",
                table: "LabResults",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_TenantId_ResultDateUtc",
                table: "LabResults",
                columns: new[] { "TenantId", "ResultDateUtc" });
        }
    }
}
