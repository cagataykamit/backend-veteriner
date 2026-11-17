using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportSourceLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportSourceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceProfile = table.Column<int>(type: "int", nullable: false),
                    Entity = table.Column<int>(type: "int", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportSourceLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ImportSourceLinks_Key",
                table: "ImportSourceLinks",
                columns: new[] { "TenantId", "ClinicId", "SourceProfile", "Entity", "SourceKey" },
                unique: true);

            // Geriye dönük doldurma (ADR-051): satır verisi temizlenmemiş, program profilli (Generic = 0 değil), klinikli partilerin
            // oluşan / eşleşen / geri almada kalan (Created = 1, Matched = 2, Kept = 6) müşteri (tablo 1 → Client = 1) ve hasta
            // (tablo 2 → Pet = 2) satırları. Kural ImportSourceLink.StableKey ile aynı: '#' içeren ve 100 karakteri aşan numara yok.
            // Hedef hâlâ duruyor olmalı; aynı anahtar birden çok partide varsa en son uygulanan kazanır.
            migrationBuilder.Sql(
                """
                ;WITH Candidates AS (
                    SELECT b.TenantId, b.ClinicId, b.SourceProfile, r.[Table] AS Entity,
                           LTRIM(RTRIM(JSON_VALUE(r.NormalizedJson, '$.reference'))) AS SourceKey,
                           r.TargetEntityId AS TargetId, b.Id AS BatchId, r.AppliedAtUtc
                    FROM ImportBatchRows r
                    JOIN ImportBatches b ON b.Id = r.BatchId
                    WHERE b.ClinicId IS NOT NULL
                      AND b.SourceProfile <> 0
                      AND r.[Table] IN (1, 2)
                      AND r.ApplyStatus IN (1, 2, 6)
                      AND r.TargetEntityId IS NOT NULL
                      AND r.NormalizedJson IS NOT NULL
                      AND ISJSON(r.NormalizedJson) = 1
                ),
                Valid AS (
                    SELECT c.*, ROW_NUMBER() OVER (
                        PARTITION BY c.TenantId, c.ClinicId, c.SourceProfile, c.Entity, c.SourceKey
                        ORDER BY c.AppliedAtUtc DESC, c.BatchId) AS Rn
                    FROM Candidates c
                    WHERE c.SourceKey IS NOT NULL AND c.SourceKey <> N'' AND LEN(c.SourceKey) <= 100
                      AND CHARINDEX(N'#', c.SourceKey) = 0
                      AND ((c.Entity = 1 AND EXISTS (SELECT 1 FROM Clients x WHERE x.Id = c.TargetId AND x.TenantId = c.TenantId))
                        OR (c.Entity = 2 AND EXISTS (SELECT 1 FROM Pets x WHERE x.Id = c.TargetId AND x.TenantId = c.TenantId)))
                )
                INSERT INTO ImportSourceLinks (Id, TenantId, ClinicId, SourceProfile, Entity, SourceKey, TargetId, BatchId, CreatedAtUtc, UpdatedAtUtc)
                SELECT NEWID(), v.TenantId, v.ClinicId, v.SourceProfile, v.Entity, v.SourceKey, v.TargetId, v.BatchId, SYSUTCDATETIME(), NULL
                FROM Valid v
                WHERE v.Rn = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportSourceLinks");
        }
    }
}
