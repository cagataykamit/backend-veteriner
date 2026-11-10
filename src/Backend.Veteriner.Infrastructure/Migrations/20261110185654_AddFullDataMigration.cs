using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFullDataMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_Created",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_Error",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_Failed",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_Matched",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_Skipped",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_Total",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_Valid",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_Warning",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_WillCreate",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_WillMatch",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AccountEntryCounts_WillSkip",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_Created",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_Error",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_Failed",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_Matched",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_Skipped",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_Total",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_Valid",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_Warning",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_WillCreate",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_WillMatch",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentCounts_WillSkip",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_Created",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_Error",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_Failed",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_Matched",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_Skipped",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_Total",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_Valid",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_Warning",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_WillCreate",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_WillMatch",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalRecordCounts_WillSkip",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_Created",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_Error",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_Failed",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_Matched",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_Skipped",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_Total",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_Valid",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_Warning",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_WillCreate",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_WillMatch",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProductCounts_WillSkip",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Charges",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportedAccountEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedAccountEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportedAccountEntries_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportedClinicalRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Complaint = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    History = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Findings = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    Diagnosis = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Treatment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Prescription = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    LabResults = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    VeterinarianName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    TemperatureC = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedClinicalRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportedClinicalRecords_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_ImportBatchId",
                table: "Charges",
                column: "ImportBatchId",
                filter: "[ImportBatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ImportBatchId",
                table: "Appointments",
                column: "ImportBatchId",
                filter: "[ImportBatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ImportedAccountEntries_ImportBatchId",
                table: "ImportedAccountEntries",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportedAccountEntries_TenantId_ClientId_OccurredOn",
                table: "ImportedAccountEntries",
                columns: new[] { "TenantId", "ClientId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedClinicalRecords_ImportBatchId",
                table: "ImportedClinicalRecords",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportedClinicalRecords_TenantId_ClientId",
                table: "ImportedClinicalRecords",
                columns: new[] { "TenantId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedClinicalRecords_TenantId_PetId_RecordedOn",
                table: "ImportedClinicalRecords",
                columns: new[] { "TenantId", "PetId", "RecordedOn" });

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ImportBatches_ImportBatchId",
                table: "Appointments",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_ImportBatches_ImportBatchId",
                table: "Charges",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ImportBatches_ImportBatchId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Charges_ImportBatches_ImportBatchId",
                table: "Charges");

            migrationBuilder.DropTable(
                name: "ImportedAccountEntries");

            migrationBuilder.DropTable(
                name: "ImportedClinicalRecords");

            migrationBuilder.DropIndex(
                name: "IX_Charges_ImportBatchId",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ImportBatchId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_Created",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_Error",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_Failed",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_Matched",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_Skipped",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_Total",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_Valid",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_Warning",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_WillCreate",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_WillMatch",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AccountEntryCounts_WillSkip",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_Created",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_Error",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_Failed",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_Matched",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_Skipped",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_Total",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_Valid",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_Warning",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_WillCreate",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_WillMatch",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AppointmentCounts_WillSkip",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_Created",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_Error",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_Failed",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_Matched",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_Skipped",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_Total",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_Valid",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_Warning",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_WillCreate",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_WillMatch",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ClinicalRecordCounts_WillSkip",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_Created",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_Error",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_Failed",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_Matched",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_Skipped",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_Total",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_Valid",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_Warning",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_WillCreate",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_WillMatch",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ProductCounts_WillSkip",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Appointments");
        }
    }
}
