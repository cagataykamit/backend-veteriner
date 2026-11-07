using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicSites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    DraftConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DraftUpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HasUnpublishedChanges = table.Column<bool>(type: "bit", nullable: false),
                    PublishedVersionNumber = table.Column<int>(type: "int", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsTemporarilyClosed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicSites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicSites_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicSiteAddresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicSiteAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicSiteAddresses_ClinicSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "ClinicSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicSiteImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicSiteImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicSiteImages_ClinicSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "ClinicSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicSiteVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ConfigJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferencedImageIds = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicSiteVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicSiteVersions_ClinicSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "ClinicSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSiteAddresses_SiteId",
                table: "ClinicSiteAddresses",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSiteAddresses_Slug",
                table: "ClinicSiteAddresses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSiteAddresses_TenantId_SiteId",
                table: "ClinicSiteAddresses",
                columns: new[] { "TenantId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSiteImages_SiteId",
                table: "ClinicSiteImages",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSiteImages_StorageKey",
                table: "ClinicSiteImages",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSiteImages_TenantId_SiteId",
                table: "ClinicSiteImages",
                columns: new[] { "TenantId", "SiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSites_ClinicId",
                table: "ClinicSites",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSites_Slug",
                table: "ClinicSites",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSites_TenantId_ClinicId",
                table: "ClinicSites",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSiteVersions_SiteId",
                table: "ClinicSiteVersions",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSiteVersions_TenantId_SiteId_VersionNumber",
                table: "ClinicSiteVersions",
                columns: new[] { "TenantId", "SiteId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicSiteAddresses");

            migrationBuilder.DropTable(
                name: "ClinicSiteImages");

            migrationBuilder.DropTable(
                name: "ClinicSiteVersions");

            migrationBuilder.DropTable(
                name: "ClinicSites");
        }
    }
}
