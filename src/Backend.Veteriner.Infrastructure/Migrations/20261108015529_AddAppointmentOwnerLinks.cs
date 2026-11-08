using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentOwnerLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OwnerCancelLinkEnabled",
                table: "ClinicAppointmentSettings",
                type: "bit",
                nullable: false,
                // TIER-019: mevcut kliniklerde iptal bağlantısı açık başlar (ClinicAppointmentSettings.DefaultOwnerCancelLinkEnabled).
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OwnerConfirmedAtUtc",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentOwnerLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedAction = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentOwnerLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentOwnerLinks_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentOwnerLinks_AppointmentId",
                table: "AppointmentOwnerLinks",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentOwnerLinks_TenantId_AppointmentId",
                table: "AppointmentOwnerLinks",
                columns: new[] { "TenantId", "AppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentOwnerLinks_TokenHash",
                table: "AppointmentOwnerLinks",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentOwnerLinks");

            migrationBuilder.DropColumn(
                name: "OwnerCancelLinkEnabled",
                table: "ClinicAppointmentSettings");

            migrationBuilder.DropColumn(
                name: "OwnerConfirmedAtUtc",
                table: "Appointments");
        }
    }
}
