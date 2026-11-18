using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerExplanations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OwnerExplanations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExaminationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SourceAiDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerExplanations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerExplanations_AiDrafts_SourceAiDraftId",
                        column: x => x.SourceAiDraftId,
                        principalTable: "AiDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerExplanations_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerExplanations_Examinations_ExaminationId",
                        column: x => x.ExaminationId,
                        principalTable: "Examinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiDrafts_TenantId_AppliedRecordId",
                table: "AiDrafts",
                columns: new[] { "TenantId", "AppliedRecordId" },
                filter: "[AppliedRecordId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerExplanations_ClinicId",
                table: "OwnerExplanations",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerExplanations_ExaminationId",
                table: "OwnerExplanations",
                column: "ExaminationId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerExplanations_SourceAiDraftId",
                table: "OwnerExplanations",
                column: "SourceAiDraftId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerExplanations_TenantId_ExaminationId",
                table: "OwnerExplanations",
                columns: new[] { "TenantId", "ExaminationId" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerExplanations_TenantId_PetId",
                table: "OwnerExplanations",
                columns: new[] { "TenantId", "PetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerExplanations");

            migrationBuilder.DropIndex(
                name: "IX_AiDrafts_TenantId_AppliedRecordId",
                table: "AiDrafts");
        }
    }
}
