using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineAppointmentRequestsAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReasonId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VeterinarianUserId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppointmentReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefaultDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    IsOnlineBookable = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentReasons_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentRequestEmailVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentRequestEmailVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentRequestEmailVerifications_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicAppointmentRequestEmailTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicAppointmentRequestEmailTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicAppointmentRequestEmailTemplates_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicOnlineBookingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MinLeadMinutes = table.Column<int>(type: "int", nullable: false),
                    MaxAdvanceDays = table.Column<int>(type: "int", nullable: false),
                    BookingCode = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    BookingCodeGeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicOnlineBookingSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicOnlineBookingSettings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetType = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentReasonVeterinarians",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReasonVeterinarians", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentReasonVeterinarians_AppointmentReasons_AppointmentReasonId",
                        column: x => x.AppointmentReasonId,
                        principalTable: "AppointmentReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReasonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReasonName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestedStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    OwnerAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PetName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PetSpecies = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PetBreed = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OwnerNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedStartUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedVeterinarianUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WasRescheduled = table.Column<bool>(type: "bit", nullable: false),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentRequests_AppointmentReasons_ReasonId",
                        column: x => x.ReasonId,
                        principalTable: "AppointmentReasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentRequests_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentRequests_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppointmentRequestStatusChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromStatus = table.Column<int>(type: "int", nullable: true),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AppointmentRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentRequestStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentRequestStatusChanges_AppointmentRequests_AppointmentRequestId",
                        column: x => x.AppointmentRequestId,
                        principalTable: "AppointmentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ReasonId",
                table: "Appointments",
                column: "ReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_VeterinarianUserId_ScheduledAtUtc",
                table: "Appointments",
                columns: new[] { "TenantId", "VeterinarianUserId", "ScheduledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReasons_ClinicId",
                table: "AppointmentReasons",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReasons_TenantId_ClinicId",
                table: "AppointmentReasons",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReasons_TenantId_ClinicId_Name",
                table: "AppointmentReasons",
                columns: new[] { "TenantId", "ClinicId", "Name" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReasonVeterinarians_AppointmentReasonId_UserId",
                table: "AppointmentReasonVeterinarians",
                columns: new[] { "AppointmentReasonId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequestEmailVerifications_ClinicId",
                table: "AppointmentRequestEmailVerifications",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequestEmailVerifications_TenantId_ClinicId_Email_CreatedAtUtc",
                table: "AppointmentRequestEmailVerifications",
                columns: new[] { "TenantId", "ClinicId", "Email", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequests_AppointmentId",
                table: "AppointmentRequests",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequests_ClinicId",
                table: "AppointmentRequests",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequests_ReasonId",
                table: "AppointmentRequests",
                column: "ReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequests_Status_RequestedStartUtc",
                table: "AppointmentRequests",
                columns: new[] { "Status", "RequestedStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequests_TenantId_ClinicId_ContactEmail_Status",
                table: "AppointmentRequests",
                columns: new[] { "TenantId", "ClinicId", "ContactEmail", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequests_TenantId_ClinicId_Status_RequestedStartUtc",
                table: "AppointmentRequests",
                columns: new[] { "TenantId", "ClinicId", "Status", "RequestedStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequests_TenantId_OwnerAccountId",
                table: "AppointmentRequests",
                columns: new[] { "TenantId", "OwnerAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequestStatusChanges_AppointmentRequestId",
                table: "AppointmentRequestStatusChanges",
                column: "AppointmentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicAppointmentRequestEmailTemplates_ClinicId",
                table: "ClinicAppointmentRequestEmailTemplates",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicAppointmentRequestEmailTemplates_TenantId_ClinicId_Kind",
                table: "ClinicAppointmentRequestEmailTemplates",
                columns: new[] { "TenantId", "ClinicId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicOnlineBookingSettings_BookingCode",
                table: "ClinicOnlineBookingSettings",
                column: "BookingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicOnlineBookingSettings_ClinicId",
                table: "ClinicOnlineBookingSettings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicOnlineBookingSettings_TenantId_ClinicId",
                table: "ClinicOnlineBookingSettings",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_TenantId_UserId_CreatedAtUtc",
                table: "UserNotifications",
                columns: new[] { "TenantId", "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_TenantId_UserId_ReadAtUtc",
                table: "UserNotifications",
                columns: new[] { "TenantId", "UserId", "ReadAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_AppointmentReasons_ReasonId",
                table: "Appointments",
                column: "ReasonId",
                principalTable: "AppointmentReasons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_AppointmentReasons_ReasonId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "AppointmentReasonVeterinarians");

            migrationBuilder.DropTable(
                name: "AppointmentRequestEmailVerifications");

            migrationBuilder.DropTable(
                name: "AppointmentRequestStatusChanges");

            migrationBuilder.DropTable(
                name: "ClinicAppointmentRequestEmailTemplates");

            migrationBuilder.DropTable(
                name: "ClinicOnlineBookingSettings");

            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "AppointmentRequests");

            migrationBuilder.DropTable(
                name: "AppointmentReasons");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ReasonId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_TenantId_VeterinarianUserId_ScheduledAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ReasonId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "VeterinarianUserId",
                table: "Appointments");
        }
    }
}
