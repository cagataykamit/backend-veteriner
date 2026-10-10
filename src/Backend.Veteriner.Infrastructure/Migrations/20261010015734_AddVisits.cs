using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VisitId",
                table: "Examinations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Visits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponsibleVeterinarianUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ArrivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CareStatus = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MutationSequence = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Examinations_TenantId_VisitId",
                table: "Examinations",
                columns: new[] { "TenantId", "VisitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_ClinicId_ArrivedAtUtc",
                table: "Visits",
                columns: new[] { "TenantId", "ClinicId", "ArrivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Visits_TenantId_AppointmentId",
                table: "Visits",
                columns: new[] { "TenantId", "AppointmentId" },
                unique: true,
                filter: "[AppointmentId] IS NOT NULL AND [VoidedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Visits_TenantId_PetId_Active",
                table: "Visits",
                columns: new[] { "TenantId", "PetId" },
                unique: true,
                filter: "[CareStatus] <> 2 AND [VoidedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Examinations_TenantId_VisitId",
                table: "Examinations");

            migrationBuilder.DropColumn(
                name: "VisitId",
                table: "Examinations");
        }
    }
}
