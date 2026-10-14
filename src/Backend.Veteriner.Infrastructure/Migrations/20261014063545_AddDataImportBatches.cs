using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Vaccinations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MappingJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OptionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ValidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CommittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RolledBackAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RemindersReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayloadPurgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CustomerCounts_Created = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_Error = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_Failed = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_Matched = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_Skipped = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_Total = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_Valid = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_Warning = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_WillCreate = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_WillMatch = table.Column<int>(type: "int", nullable: false),
                    CustomerCounts_WillSkip = table.Column<int>(type: "int", nullable: false),
                    PetCounts_Created = table.Column<int>(type: "int", nullable: false),
                    PetCounts_Error = table.Column<int>(type: "int", nullable: false),
                    PetCounts_Failed = table.Column<int>(type: "int", nullable: false),
                    PetCounts_Matched = table.Column<int>(type: "int", nullable: false),
                    PetCounts_Skipped = table.Column<int>(type: "int", nullable: false),
                    PetCounts_Total = table.Column<int>(type: "int", nullable: false),
                    PetCounts_Valid = table.Column<int>(type: "int", nullable: false),
                    PetCounts_Warning = table.Column<int>(type: "int", nullable: false),
                    PetCounts_WillCreate = table.Column<int>(type: "int", nullable: false),
                    PetCounts_WillMatch = table.Column<int>(type: "int", nullable: false),
                    PetCounts_WillSkip = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_Created = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_Error = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_Failed = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_Matched = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_Skipped = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_Total = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_Valid = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_Warning = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_WillCreate = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_WillMatch = table.Column<int>(type: "int", nullable: false),
                    VaccinationCounts_WillSkip = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatchRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Table = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NormalizedJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidationStatus = table.Column<int>(type: "int", nullable: false),
                    IssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApplyStatus = table.Column<int>(type: "int", nullable: false),
                    TargetEntityType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TargetEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Matched = table.Column<bool>(type: "bit", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatchRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatchRows_ImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vaccinations_ImportBatchId",
                table: "Vaccinations",
                column: "ImportBatchId",
                filter: "[ImportBatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_TenantId_ClinicId",
                table: "ImportBatches",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_TenantId_CreatedAtUtc",
                table: "ImportBatches",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_TenantId_Status",
                table: "ImportBatches",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatches_TenantId_ActiveJob",
                table: "ImportBatches",
                column: "TenantId",
                unique: true,
                filter: "[Status] IN (3, 6, 9)");

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatches_TenantId_ContentHash",
                table: "ImportBatches",
                columns: new[] { "TenantId", "ContentHash" },
                unique: true,
                filter: "[ContentHash] IS NOT NULL AND [Status] <> 8 AND [Status] <> 10 AND [Status] <> 13 AND [Status] <> 14");

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatches_TenantId_RequestId",
                table: "ImportBatches",
                columns: new[] { "TenantId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchRows_TargetEntityId",
                table: "ImportBatchRows",
                column: "TargetEntityId",
                filter: "[TargetEntityId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatchRows_BatchId_Table_RowNumber",
                table: "ImportBatchRows",
                columns: new[] { "BatchId", "Table", "RowNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Vaccinations_ImportBatches_ImportBatchId",
                table: "Vaccinations",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vaccinations_ImportBatches_ImportBatchId",
                table: "Vaccinations");

            migrationBuilder.DropTable(
                name: "ImportBatchRows");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_Vaccinations_ImportBatchId",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Vaccinations");
        }
    }
}
