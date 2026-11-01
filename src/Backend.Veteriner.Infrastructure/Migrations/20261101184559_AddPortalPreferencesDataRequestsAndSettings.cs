using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalPreferencesDataRequestsAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PushEnabled",
                table: "OwnerAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MarketingConsentAtUtc",
                table: "OwnerAccountClientLinks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicPortalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicPortalSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicPortalSettings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OwnerDataRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HandledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HandledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MutationSequence = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OwnerDataRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OwnerDataRequests_OwnerAccountClientLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "OwnerAccountClientLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicPortalSettings_ClinicId",
                table: "ClinicPortalSettings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicPortalSettings_TenantId_ClinicId",
                table: "ClinicPortalSettings",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnerDataRequests_LinkId",
                table: "OwnerDataRequests",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_OwnerDataRequests_TenantId_ClinicId_Status",
                table: "OwnerDataRequests",
                columns: new[] { "TenantId", "ClinicId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OwnerDataRequests_TenantId_LinkId",
                table: "OwnerDataRequests",
                columns: new[] { "TenantId", "LinkId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicPortalSettings");

            migrationBuilder.DropTable(
                name: "OwnerDataRequests");

            migrationBuilder.DropColumn(
                name: "PushEnabled",
                table: "OwnerAccounts");

            migrationBuilder.DropColumn(
                name: "MarketingConsentAtUtc",
                table: "OwnerAccountClientLinks");
        }
    }
}
