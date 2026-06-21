using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Persistence.Migrations.Query
{
    /// <inheritdoc />
    public partial class AddPaymentDailyContributionReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentDailyContributionReadModels",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LastEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastEventOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentDailyContributionReadModels", x => x.PaymentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentDailyContributionReadModels_Tenant_Clinic_LocalDate_Currency",
                table: "PaymentDailyContributionReadModels",
                columns: new[] { "TenantId", "ClinicId", "LocalDate", "Currency" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentDailyContributionReadModels");
        }
    }
}
