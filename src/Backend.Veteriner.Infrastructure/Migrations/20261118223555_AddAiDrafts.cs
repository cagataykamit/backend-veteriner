using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DraftId",
                table: "AiUsageRecords",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptVersion",
                table: "AiUsageRecords",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceRefsJson",
                table: "AiUsageRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "AiDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Feature = table.Column<int>(type: "int", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceRefsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", maxLength: 50000, nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppliedRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiDrafts_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRecords_DraftId",
                table: "AiUsageRecords",
                column: "DraftId",
                filter: "[DraftId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AiDrafts_ClinicId",
                table: "AiDrafts",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDrafts_Status_ExpiresAtUtc",
                table: "AiDrafts",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDrafts_TenantId_ClinicId_TargetType_TargetId_Status",
                table: "AiDrafts",
                columns: new[] { "TenantId", "ClinicId", "TargetType", "TargetId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_AiUsageRecords_AiDrafts_DraftId",
                table: "AiUsageRecords",
                column: "DraftId",
                principalTable: "AiDrafts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiUsageRecords_AiDrafts_DraftId",
                table: "AiUsageRecords");

            migrationBuilder.DropTable(
                name: "AiDrafts");

            migrationBuilder.DropIndex(
                name: "IX_AiUsageRecords_DraftId",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "DraftId",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                table: "AiUsageRecords");

            migrationBuilder.DropColumn(
                name: "SourceRefsJson",
                table: "AiUsageRecords");
        }
    }
}
