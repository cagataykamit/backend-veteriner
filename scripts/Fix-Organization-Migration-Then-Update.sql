-- =============================================================================
-- Neden: Add_Organization_Module migration daha önce boş Up() ile "uygulandı";
--       __EFMigrationsHistory'e yazıldı ama Positions/OrganizationUnits/Employees
--       tabloları oluşmadı. EF bir kez uyguladığı migration'ı tekrar çalıştırmaz.
-- Ne yapıyor: Bu migration kaydını history'den siler. Sonra siz:
--   dotnet ef database update -p src/Backend.Veteriner.Infrastructure -s src/Backend.Veteriner.Api
-- çalıştırdığınızda EF migration'ı yeniden uygular ve tabloları oluşturur.
-- =============================================================================
-- Kullanım: API'nin kullandığı veritabanında (DefaultConnection/SqlServer) çalıştırın.
-- =============================================================================

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20260314175608_Add_Organization_Module';

-- Silindiğini doğrulamak için (isteğe bağlı):
-- SELECT * FROM [__EFMigrationsHistory] ORDER BY [MigrationId];
