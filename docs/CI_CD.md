# CI/CD (Aşama 9 Dilim 2)

Kapsam: yalnız CI + artifact + sürümleme. **Dağıtım (CD) ve hosting yok** (Dilim 3). İş akışları henüz GitHub'da çalışmadı; ilk koşuda ayar gerekebilir.

## İş akışı: `.github/workflows/ci.yml`

Tetik: `main`'e push, `v*` etiketi, pull request, elle (`workflow_dispatch`).

| İş | Runner | Ne yapar |
|---|---|---|
| `build-test` | ubuntu | restore → Release build (uyarılar hata değil; sayı/liste koşu özetinde) → Domain + Application testleri → API ve DbMigrator publish → idempotent migration betikleri |
| `integration-tests` | windows | LocalDB hazırla → entegrasyon testleri (`INTEGRATION_TEST_RUN_ID=ci<run_id>a<deneme>` ile koşuya özgü DB adları, bkz. `IntegrationTestDatabaseNames`) |

**Neden SQL Server kapsayıcısı değil, Windows + LocalDB:** entegrasyon testleri `(localdb)\mssqllocaldb` + Trusted_Connection'a sabit bağlı (`IntegrationTestDatabaseNames.ConnectionString`, `appsettings.IntegrationTests.json`, birçok test fabrikası). Linux'ta SQL Server hizmet kapsayıcısı kullanmak test kodunda bağlantı dizesini ortam değişkeninden okutmayı gerektirir; bu görevde kaynak değiştirilmedi. İleride yapılırsa `integration-tests` ubuntu + `services: mssql` olabilir (daha hızlı).

## Artifact'lar (koşu sayfasından indirilir)

| Ad | İçerik |
|---|---|
| `api-<sürüm>` | `dotnet publish` çıktısı (API) |
| `dbmigrator-<sürüm>` | DbMigrator çıktısı (ilk yönetici/seed komutları için, Dilim 1 sonrası) |
| `migrations-<sürüm>` | `AppDbContext.sql` (Command DB), `QueryDbContext.sql` (Query DB): `dotnet ef migrations script --idempotent`; **hiçbir DB'ye uygulanmaz** |
| `test-results-unit`, `test-results-integration` | `.trx` dosyaları |

Saklama: etiket koşuları 90 gün, diğerleri 30 gün.

## Sürüm çıkarma (SemVer)

Kural: sürüm = git etiketi. `vMAJOR.MINOR.PATCH` veya önsürüm `vMAJOR.MINOR.PATCH-rc.1`. Geçersiz etiketle koşu başarısız olur.

```powershell
git checkout main
git pull
git tag v1.0.0
git push origin v1.0.0
```

Etiket yalnız **testleri geçmiş `main` commit'ine** atılmalı (iş akışı bunu zorlamaz). Derlemeye `Version` ve `InformationalVersion` (`<sürüm>+<commit>`) yazılır; etiketsiz koşu `0.0.0-ci.<koşu no>` alır. Push kullanıcı işidir.

**Bilinen boşluk:** `WebApplicationBuilderExtensions.cs:67` içindeki `serviceVersion = "1.0.0"` sabittir (telemetri); derleme sürümünü okumaz. Sürümün uygulamada görünmesi için küçük bir kod değişikliği gerekir (Dilim 1/3'te yapılmalı). Derleme sürümü şu an yalnız dll `ProductVersion` alanında.

**Bilinen tuzak:** `Api.csproj` yayından `appsettings.Production.json`'u **her zaman dışlar**, `appsettings.Staging.json`'u dahil eder (`docs/deploy/staging-publish-artifact-checklist.md` ise koşullu dışlama anlatır; artık güncel değil). Üretim yapılandırması dosyadan değil, platform ortam değişkenlerinden gelmeli ya da csproj kuralı Dilim 3'te netleştirilmeli.

## Migration betiği: alma ve onaylı uygulama

1. İlgili sürümün koşu sayfasından `migrations-<sürüm>` artifact'ını indirin.
2. Betiği **gözden geçirin** (yeni migration'lar ve veri kaybı riski: sütun/tablo silme, tür daralması).
3. **Kullanıcı onayı şart.** Paylaşılan/üretim veritabanına uygulama onaysız yapılmaz; ajanlar uygulamaz.
4. Önce **yedek alın** (EF down geri alma yok; geri dönüş yedekten).
5. Betik idempotenttir (`__EFMigrationsHistory` denetimli): yalnız eksik migration'ları uygular. Hedef DB'de `sqlcmd`/SSMS veya platformun betik çalıştırma aracıyla çalıştırın. Command DB için `AppDbContext.sql`, Query DB için `QueryDbContext.sql`.
6. Betik izin/seed verisi eklemez: yeni izinler için etiketli birleşik sürümden `DbMigrator seed` gerekir (üretimde sabit parolalı yönetici riski için Dilim 1'e bakın).

Yerelde üretmek için (derleme sonrası):

```powershell
dotnet ef migrations script --idempotent --project src/Backend.Veteriner.Infrastructure --startup-project src/Backend.Veteriner.Infrastructure --context AppDbContext --configuration Release --no-build --output AppDbContext.sql
```

Araç sürümü: `dotnet-ef 9.0.9` (EF Core paketleriyle aynı). Tasarım zamanı fabrikaları veritabanına bağlanmaz.

## Kullanıcı yapılacaklar

- [ ] Depoyu GitHub'a bağlayın / push edin (ajan push etmez); Actions'ın açık olduğunu doğrulayın.
- [ ] İlk koşuyu izleyin: özellikle `integration-tests` (LocalDB'nin runner'da hazır olması, bilinen başarısız testler) ve uyarı özeti.
- [ ] `main` için dal koruması: PR + `build-test` ve `integration-tests` zorunlu durum denetimi.
- [ ] Gizli depo anahtarı: **bu iş akışları için gerekmez**. CD (Dilim 3) için bulut kimlik bilgileri/yayın profili ve uygulama gizlileri (Jwt anahtarı, bağlantı dizeleri, SMTP) depo/ortam *secrets* olarak eklenmeli; değerleri sohbete yazmayın.
- [ ] Sürüm etiketi atın (yukarıdaki kural), yalnız testler yeşilken.
- [ ] Frontend için `API_BASE_URL` depo *değişkeni* (frontend deposu; bkz. oradaki README).
