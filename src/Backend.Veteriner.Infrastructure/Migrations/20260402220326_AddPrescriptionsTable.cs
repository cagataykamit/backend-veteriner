using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrescriptionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Prescriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExaminationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TreatmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PrescribedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    FollowUpDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_TenantId",
                table: "Prescriptions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_TenantId_ClinicId",
                table: "Prescriptions",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_TenantId_ExaminationId",
                table: "Prescriptions",
                columns: new[] { "TenantId", "ExaminationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_TenantId_PetId",
                table: "Prescriptions",
                columns: new[] { "TenantId", "PetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_TenantId_PrescribedAtUtc",
                table: "Prescriptions",
                columns: new[] { "TenantId", "PrescribedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_TenantId_TreatmentId",
                table: "Prescriptions",
                columns: new[] { "TenantId", "TreatmentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Prescriptions");
        }
    }
}
