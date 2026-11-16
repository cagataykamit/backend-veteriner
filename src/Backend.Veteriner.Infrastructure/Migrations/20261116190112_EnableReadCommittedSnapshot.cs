using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <summary>
    /// READ_COMMITTED_SNAPSHOT (RCSI) açılır: READ COMMITTED okumaları yazanı beklemez, son onaylanmış sürümü okur.
    /// Sebep: canlıda içe aktarma geri alması, outbox ölçüm okuması ile projeksiyon tüketicisinin UPDATE'i arasında
    /// kilitlenme kurbanı (1205) olup yarıda kaldı (2026-11-16). Yalnız <c>CURRENT</c> veritabanına uygulanır; ad yazılmaz
    /// (aynı sunucudaki başka veritabanlarına dokunulmaz). Zaten açıksa/kapalıysa bir şey yapmaz.
    /// <para>
    /// <c>ALTER DATABASE</c> işlem içinde çalışamaz (<c>suppressTransaction</c>); <c>WITH ROLLBACK IMMEDIATE</c> veritabanındaki
    /// diğer bağlantıların açık işlemlerini geri alır: canlıda birkaç saniyelik kesinti, gece yayınıyla uygulanır. Migrator
    /// kullanıcısının <c>ALTER DATABASE</c> yetkisi gerekir.
    /// </para>
    /// </summary>
    public partial class EnableReadCommittedSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_read_committed_snapshot_on = 0)
                    EXEC (N'ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE');
                """,
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM sys.databases WHERE database_id = DB_ID() AND is_read_committed_snapshot_on = 1)
                    EXEC (N'ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE');
                """,
                suppressTransaction: true);
        }
    }
}
