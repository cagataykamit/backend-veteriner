using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHospitalizationWardsAndRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RoomId",
                table: "Hospitalizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HospitalizationWards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HospitalizationWards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HospitalizationWards_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HospitalizationRooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WardId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HospitalizationRooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HospitalizationRooms_HospitalizationWards_WardId",
                        column: x => x.WardId,
                        principalTable: "HospitalizationWards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_RoomId",
                table: "Hospitalizations",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Hospitalizations_TenantId_RoomId",
                table: "Hospitalizations",
                columns: new[] { "TenantId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_HospitalizationRooms_TenantId_ClinicId",
                table: "HospitalizationRooms",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_HospitalizationRooms_TenantId_WardId",
                table: "HospitalizationRooms",
                columns: new[] { "TenantId", "WardId" });

            migrationBuilder.CreateIndex(
                name: "IX_HospitalizationRooms_TenantId_WardId_Name",
                table: "HospitalizationRooms",
                columns: new[] { "TenantId", "WardId", "Name" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_HospitalizationRooms_WardId",
                table: "HospitalizationRooms",
                column: "WardId");

            migrationBuilder.CreateIndex(
                name: "IX_HospitalizationWards_ClinicId",
                table: "HospitalizationWards",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_HospitalizationWards_TenantId_ClinicId",
                table: "HospitalizationWards",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_HospitalizationWards_TenantId_ClinicId_Name",
                table: "HospitalizationWards",
                columns: new[] { "TenantId", "ClinicId", "Name" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_Hospitalizations_HospitalizationRooms_RoomId",
                table: "Hospitalizations",
                column: "RoomId",
                principalTable: "HospitalizationRooms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Hospitalizations_HospitalizationRooms_RoomId",
                table: "Hospitalizations");

            migrationBuilder.DropTable(
                name: "HospitalizationRooms");

            migrationBuilder.DropTable(
                name: "HospitalizationWards");

            migrationBuilder.DropIndex(
                name: "IX_Hospitalizations_RoomId",
                table: "Hospitalizations");

            migrationBuilder.DropIndex(
                name: "IX_Hospitalizations_TenantId_RoomId",
                table: "Hospitalizations");

            migrationBuilder.DropColumn(
                name: "RoomId",
                table: "Hospitalizations");
        }
    }
}
