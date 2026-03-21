using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExaminationsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Examinations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExaminedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VisitReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Findings = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Assessment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Examinations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Examinations_TenantId",
                table: "Examinations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Examinations_TenantId_AppointmentId",
                table: "Examinations",
                columns: new[] { "TenantId", "AppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Examinations_TenantId_ClinicId",
                table: "Examinations",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_Examinations_TenantId_ExaminedAtUtc",
                table: "Examinations",
                columns: new[] { "TenantId", "ExaminedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Examinations_TenantId_PetId",
                table: "Examinations",
                columns: new[] { "TenantId", "PetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Examinations");
        }
    }
}
