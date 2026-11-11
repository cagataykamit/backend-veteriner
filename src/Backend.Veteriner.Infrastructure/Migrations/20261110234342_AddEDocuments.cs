using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EDocumentId",
                table: "Charges",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EDocumentConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AccountId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    AccountName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ProtectedCredentials = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    ConnectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastVerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastVerificationOk = table.Column<bool>(type: "bit", nullable: true),
                    LastVerificationMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DisconnectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EDocumentConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EDocumentConnections_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SaleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VisitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    ProviderCode = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    ExternalReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyerKind = table.Column<int>(type: "int", nullable: false),
                    BuyerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BuyerIdentityNumber = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    BuyerIsUnknown = table.Column<bool>(type: "bit", nullable: false),
                    BuyerTaxOffice = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BuyerProvince = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    BuyerDistrict = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    BuyerAddressLine = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyerEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WithholdingRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ProviderInvoiceId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ProviderDocumentId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ProviderJobId = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    DocumentNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentUuid = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    ProviderTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastErrorKind = table.Column<int>(type: "int", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastProviderErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    CheckCount = table.Column<int>(type: "int", nullable: false),
                    NextCheckAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelRequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DiscardReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DiscardedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscardedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EDocuments", x => x.Id);
                    table.CheckConstraint("CK_EDocuments_TotalAmount_Positive", "[TotalAmount] > 0");
                    table.ForeignKey(
                        name: "FK_EDocuments_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EDocuments_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EDocumentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    LineKind = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EDocumentLines", x => x.Id);
                    table.CheckConstraint("CK_EDocumentLines_GrossAmount_Positive", "[GrossAmount] > 0");
                    table.ForeignKey(
                        name: "FK_EDocumentLines_EDocuments_EDocumentId",
                        column: x => x.EDocumentId,
                        principalTable: "EDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_EDocumentId",
                table: "Charges",
                column: "EDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_TenantId_EDocumentId",
                table: "Charges",
                columns: new[] { "TenantId", "EDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_EDocumentConnections_ClinicId",
                table: "EDocumentConnections",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_EDocumentConnections_TenantId_ClinicId",
                table: "EDocumentConnections",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EDocumentLines_ChargeId",
                table: "EDocumentLines",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_EDocumentLines_EDocumentId",
                table: "EDocumentLines",
                column: "EDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_EDocuments_ClientId",
                table: "EDocuments",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_EDocuments_ClinicId",
                table: "EDocuments",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_EDocuments_ExternalReference",
                table: "EDocuments",
                column: "ExternalReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EDocuments_Status_NextCheckAtUtc",
                table: "EDocuments",
                columns: new[] { "Status", "NextCheckAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EDocuments_TenantId_ClientId",
                table: "EDocuments",
                columns: new[] { "TenantId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_EDocuments_TenantId_ClinicId_CreatedAtUtc",
                table: "EDocuments",
                columns: new[] { "TenantId", "ClinicId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_EDocuments_TenantId_RequestId",
                table: "EDocuments",
                columns: new[] { "TenantId", "RequestId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Charges_EDocuments_EDocumentId",
                table: "Charges",
                column: "EDocumentId",
                principalTable: "EDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Charges_EDocuments_EDocumentId",
                table: "Charges");

            migrationBuilder.DropTable(
                name: "EDocumentConnections");

            migrationBuilder.DropTable(
                name: "EDocumentLines");

            migrationBuilder.DropTable(
                name: "EDocuments");

            migrationBuilder.DropIndex(
                name: "IX_Charges_EDocumentId",
                table: "Charges");

            migrationBuilder.DropIndex(
                name: "IX_Charges_TenantId_EDocumentId",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "EDocumentId",
                table: "Charges");
        }
    }
}
