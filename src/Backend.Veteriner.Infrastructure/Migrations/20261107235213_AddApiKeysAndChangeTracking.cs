using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeysAndChangeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAtUtc",
                table: "Vaccinations",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAtUtc",
                table: "Pets",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAtUtc",
                table: "Clients",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<DateTime>(
                name: "ChangedAtUtc",
                table: "Appointments",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            // Değişiklik zamanı tutan tablolarda eski satırlar bilinen son zamanı alır; diğerlerinde migration anı kalır.
            migrationBuilder.Sql("UPDATE [Clients] SET [ChangedAtUtc] = COALESCE([UpdatedAtUtc], [CreatedAtUtc]);");
            migrationBuilder.Sql("UPDATE [Vaccinations] SET [ChangedAtUtc] = COALESCE([UpdatedAtUtc], [CreatedAtUtc]);");

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Prefix = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    SecretHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    Scopes = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.CheckConstraint("CK_ApiKeys_Revoke_Consistent", "([RevokedAtUtc] IS NULL AND [RevokedByUserId] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_ApiKeys_SecretHash_Length", "DATALENGTH([SecretHash]) = 32");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_TenantId_ChangedAtUtc_Id",
                table: "Vaccinations",
                columns: new[] { "TenantId", "ChangedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Pets_TenantId_ChangedAtUtc_Id",
                table: "Pets",
                columns: new[] { "TenantId", "ChangedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_TenantId_ChangedAtUtc_Id",
                table: "Clients",
                columns: new[] { "TenantId", "ChangedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_ChangedAtUtc_Id",
                table: "Appointments",
                columns: new[] { "TenantId", "ChangedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_Prefix",
                table: "ApiKeys",
                column: "Prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_TenantId_CreatedAtUtc",
                table: "ApiKeys",
                columns: new[] { "TenantId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropIndex(
                name: "IX_Vaccinations_TenantId_ChangedAtUtc_Id",
                table: "Vaccinations");

            migrationBuilder.DropIndex(
                name: "IX_Pets_TenantId_ChangedAtUtc_Id",
                table: "Pets");

            migrationBuilder.DropIndex(
                name: "IX_Clients_TenantId_ChangedAtUtc_Id",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_ChangedAtUtc_Id",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ChangedAtUtc",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "ChangedAtUtc",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "ChangedAtUtc",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ChangedAtUtc",
                table: "Appointments");
        }
    }
}
