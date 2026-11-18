using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QuotaWarningLevel",
                table: "ClinicAiSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuotaWarningMonthStartUtc",
                table: "ClinicAiSettings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Credits",
                table: "AiUsageRecords",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FundCredits",
                table: "AiUsageRecords",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AiCreditGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Credits = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCreditGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCreditGrants_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCreditGrants_ClinicId",
                table: "AiCreditGrants",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AiCreditGrants_TenantId_ClinicId_CreatedAtUtc",
                table: "AiCreditGrants",
                columns: new[] { "TenantId", "ClinicId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCreditGrants");

            migrationBuilder.DropColumn(
                name: "QuotaWarningLevel",
                table: "ClinicAiSettings");

            migrationBuilder.DropColumn(
                name: "QuotaWarningMonthStartUtc",
                table: "ClinicAiSettings");

            migrationBuilder.DropColumn(
                name: "Credits",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "FundCredits",
                table: "AiUsageRecords");
        }
    }
}
