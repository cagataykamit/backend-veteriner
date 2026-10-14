using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientKvkkConsentAndClinicKvkkFormTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "KvkkConsentRecordedByUserId",
                table: "Clients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "KvkkConsentRecordedOn",
                table: "Clients",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KvkkConsentStatus",
                table: "Clients",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Clients",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "ClinicKvkkFormTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicKvkkFormTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicKvkkFormTemplates_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicKvkkFormTemplates_ClinicId",
                table: "ClinicKvkkFormTemplates",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicKvkkFormTemplates_TenantId_ClinicId",
                table: "ClinicKvkkFormTemplates",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicKvkkFormTemplates");

            migrationBuilder.DropColumn(
                name: "KvkkConsentRecordedByUserId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "KvkkConsentRecordedOn",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "KvkkConsentStatus",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Clients");
        }
    }
}
