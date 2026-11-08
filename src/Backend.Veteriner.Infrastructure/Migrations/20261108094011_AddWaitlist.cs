using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WaitlistAutoOfferEnabled",
                table: "ClinicAppointmentSettings",
                type: "bit",
                nullable: false,
                // TIER-018: mevcut kliniklerde otomatik teklif açık başlar (ClinicAppointmentSettings.DefaultWaitlistAutoOfferEnabled).
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WaitlistOfferId",
                table: "AppointmentOwnerLinks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreferredVeterinarianUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppointmentType = table.Column<int>(type: "int", nullable: false),
                    ReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: true),
                    EarliestDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LatestDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreferredDays = table.Column<int>(type: "int", nullable: false),
                    PreferredFromTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    PreferredToTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SortKey = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CloseReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BookedAppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Pets_PetId",
                        column: x => x.PetId,
                        principalTable: "Pets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WaitlistEntryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WaitlistEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntryEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitlistEntryEvents_WaitlistEntries_WaitlistEntryId",
                        column: x => x.WaitlistEntryId,
                        principalTable: "WaitlistEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WaitlistOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WaitlistEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    VeterinarianUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsAutomatic = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitlistOffers_Appointments_SourceAppointmentId",
                        column: x => x.SourceAppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WaitlistOffers_WaitlistEntries_WaitlistEntryId",
                        column: x => x.WaitlistEntryId,
                        principalTable: "WaitlistEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentOwnerLinks_WaitlistOfferId",
                table: "AppointmentOwnerLinks",
                column: "WaitlistOfferId",
                filter: "[WaitlistOfferId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_ClientId",
                table: "WaitlistEntries",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_ClinicId",
                table: "WaitlistEntries",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_PetId",
                table: "WaitlistEntries",
                column: "PetId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_TenantId_ClinicId_Status_SortKey",
                table: "WaitlistEntries",
                columns: new[] { "TenantId", "ClinicId", "Status", "SortKey" });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_TenantId_PetId",
                table: "WaitlistEntries",
                columns: new[] { "TenantId", "PetId" });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntryEvents_TenantId_WaitlistEntryId_OccurredAtUtc",
                table: "WaitlistEntryEvents",
                columns: new[] { "TenantId", "WaitlistEntryId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntryEvents_WaitlistEntryId",
                table: "WaitlistEntryEvents",
                column: "WaitlistEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistOffers_TenantId_SourceAppointmentId",
                table: "WaitlistOffers",
                columns: new[] { "TenantId", "SourceAppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistOffers_TenantId_Status_ExpiresAtUtc",
                table: "WaitlistOffers",
                columns: new[] { "TenantId", "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_WaitlistOffers_Entry_Open",
                table: "WaitlistOffers",
                column: "WaitlistEntryId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_WaitlistOffers_Slot_Accepted",
                table: "WaitlistOffers",
                column: "SourceAppointmentId",
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_WaitlistOffers_Slot_Open",
                table: "WaitlistOffers",
                column: "SourceAppointmentId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_AppointmentOwnerLinks_WaitlistOffers_WaitlistOfferId",
                table: "AppointmentOwnerLinks",
                column: "WaitlistOfferId",
                principalTable: "WaitlistOffers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppointmentOwnerLinks_WaitlistOffers_WaitlistOfferId",
                table: "AppointmentOwnerLinks");

            migrationBuilder.DropTable(
                name: "WaitlistEntryEvents");

            migrationBuilder.DropTable(
                name: "WaitlistOffers");

            migrationBuilder.DropTable(
                name: "WaitlistEntries");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentOwnerLinks_WaitlistOfferId",
                table: "AppointmentOwnerLinks");

            migrationBuilder.DropColumn(
                name: "WaitlistAutoOfferEnabled",
                table: "ClinicAppointmentSettings");

            migrationBuilder.DropColumn(
                name: "WaitlistOfferId",
                table: "AppointmentOwnerLinks");
        }
    }
}
