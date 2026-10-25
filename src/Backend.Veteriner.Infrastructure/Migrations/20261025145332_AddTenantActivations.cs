using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantActivations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantActivations",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExampleClinicLoadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstActionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstActionKind = table.Column<int>(type: "int", nullable: true),
                    FirstActionUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantActivations", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TenantActivations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_TrialStartsAtUtc",
                table: "TenantSubscriptions",
                column: "TrialStartsAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantActivations");

            migrationBuilder.DropIndex(
                name: "IX_TenantSubscriptions_TrialStartsAtUtc",
                table: "TenantSubscriptions");
        }
    }
}
