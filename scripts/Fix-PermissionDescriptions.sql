-- Permission tablosundaki Description ve [Group] alanlarını doğru Türkçe (UTF-8/NVARCHAR) ile günceller.
-- Çalıştırmak: SQL Server Management Studio veya sqlcmd ile veritabanınıza bağlanıp bu scripti çalıştırın.
-- Örnek: sqlcmd -S localhost -d YourDatabase -i scripts/Fix-PermissionDescriptions.sql

SET NOCOUNT ON;

UPDATE Permissions SET Description = N'Permission kayıtlarını görüntüleme yetkisi', [Group] = N'Permissions' WHERE Code = N'Permissions.Read';
UPDATE Permissions SET Description = N'Permission kayıtlarını oluşturma ve güncelleme yetkisi', [Group] = N'Permissions' WHERE Code = N'Permissions.Write';

UPDATE Permissions SET Description = N'Kullanıcıları görüntüleme yetkisi', [Group] = N'Users' WHERE Code = N'Users.Read';
UPDATE Permissions SET Description = N'Kullanıcı oluşturma ve güncelleme yetkisi', [Group] = N'Users' WHERE Code = N'Users.Write';

UPDATE Permissions SET Description = N'Rol ve claim kayıtlarını görüntüleme yetkisi', [Group] = N'Roles' WHERE Code = N'Roles.Read';
UPDATE Permissions SET Description = N'Rol ve claim atama/değiştirme yetkisi', [Group] = N'Roles' WHERE Code = N'Roles.Write';

UPDATE Permissions SET Description = N'Outbox kayıtlarını görüntüleme yetkisi', [Group] = N'Outbox' WHERE Code = N'Outbox.Read';
UPDATE Permissions SET Description = N'Outbox üzerinde yönetim işlemleri yapma yetkisi', [Group] = N'Outbox' WHERE Code = N'Outbox.Write';

UPDATE Permissions SET Description = N'Tanılama ve yönetim kontrollerine erişim yetkisi', [Group] = N'Diagnostics' WHERE Code = N'Admin.Diagnostics';

UPDATE Permissions SET Description = N'Test amaçlı özellikleri çalıştırma yetkisi', [Group] = N'Test' WHERE Code = N'Test.Feature.Run';
UPDATE Permissions SET Description = N'Forbidden test endpoint', [Group] = N'Test' WHERE Code = N'Test.Forbidden.Probe';

UPDATE Permissions SET Description = N'Pozisyonları listeleme ve detay görüntüleme yetkisi', [Group] = N'Organization' WHERE Code = N'organization.positions.read';
UPDATE Permissions SET Description = N'Pozisyon oluşturma yetkisi', [Group] = N'Organization' WHERE Code = N'organization.positions.create';
UPDATE Permissions SET Description = N'Organizasyon birimlerini listeleme ve detay görüntüleme yetkisi', [Group] = N'Organization' WHERE Code = N'organization.units.read';
UPDATE Permissions SET Description = N'Organizasyon birimi oluşturma yetkisi', [Group] = N'Organization' WHERE Code = N'organization.units.create';
UPDATE Permissions SET Description = N'Çalışanları listeleme ve detay görüntüleme yetkisi', [Group] = N'Organization' WHERE Code = N'organization.employees.read';
UPDATE Permissions SET Description = N'Çalışan oluşturma yetkisi', [Group] = N'Organization' WHERE Code = N'organization.employees.create';

PRINT N'Permission açıklamaları güncellendi.';
