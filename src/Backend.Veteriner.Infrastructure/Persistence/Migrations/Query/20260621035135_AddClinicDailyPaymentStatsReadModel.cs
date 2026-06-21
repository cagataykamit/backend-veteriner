using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Persistence.Migrations.Query
{
    /// <inheritdoc />
    public partial class AddClinicDailyPaymentStatsReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicDailyPaymentStatsReadModels",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PaidTotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidCount = table.Column<int>(type: "int", nullable: false),
                    LastEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastEventOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicDailyPaymentStatsReadModels", x => new { x.TenantId, x.ClinicId, x.LocalDate, x.Currency });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicDailyPaymentStatsReadModels_TenantId_LocalDate",
                table: "ClinicDailyPaymentStatsReadModels",
                columns: new[] { "TenantId", "LocalDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicDailyPaymentStatsReadModels");
        }
    }
}
