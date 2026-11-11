using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEDocumentAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Origin",
                table: "EDocuments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ClinicEDocumentSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutoIssueMode = table.Column<int>(type: "int", nullable: false),
                    EndOfDayTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    IssueOnPaymentByDefault = table.Column<bool>(type: "bit", nullable: false),
                    AutoIssueEnabledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastEndOfDayRunDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicEDocumentSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicEDocumentSettings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicEDocumentSettings_ClinicId",
                table: "ClinicEDocumentSettings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicEDocumentSettings_TenantId_ClinicId",
                table: "ClinicEDocumentSettings",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicEDocumentSettings");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "EDocuments");
        }
    }
}
