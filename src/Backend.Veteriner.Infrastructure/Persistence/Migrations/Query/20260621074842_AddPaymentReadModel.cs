using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Persistence.Migrations.Query
{
    /// <inheritdoc />
    public partial class AddPaymentReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentReadModels",
                columns: table => new
                {
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ClientNameNormalized = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PetNameNormalized = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NotesNormalized = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExaminationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastEventOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReadModels", x => x.PaymentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReadModels_TenantId_ClientId_PaidAtUtc",
                table: "PaymentReadModels",
                columns: new[] { "TenantId", "ClientId", "PaidAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReadModels_TenantId_ClinicId_ClientNameNormalized",
                table: "PaymentReadModels",
                columns: new[] { "TenantId", "ClinicId", "ClientNameNormalized" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReadModels_TenantId_ClinicId_PaidAtUtc_PaymentId",
                table: "PaymentReadModels",
                columns: new[] { "TenantId", "ClinicId", "PaidAtUtc", "PaymentId" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReadModels_TenantId_ClinicId_PetNameNormalized",
                table: "PaymentReadModels",
                columns: new[] { "TenantId", "ClinicId", "PetNameNormalized" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentReadModels");
        }
    }
}
