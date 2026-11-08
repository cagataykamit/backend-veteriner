using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicVetFinderListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicVetFinderListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsListed = table.Column<bool>(type: "bit", nullable: false),
                    PublicKey = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ServiceKeys = table.Column<string>(type: "varchar(400)", unicode: false, maxLength: 400, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicVetFinderListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicVetFinderListings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicVetFinderListings_ClinicId",
                table: "ClinicVetFinderListings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicVetFinderListings_IsListed",
                table: "ClinicVetFinderListings",
                column: "IsListed");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicVetFinderListings_PublicKey",
                table: "ClinicVetFinderListings",
                column: "PublicKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicVetFinderListings_TenantId_ClinicId",
                table: "ClinicVetFinderListings",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicVetFinderListings");
        }
    }
}
