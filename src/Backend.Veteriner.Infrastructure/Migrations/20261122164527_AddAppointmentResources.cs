using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EquipmentResourceId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoomResourceId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentResources_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_EquipmentResourceId",
                table: "Appointments",
                column: "EquipmentResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_RoomResourceId",
                table: "Appointments",
                column: "RoomResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_EquipmentResourceId_ScheduledAtUtc",
                table: "Appointments",
                columns: new[] { "TenantId", "EquipmentResourceId", "ScheduledAtUtc" },
                filter: "[EquipmentResourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_RoomResourceId_ScheduledAtUtc",
                table: "Appointments",
                columns: new[] { "TenantId", "RoomResourceId", "ScheduledAtUtc" },
                filter: "[RoomResourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResources_ClinicId",
                table: "AppointmentResources",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResources_TenantId_ClinicId",
                table: "AppointmentResources",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentResources_TenantId_ClinicId_Name",
                table: "AppointmentResources",
                columns: new[] { "TenantId", "ClinicId", "Name" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AppointmentResources_EquipmentResourceId",
                table: "Appointments",
                column: "EquipmentResourceId",
                principalTable: "AppointmentResources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AppointmentResources_RoomResourceId",
                table: "Appointments",
                column: "RoomResourceId",
                principalTable: "AppointmentResources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AppointmentResources_EquipmentResourceId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AppointmentResources_RoomResourceId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "AppointmentResources");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_EquipmentResourceId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_RoomResourceId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_EquipmentResourceId_ScheduledAtUtc",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_RoomResourceId_ScheduledAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "EquipmentResourceId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "RoomResourceId",
                table: "Appointments");
        }
    }
}
