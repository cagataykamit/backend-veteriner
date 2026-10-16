using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerPortalAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "LegalAcceptances",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "LegalAcceptances",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerAccountId",
                table: "LegalAcceptances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OwnerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailedSignInCount = table.Column<int>(type: "int", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OwnerPortalInvites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedOwnerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerPortalInvites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerPortalInvites_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnerAccountClientLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisclosureAcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerAccountClientLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerAccountClientLinks_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OwnerAccountClientLinks_OwnerAccounts_OwnerAccountId",
                        column: x => x.OwnerAccountId,
                        principalTable: "OwnerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnerPortalCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerPortalCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerPortalCodes_OwnerAccounts_OwnerAccountId",
                        column: x => x.OwnerAccountId,
                        principalTable: "OwnerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OwnerPortalRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokeReason = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReplacedByTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerPortalRefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerPortalRefreshTokens_OwnerAccounts_OwnerAccountId",
                        column: x => x.OwnerAccountId,
                        principalTable: "OwnerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalAcceptances_OwnerAccountId_DocumentType",
                table: "LegalAcceptances",
                columns: new[] { "OwnerAccountId", "DocumentType" },
                filter: "[OwnerAccountId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LegalAcceptances_SingleAcceptor",
                table: "LegalAcceptances",
                sql: "([UserId] IS NOT NULL AND [TenantId] IS NOT NULL AND [OwnerAccountId] IS NULL) OR ([UserId] IS NULL AND [TenantId] IS NULL AND [OwnerAccountId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerAccountClientLinks_ClientId",
                table: "OwnerAccountClientLinks",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerAccountClientLinks_OwnerAccountId",
                table: "OwnerAccountClientLinks",
                column: "OwnerAccountId");

            migrationBuilder.CreateIndex(
                name: "UX_OwnerAccountClientLinks_ActivePerClient",
                table: "OwnerAccountClientLinks",
                columns: new[] { "TenantId", "ClientId" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerAccounts_Email",
                table: "OwnerAccounts",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPortalCodes_OwnerAccountId_Purpose_CreatedAtUtc",
                table: "OwnerPortalCodes",
                columns: new[] { "OwnerAccountId", "Purpose", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPortalInvites_ClientId",
                table: "OwnerPortalInvites",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPortalInvites_TenantId_ClientId_Status",
                table: "OwnerPortalInvites",
                columns: new[] { "TenantId", "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPortalInvites_TokenHash",
                table: "OwnerPortalInvites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPortalRefreshTokens_OwnerAccountId",
                table: "OwnerPortalRefreshTokens",
                column: "OwnerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerPortalRefreshTokens_TokenHash",
                table: "OwnerPortalRefreshTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OwnerAccountClientLinks");

            migrationBuilder.DropTable(
                name: "OwnerPortalCodes");

            migrationBuilder.DropTable(
                name: "OwnerPortalInvites");

            migrationBuilder.DropTable(
                name: "OwnerPortalRefreshTokens");

            migrationBuilder.DropTable(
                name: "OwnerAccounts");

            migrationBuilder.DropIndex(
                name: "IX_LegalAcceptances_OwnerAccountId_DocumentType",
                table: "LegalAcceptances");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LegalAcceptances_SingleAcceptor",
                table: "LegalAcceptances");

            // Portal hesabı kabulleri personel sütunlarını boş bırakır; sütunlar zorunluya dönmeden önce kaldırılır.
            migrationBuilder.Sql("DELETE FROM [LegalAcceptances] WHERE [OwnerAccountId] IS NOT NULL;");

            migrationBuilder.DropColumn(
                name: "OwnerAccountId",
                table: "LegalAcceptances");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "LegalAcceptances",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "LegalAcceptances",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
