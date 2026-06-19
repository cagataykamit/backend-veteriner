using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Persistence.Migrations.Query
{
    /// <inheritdoc />
    public partial class AddClientReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientReadModels",
                columns: table => new
                {
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    FullNameNormalized = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PhoneNormalized = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastProjectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientReadModels", x => x.ClientId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientReadModels_TenantId_Email",
                table: "ClientReadModels",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientReadModels_TenantId_FullNameNormalized_ClientId",
                table: "ClientReadModels",
                columns: new[] { "TenantId", "FullNameNormalized", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientReadModels_TenantId_PhoneNormalized",
                table: "ClientReadModels",
                columns: new[] { "TenantId", "PhoneNormalized" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientReadModels");
        }
    }
}
