# Aşama 9 hazırlık keşfi — pilot, canlı işletim, müşteri geçişi

**Tür:** Yalnız keşif. Ürün kodu, migration, seed, yapılandırma değişmedi. Bütün bilgi kod/yapılandırma/doküman okumasından; hiçbir ortama bağlanılmadı, derleme/test/API çağrısı yapılmadı.
**Taban:** backend `main` `7d84586`; frontend `main` `4c92c82` (yalnız okundu).
**Kaynak planlar:** ürün deposu `roadmap/Vetinity_Uctan_Uca_Yol_Haritasi_ve_Urun_Kararlari.md` bölüm 14 (12 çıkış kapısı, 10 pilot senaryosu), `release-plan.md`, `v1-release-scope.md`, `KOORDINATOR-DEVIR.md`, `AJAN-KUYRUGU.md`.
**Gizlilik:** Bu belgede parola, anahtar, bağlantı dizesi, sunucu adı yok; değerler yerine yalnız "dolu/boş/var/yok" yazıldı.

---

## 1. Özet (sade)

- Uygulama **geliştirme ortamı için** iyi donatılmış: sağlık uçları, Serilog, OpenTelemetry, yavaş sorgu günlüğü, rate limiting, audit, kapsamlı CQRS runbook'ları var.
- **Canlıya (üretime) çıkış için** eksik olanlar: otomatik derleme/yayın hattı (CI/CD) yok, kapsayıcı/altyapı kodu yok, yedekleme hiçbir yerde tanımlı değil (yalnız "DBA'nın işi" diye yazılmış), hata izleme/uyarı aracı yok, sürüm etiketi yok, **müşteri verisi içe aktarma aracı yok**, **KVKK silme/anonimleştirme yok**.
- **En ciddi 3 risk:** (1) `seed` komutu her ortamda sabit bilinen parolalı yönetici hesabı oluşturuyor; (2) üretimde `Jwt:Key` ve `AllowedOrigins` boş/varsayılan kalırsa uygulama sessizce yanlış çalışır, açılışta durdurma yalnız Development'ta var; (3) Swagger üretimde de açık.
- Staging ortamı yalnız **tasarım/runbook** olarak var (Windows IIS hedefli); kurulduğuna dair kanıt yok.

---

## 2. Mevcut durum (kanıtlı)

### 2.1 Dağıtım, ortamlar, yapılandırma, gizli bilgi

| Bulgu | Kanıt |
|---|---|
| Ortam dosyaları: base, Development, Staging, Production, LoadTest, IntegrationTests; hepsi **git'te izleniyor** (Development dahil). | `git ls-files` → `src/Backend.Veteriner.Api/appsettings*.json`, `Properties/launchSettings.json` |
| Gizli bilgi kaynağı: User Secrets (`UserSecretsId` hem Api hem DbMigrator'da aynı). Üretim/staging için seçilmiş bir gizli depo yok; "repo dışı" deniyor. | `Backend.Veteriner.Api.csproj:6`, `DbMigrator.csproj:10`, `docs/deploy/staging-config-secrets-inventory.md` |
| Base config'te Jwt anahtarı, bağlantı dizeleri, SMTP kullanıcı/parola, Stripe/Iyzico gizlileri **boş**. Development.json'da Iyzico sandbox ApiKey/SecretKey/MerchantId **dolu** ve izleniyor (değerler yazılmadı); `AllowUnsignedSandboxWebhooks: true`. | `appsettings.json:2-13,15-24,182-215`; `appsettings.Development.json:185-198` |
| Başlangıç doğrulaması (Jwt:Key, iki bağlantı dizesi, Smtp:Pass boş mu) **yalnız Development'ta**. Üretimde Jwt:Key boşsa doğrulama atlanır. | `WebApplicationBuilderExtensions.cs:163-176` |
| CORS izinli kökenler `AllowedOrigins` ayarından; **hiçbir json dosyasında tanımlı değil**, yoksa varsayılan `http://localhost:4200`. | `WebApplicationBuilderExtensions.cs:134-136` |
| Swagger/SwaggerUI koşulsuz açık (ortam kontrolü yok). | `WebApplicationExtensions.cs:45-57` (yalnız `/` yönlendirmesi Development'a bağlı, `:31`) |
| `AllowedHosts: *`; ForwardedHeaders `ForwardLimit = null`, simetri kontrolü kapalı. | `appsettings*.json` son satır; `WebApplicationBuilderExtensions.cs:125-130` |
| Production şablonu: Jwt/SMTP/Billing boş veya yer tutucu (`smtp.example.com`), bağlantı anahtarı `SqlServer` (kod `DefaultConnection`'ı önce, `SqlServer`'i yedek okur), Serilog dosya yolu **Linux** (`/var/log/...`) iken staging runbook'u **Windows IIS** hedefliyor. | `appsettings.Production.json`; `Infrastructure/DependencyInjection.cs:70-84`; `docs/deploy/windows-iis-staging-host-checklist.md` |
| `RefreshTokenCleanup` Production'da `true`, base/Development'ta `false`. | `appsettings.Production.json`, `appsettings.json:121-127` |
| Dockerfile, docker-compose, IaC (Bicep/Terraform), `.github`, Azure/IIS yayın profili: **yok**. CI/CD kanıtı yok. | repo kök listesi, `find` (yml/Dockerfile/web.config) boş; `docs/deploy/staging-environment-design.md` "Dockerfile / docker-compose: Yok" |
| Dağıtım dokümanları var (altı dosya, ~2.100 satır): staging tasarımı, gizli envanteri, ilk deploy runbook'u, artifact kontrol listesi, IIS host kontrol listesi, provisioning planı. Hepsi plan; **uygulandığına dair kanıt yok**. | `docs/deploy/*` |
| Frontend: Vercel SPA yönlendirmesi izi (`vercel.json`); `environment.ts` (üretim) `apiBaseUrl` hâlâ `https://localhost:7173`; staging için `api-staging.vetinity.com` adresi var, gerçek üretim adresi yok. CI/Docker yok. | `veteriner-frontend/vercel.json`, `src/environments/environment.ts`, `environment.staging.ts` |
| API sürümü kodda sabit `"1.0.0"`; git etiketi (tag) yok; 461 commit. | `WebApplicationBuilderExtensions.cs:67`; `git tag` boş |
| Dev ortam: backend 7173, Query DB bayrakları kapalı vb. koordinatör notlarında. | `KOORDINATOR-DEVIR.md` |

### 2.2 Veritabanı, migration, seed, Command/Query DB

| Bulgu | Kanıt |
|---|---|
| API açılışta migration/seed **çalıştırmaz**; migration yalnız `DbMigrator` komutuyla. | `WebApplicationExtensions.cs:~138` günlük mesajı; `DbMigrator/Program.cs:43-75` |
| DbMigrator komutları: `migrate`, `migrate-query`, `seed`, `all`, `seed-dev-*` (yalnız Development, kod içinde ortam kontrolü), `loadtest-seed`, `rebuild-appointment-projections`, `backfill-client/pet/payment-*`. **İçe aktarma (import) komutu yok.** | `DbMigrator/Program.cs:43-330`; `DevSampleDataSeeder.cs:59`, `DevTestVeterinarianSeeder.cs:39` |
| ~57 migration; sonuncusu `20261012222251_AddLabResultStatus`. Aşağı (down) geri alma yok: runbook "EF down yok — yedekten dönüş" diyor. İdempotent SQL betiği üretimi/dokümanı bulunamadı. | `Infrastructure/Migrations/`; `docs/deploy/staging-provisioning-action-plan.md:274` |
| **Seed riski 1 (kritik):** `DataSeeder` `admin@example.com` kullanıcısını **kodda sabit, zayıf bir parolayla** oluşturur ve `Admin` rolü verir; `AdminClaimSeeder` aynı hesabı PlatformAdmin yapar. Ortam kontrolü yok; `seed`/`all` üretimde de çalışır. Parola kodda yazılı olduğu için herkesçe bilinir. | `DataSeeder.cs:38-62`; `AdminClaimSeeder.cs:44`; `DbMigrator/Program.cs:54-75,347-362` |
| **Seed riski 2:** `PermissionSeeder` katalogda olmayan izinleri **siler**; `RolePermissionBindingSeeder` rol-izin bağlarını (`toRemove`) siler. Farklı dallardan seed çalıştırılırsa izinler birbirini siler (koordinatör notunda yaşandığı yazıyor). Üretimde seed yalnız etiketli, birleşik sürümden çalışmalı. | `PermissionSeeder.cs:79-85`; `RolePermissionBindingSeeder.cs:171`; `KOORDINATOR-DEVIR.md` "Seed uyarısı" |
| Yeni `Visits.*` vb. izinler seed ile gelir; mevcut özel roller elle almalı (deploy notu). | `AJAN-KUYRUGU.md` "Bilinen eksikler" |
| Command DB (`AppDbContext`) ve Query DB (`QueryDbContext`) ayrı; ikisi de **zorunlu** bağlantı. Backfill servisleri iki bağlantı aynı katalogsa hata verir. Üretimde iki ayrı veritabanı gerekir. | `DependencyInjection.cs:70-84`; `ClientReadModelBackfillService.cs:262-264`; `staging-environment-design.md` |
| Okuma modeli bayrakları **13'ünün hepsi her ortamda `false`**. Projeksiyon işleyicileri: Appointment ve Client `Enabled: true`; Pet ve Payment `Enabled: false`. Yani pilotta uygulama Command DB'den okur (en basit ve güvenli yol). Açılırsa pet/ödeme için backfill + parity gerekir; Pet için "şimdilik dokunma" kararı var. | `appsettings.json:34-119`, `appsettings.Production.json`; `AJAN-KUYRUGU.md` "Pet read model" |
| Aşama 3–7'de eklenen Visit, Charge, Allocation, finalize, addendum, stok güvenliği, reçete kalemleri, lab durumu için **okuma modeli/projeksiyon yok**; Bugün/arama Command DB'den okuyor. | `AJAN-KUYRUGU.md` "Query DB read modeli" |
| Yedekleme/geri yükleme: **repoda otomatik değil**, "DBA sorumluluğu"; RTO/RPO sayısı "organizasyon SLA'sında tanımlanmalı". Command DB için "veri kaybı kabul edilemez", Query DB yeniden üretilebilir. Yedek stratejisi staging planında seçenek olarak duruyor (günlük tam / snapshot / yok). | `docs/cqrs/cqrs-11b-operations-runbook.md:463-481`; `staging-provisioning-action-plan.md:74,138` |
| Geri alma yolları: bayrak geri alma (Mode C→B→A), ikili dosya geri alma, rollback klasörü; DB geri alma = yedekten dönüş. | `cqrs-11b-operations-runbook.md:196-232,~485-510` |

### 2.3 İzleme

| Bulgu | Kanıt |
|---|---|
| Serilog: konsol + dosya (`logs/log-.ndjson`, günlük, **14 gün**; Production şablonu 30 gün, Linux yolu). Exception zenginleştirme var; merkezi log toplama yok. | `appsettings.json:138-163`; `appsettings.Production.json` |
| Sağlık uçları: `/health/live` (kontrolsüz), `/health/ready` (7 kontrol: sql, query-sql, outbox, 4 projeksiyon; Degraded **ve** Unhealthy → 503), `/health` (varsayılan; Degraded → 200). | `WebApplicationExtensions.cs:119-136`; `WebApplicationBuilderExtensions.cs:148-155` |
| Outbox sağlık: dead-letter varsa Unhealthy, bekleyen > 500 ise Degraded (eşik statik alan, yapılandırılamaz). Projeksiyon gecikme eşikleri yapılandırılabilir (Production'da Unhealthy 60 sn). | `OutboxHealthCheck.cs:9,17-26`; `appsettings.Production.json` `*ProjectionHealth` |
| OpenTelemetry iz + metrik, OTLP/gRPC; uç nokta `OTEL_EXPORTER_OTLP_ENDPOINT`, yoksa `http://localhost:4317`. Üretimde toplayıcı yoksa veri gitmez; gösterge paneli/uyarı kuralı depoda yok. Yalnız tasarım dokümanı: `cqrs-11c-monitoring-alerts.md`. | `WebApplicationBuilderExtensions.cs:68,202-250`; `docs/cqrs/cqrs-11c-monitoring-alerts.md` |
| Hata izleme aracı (Sentry/App Insights/Seq vb.): **yok**. | csproj ve kod taraması boş |
| Yavaş SQL günlüğü: `EF.SlowSql`, eşik 200 ms (config); `SlowConnectionOpenMs 1000`, `MediatorSlowMs 400`, `HttpRequestWarningMs 750`. | `SlowQueryLoggingInterceptor.cs:95-103`; `appsettings.json` `PerformanceDiagnostics` |
| Outbox yönetim ucu (sayfa 200 sınırı, silme/yeniden dene). | `OutboxAdminController.cs:22,199` |
| Hata yanıtları ProblemDetails + traceId/correlationId. | `WebApplicationExtensions.cs:~95-115` |

### 2.4 Güvenlik ve gizlilik

| Bulgu | Kanıt |
|---|---|
| Kimlik: JWT (60 dk) + refresh (7 gün), imza/süre/yayıncı/hedef doğrulanıyor, `ClockSkew = 0`. `SingleSessionPerUser` kapalı. Üretimde `PermissionChange:RevokeSessionsOnPermissionChange` **true** (2026-10-13 kararı; izin değişince refresh token'lar iptal olur, açık access token süresi bitene kadar eski izinle sürer; bkz. `SECURITY_CONFIGURATION.md`). | `WebApplicationBuilderExtensions.cs:178-196`; `appsettings.json:178`; `appsettings.Production.json` |
| Rate limiting var (login 5/dk, parola sıfırlama 3/dk vb., global 200/dk/IP). Hesap kilitleme (lockout) izi **bulunamadı**. Analiz belgesinde `ContactController` "bu depoda yok" deniyor (belge tarihli 2025). Staging'de `RateLimiting:Enabled: true`; base/Production'da bu anahtar yok, varsayılanı **doğrulanmadı**. | `docs/rate-limiting-and-security-hardening.md`; `appsettings.Staging.json` |
| Kiracı: kullanıcı tek kiracılı; `tenant_id` JWT claim'inden `ITenantContext`. Sorgu filtresi (global `HasQueryFilter`) yalnız 3 yapılandırmada (Charge, PaymentAllocation, Payment); izolasyon geri kalanında handler/repo düzeyinde elle. IDOR regresyon kümesi var, "P0/P1 yok". Müşteri ve Hasta **kiracı geneli** (klinik filtresi yok). | `ChargeConfiguration`, `PaymentAllocationConfiguration`, `PaymentConfiguration`; `docs/security/idor-regression.md:1-20`; `docs/AUTH_TENANT_CONTRACT.md` |
| Roller/izinler seed ile; kayıtlı rol-izin eşlemesi `RolePermissionBindings`. Pilotta gerçek rol dağılımı elle doğrulanmadı. | `RolePermissionBindingSeeder.cs`; `docs/ASAMA7_DISCOVERY.md` §5 |
| Denetim izi: `AuditBehavior` (komut payload'ı, Password/Token/Secret/Hash anahtarları maskeli) + `AuditLogs` tablosu. Okuma (kim hangi hastayı gördü) denetimi yok (doğrulanmadı: tüm sorgu işleyicileri taranmadı). | `AuditBehavior.cs:14-24`; `AppDbContext.cs:64` |
| Kişisel veri envanteri (koddan): **Müşteri** (ad, e-posta, telefon, adres); **Hasta/hayvan** (ad, doğum, mikroçip, pasaport no, notlar); **Kullanıcı** (e-posta, parola özeti, ad); klinik kayıtlar (muayene, reçete, lab, aşı, yatış), finans, audit, oturum/IP. T.C. kimlik no alanı **yok**. | `Domain/Clients/Client.cs`, `Pets/Pet.cs`, `Users/User.cs` |
| Silme/saklama/anonimleştirme: Müşteri/Hasta için **silme ucu yok**, yumuşak silme/anonimleştirme/saklama süresi kodu **yok**; yalnız refresh token temizliği ve audit. KVKK silme talebi "ayrı süreç (backlog)". Dosya/görüntü depolama kodu da yok (yükleme `IFormFile` taraması boş). | `Api/Controllers` `HttpDelete` listesi (müşteri/hasta yok); `docs/ASAMA6_DISCOVERY.md` D10 |
| SMTP: MailKit, 587→STARTTLS, 465→SSL; base config Gmail (`smtp.gmail.com`, gönderen adresi dolu), Production yer tutucu host. Hatırlatma işçisi açık (5 dk). "Sent" = SMTP'ye iletildi (teslim/bounce izi yok). **Gerçek gönderim doğrulaması (D10) açık**; kullanıcı notu: eski Gmail uygulama parolası sızdı, "iptal edilmeyecek" kabul edildi (2026-10-10). SMTP seçenekleri hata ayıklama günlüğüne kullanıcı adı ve parola uzunluğu yazıyor (parola değil). Test ucu `EmailTestController` `Admin.Diagnostics` izniyle korunuyor. | `MailKitEmailSender.cs:79-111`; `SmtpOptionsValidator.cs:9-20`; `EmailTestController.cs:16-17`; `ASAMA7_DISCOVERY.md` D10 |
| Faturalama: Üretim şablonu `Iyzico` varsayılan, anahtarlar boş; ödeme sağlayıcısı entegrasyonu ve webhook doğrulaması üretimde denenmedi. | `appsettings.Production.json` Billing |

### 2.5 Performans ve ölçek

| Bulgu | Kanıt |
|---|---|
| Bugün listesi klinik/gün başına **500** satır; aşılırsa sessiz kesme yok, hata döner (pilot klinik için yeterli, çok şubeli için sınır). | `GetVisitsTodayQueryHandler.cs:20` |
| Rapor sayfa üst sınırı 200, dışa aktarım üst sınırı 50.000 satır. | `ReportsSharedLimits` (`Reports/*Constants.cs`) |
| Yavaş sorgu izi canlıda `EF.SlowSql` günlüğü; eşik 200 ms. | bkz. 2.3 |
| ~148 `HasIndex` tanımı; hangi sorgunun indeks kapsadığı çalıştırılarak doğrulanmadı. | `Persistence/Configurations/` |
| Yük testi araçları (k6 betikleri: bugün, panel, projeksiyon gecikmesi) ve LoadTest DB seed'i var; pilot boyutunda sonuç yok. | `tests/load/*` |
| Bilinen test durumu: tam entegrasyon paketinde Dashboard/Payments parity ve `OperationalListsEndpointPaginationTests` kırık; "CREATE DATABASE failed" eşzamanlılık gürültüsü. | `AJAN-KUYRUGU.md` "Test çalıştırma kuralı" |

### 2.6 Müşteri geçişi ve destek/işletim

| Bulgu | Kanıt |
|---|---|
| Müşteri/hasta/aşı geçmişi **içe aktarma yeteneği yok**: kodda yalnız CSV/dışa aktarım (rapor) var; ana plan da "doğrulanmadı" diyor. Elle giriş: hızlı kayıt (müşteri+hasta tek işlem) var, toplu yol yok. | grep (`import`, `CsvHelper` vb.) yalnız rapor yazıcılarını döndürdü; plan bölüm 14 "Müşteri geçişi" kapısı |
| Aşı kaydı geçmiş tarihli girilebilir mi / eski aşı geçmişi nasıl girilir: **doğrulanmadı** (aşı komutları incelenmedi). | — |
| Yedekten dönüş provası, olay yönetimi (kim, hangi kanal), sürüm/etiket, rollback: yalnız CQRS bayrak/ikili geri alma runbook'u var; **genel olay yönetimi ve destek süreci yok**. | `cqrs-11b-operations-runbook.md`; `docs/deploy/*` |
| 10 pilot senaryosu ana planda tanımlı; hiçbiri pilot ortamında yürütülmedi. Aşama 1'den beri elle test edilmeyenler kuyrukta listeli. | plan bölüm 14; `AJAN-KUYRUGU.md` |

---

## 3. Boşluklar ve riskler

### Kritik (pilottan önce kapanmalı)
1. **Sabit parolalı varsayılan yönetici (seed).** Üretimde `seed`/`all` çalıştırılırsa herkesçe bilinen bilgilerle PlatformAdmin hesabı oluşur. (2.2)
2. **Üretim başlangıç doğrulaması yok.** Boş `Jwt:Key`, boş bağlantı dizesi, varsayılan `AllowedOrigins=localhost` üretimde açılışta yakalanmaz; yalnız Development'ta doğrulanıyor. (2.1)
3. **Yedekleme/geri yükleme tanımsız ve hiç denenmemiş.** Command DB'de veri kaybı kabul edilemez ama politika, otomasyon ve restore provası yok. Ana plan çıkış kapısı: "yedekleme ve geri yükleme denenmiş". (2.2)
4. **Hosting ve yayın hattı yok.** Üretim ortamı, alan adı, TLS, gizli deposu, CI/CD, artifact, sürüm etiketi kararı/uygulaması yok; frontend üretim API adresi `localhost`. (2.1)
5. **Müşteri verisi içe aktarma yok.** Pilot klinik mevcut müşteri/hasta/aşı verisiyle başlayacaksa elle girmek dışında yol yok. (2.6)
6. **KVKK:** silme/anonimleştirme, saklama süresi, aydınlatma/açık rıza metinleri, veri işleyen sözleşmesi, VERBİS değerlendirmesi hiçbiri yok (kod veya belge). Sağlık verisi "özel nitelikli" olabilir; hukuki danışman gerekir. (2.4)
7. **Gizli bilgi hijyeni.** Development.json (Iyzico sandbox anahtarları dolu) git'te; bir Gmail uygulama parolası geçmişte sızdı ve iptal edilmedi. Üretime hiçbirinin taşınmaması ve sızan/izlenen anahtarların döndürülmesi gerekir. Git geçmişi taranmadı. (2.1, 2.4)

### Orta
8. Swagger üretimde açık (API yüzeyi ve şeması herkese görünür). (2.1)
9. Hata izleme/uyarı yok: kimse gece outbox dead-letter veya projeksiyon gecikmesinden haberdar olmaz; OTLP uç noktası varsayılan localhost; outbox eşiği sabit. (2.3)
10. Üretimde izin değişince oturum iptali kapalı: bir kullanıcının yetkisi alınsa bile JWT süresi (60 dk) boyunca eski izin geçerli; refresh ile yenilenince doğru izin gelir. (2.4)
11. Hesap kilitleme yok; yalnız IP bazlı rate limit (paylaşılan IP/NAT arkasında klinikte kendi kendini kilitleme ve dağıtık deneme riski). (2.4)
12. Kiracı izolasyonu çoğunlukla elle (global filtre yok); yeni sorgu eklenirken unutulma riski. IDOR regresyon kümesi bu riski azaltıyor ama Aşama 3–7 uçları kümeye eklenmiş mi **doğrulanmadı**. (2.4)
13. SMTP gerçek gönderim (D10) doğrulanmadı; "Sent" teslim anlamına gelmiyor; Gmail ile üretim gönderimi (günlük limit, DKIM/SPF yok) pilot için zayıf. (2.4)
14. İdempotent migration SQL betiği, "migration öncesi yedek al + sonrası doğrula" kontrol listesi, Query DB için ayrı `migrate-query` sırası tek bir üretim runbook'unda toplanmamış (CQRS runbook'u ağırlıklı appointment/payment). (2.2)
15. Serilog yolu Production'da Linux (`/var/log/...`) ama hedef Windows IIS: dosya günlüğü sessizce yazılmayabilir. (2.1)
16. Tam entegrasyon paketi yeşil değil (bilinen kırıklar); sürüm kesmeden önce "temiz referans" gerekir. (2.5)
17. Pilotta okuma modeli bayrakları kapalı kalacak; Aşama 3–7 verisi Command DB'den okunur, performans pilot hacminde ölçülmedi. (2.2, 2.5)

### Düşük
18. Hizmet sürümü kodda sabit `1.0.0`, etiket yok: hangi sürümün canlıda olduğu izlenemez. (2.1)
19. SMTP hata ayıklama günlüğü kullanıcı adını yazıyor (Debug seviyesi; üretimde Information). (2.4)
20. `AllowedHosts: *`, `ForwardLimit = null`: ters vekil arkasında yanlış IP/şema güvenilir sayılabilir. (2.1)
21. Dosya/görüntü deposu kodu yok (Aşama 6 sonrası yüklenecek dosyalar için ayrı yedek ve erişim tasarımı gerekecek). (2.4)

---

## 4. Önerilen çıkış (go-live) kontrol listesi

Ana plandaki 12 kapıdan Aşama 9'a özgü olanlar (canlı işletim, müşteri geçişi, pilot kabulü) + bu keşifte bulunanlar. Hiçbiri yapılmış değil.

**A. Altyapı ve dağıtım**
- [ ] Hosting seçildi, üretim + staging ortamları ayrı kuruldu; alan adı ve TLS hazır.
- [ ] Gizli bilgi deposu seçildi; üretim anahtarları yeni üretildi (geliştirme anahtarlarından farklı); sızan/izlenen gizliler döndürüldü.
- [ ] Üretim açılış doğrulaması: boş `Jwt:Key`, bağlantı dizeleri, `AllowedOrigins`, SMTP eksikse uygulama başlamaz.
- [ ] Swagger üretimde kapalı veya korumalı.
- [ ] CI: derleme + Domain/Application testleri + seçili entegrasyon testleri her birleşmede; CD: etiketli artifact, sürüm numarası uygulamada görünür.
- [ ] Frontend üretim derlemesi doğru API adresiyle; CORS yalnız gerçek frontend adresi.

**B. Veritabanı**
- [ ] İki ayrı veritabanı (Command, Query) üretimde; yetkileri en az ayrıcalıkla.
- [ ] Üretim migration prosedürü: yedek → idempotent betik veya `DbMigrator migrate` → doğrulama → (Query için `migrate-query`); yazılı ve stagingde denenmiş.
- [ ] Üretim ilk kurulumu: seed'in **yönetici hesabı oluşturmayan** bir üretim yolu; ilk yönetici güçlü parolayla ve sonradan değiştirilerek oluşturuldu. Varsayılan `admin@example.com` hesabı üretimde yok.
- [ ] Seed yalnız etiketli birleşik sürümden çalıştırılıyor (izin silme riski), ardından kullanıcılar çıkış-giriş yaptı.
- [ ] Okuma modeli bayrakları pilotta kapalı (Command DB okuma) olduğu kabul edildi; açılacaksa backfill + parity kanıtı.
- [ ] **Yedek:** politika yazıldı, otomatik çalışıyor, şifreli ve ayrı konumda; **yedekten dönüş provası** staging'de yapıldı ve süre ölçüldü (RTO/RPO sayıları belirlendi).

**C. İzleme ve işletim**
- [ ] `/health/ready` ve `/health/live` dışarıdan izleniyor (uptime) ve uyarı kanalı var.
- [ ] Hata izleme + log toplama; outbox dead-letter, projeksiyon gecikmesi, 5xx oranı, yavaş sorgu için uyarı.
- [ ] Olay yönetimi: kim nöbetçi, destek kanalı, şiddet seviyeleri, kullanıcıya duyuru yolu.
- [ ] Rollback: önceki artifact saklı, DB için yedek noktası, bayrak geri alma; "sürüm geri alma provası" yapıldı.

**D. Güvenlik ve KVKK**
- [ ] Hukuki danışman değerlendirmesi: veri envanteri, aydınlatma metni, açık rıza (gerekirse), veri işleyen sözleşmeleri, saklama süreleri, VERBİS gereği.
- [ ] Müşteri/hasta silme ve anonimleştirme süreci tanımlı ve uygulanabilir (klinik kayıt bütünlüğüyle uyumlu).
- [ ] Rol/izin dağılımı pilot rolleriyle (Yönetici, Veteriner, Sekreter) elle doğrulandı; ikinci klinik/kiracı sızıntı testi yapıldı.
- [ ] E-posta: gerçek hesapla uçtan uca gönderim denendi (D10), SPF/DKIM'li alan adı, gönderim limiti uygun.
- [ ] Hesap kilitleme/şüpheli giriş politikası kararlaştırıldı.

**E. Müşteri geçişi ve pilot kabulü**
- [ ] Pilot kliniğin mevcut verisinin biçimi ve hacmi öğrenildi; içe aktarma yöntemi seçildi, deneme içe aktarma yapıldı, sayım/örnek eşleştirme raporu alındı.
- [ ] 10 pilot senaryosu (plan bölüm 14) pilot ortamında sırayla, pilot kullanıcıyla yürütüldü; çıkış engelleri (veri kaybı, yanlış kapsam, tutarsız bakiye) kapandı.
- [ ] Eğitim/ilk gün planı ve ilk hafta destek penceresi.
- [ ] Geri dönüş planı: pilot başarısız olursa klinik eski yönteme nasıl döner, veri nasıl dışa verilir.

---

## 5. Önerilen iş dilimleri

Her dilim ayrı dal, ayrı commit; migration ve paylaşılan/üretim ortamı işleri kullanıcı onayı ister. "Paralel" = aynı anda ayrı ajan/oturum yürütülebilir.

| # | Dilim | Tür | Migration | Paralel? | Bağımlılık |
|---|---|---|---|---|---|
| 0 | **Karar toplantısı** (bölüm 6): hosting, yedek, KVKK, pilot klinik, e-posta sağlayıcı | Karar | — | — | Her şeyden önce |
| 1 | **Üretim sertleştirme (backend):** açılış doğrulamasını üretime genişlet; Swagger ortama bağlı; ilk-yönetici kurulum komutu (sabit parola kaldırılır); `RevokeSessionsOnPermissionChange` ve rate-limit varsayılanlarını gözden geçir; SMTP günlüğü; Serilog yolu | Backend (kod) | Yok | Evet | Yok (1'in `seed` kısmı kullanıcı onayı) |
| 2 | **CI/CD + sürümleme:** pipeline, artifact, sürüm numarası/etiket, idempotent migration betiği üretimi | Altyapı | Yok | Evet | Hosting kararı (araç seçimi) |
| 3 | **Ortam kurulumu (staging → üretim):** host, TLS, gizli deposu, iki veritabanı, deploy runbook'unu uygulama | Altyapı (kullanıcı hesabı gerekir) | Staging'e `migrate` (onaylı) | Kısmen (1 ve 2 ile) | Dilim 0, 2 |
| 4 | **Yedekleme ve geri yükleme:** otomasyon + **dönüş provası** + yazılı prosedür | Altyapı | Yok | 3 sonrası | Dilim 3 |
| 5 | **İzleme ve uyarı:** hata izleme, log toplama, uptime, outbox/projeksiyon uyarıları, gösterge paneli | Altyapı + küçük backend | Yok | Evet | Dilim 0 (araç) |
| 6 | **E-posta gerçek gönderim doğrulaması (D10)** + üretim gönderici (SPF/DKIM) | Backend/altyapı | Yok | Evet | Kullanıcı hesabı onayı; parola sohbete yazılmaz |
| 7 | **Veri içe aktarma (müşteri → hasta → aşı geçmişi):** biçim, doğrulama, önizleme, "kuru çalıştırma", rapor, geri alma (parti kimliği) | Backend sözleşme → backend → frontend | **Olası** (içe aktarma partisi tablosu) | Evet (1–6 ile) | Dilim 0 (pilot verisi biçimi); önce sözleşme |
| 8 | **KVKK teknik işleri:** silme/anonimleştirme kuralı, saklama, veri dışa aktarma (hasta/müşteri talebi), erişim/okuma denetimi | Backend + frontend | **Olası** (anonimleştirme/saklama alanları) | Dilim 7 ile paralel | Hukuki danışman çıktısı; ürün kararı (ADR) |
| 9 | **Performans/ölçek ölçümü:** pilot hacmi (örn. 5–10 bin hasta, günde 50–100 geliş) için yük testi, yavaş sorgu/indeks incelemesi, Bugün 500 sınırı | Backend testi | İndeks gerekirse olası | Evet | Dilim 3 (gerçekçi ortam) veya LoadTest DB |
| 10 | **Pilot hazırlık provası:** 10 senaryonun staging'de pilot verisiyle yürütülmesi, kalan elle testler, hata düzeltme | Test + frontend/backend düzeltmeleri | Düzeltmeye bağlı | 7 ve 3 sonrası | Dilim 3, 7 |
| 11 | **Canlı işletim kılavuzu:** olay yönetimi, destek kanalı, sürüm/rollback prosedürü, rol bazlı kullanıcı kılavuzu | Doküman | Yok | Evet | Dilim 0 |

**Önerilen sıra:** 0 → (1, 2, 5, 6, 11 paralel) → 3 → 4 → (7, 8 paralel; 9) → 10 → pilot başlangıç kararı.
**Frontend payı:** yalnız 7 (içe aktarma ekranı), 8 (silme/dışa aktarma ekranları), 10 (düzeltmeler) ve üretim ortam dosyası/CORS adresi. Diğerleri backend/altyapı.
**Migration içeren olası dilimler:** 7 ve 8 (ve 9'da indeks). Diğerleri migration içermez.

---

## 6. Kullanıcıya sorulacak kararlar

Her biri için seçenekler ve öneri. Öneriler maliyet/ölçü verisine dayanmıyor; kullanıcı kararıyla netleşir.

**K1 — Nerede çalışacak (hosting)?**
- **a)** Yönetilen bulut (ör. Azure App Service + Azure SQL): yedek ve sertifikayı platform yönetir, bakım yükü en az; aylık maliyeti yüksek olabilir.
- **b)** Windows sunucu + IIS + SQL Server (mevcut staging dokümanlarının hedefi): tam kontrol; yedek, güncelleme, güvenlik duvarı bizim işimiz.
- **c)** Kapsayıcı (Docker) + kiralık sunucu: esnek, ama depoda hazır bir şey yok, sıfırdan kurulum gerekir.
- **Öneri: a** pilot için; en az işletim yükü ve yedek/geri yükleme hazır geliyor. Frontend zaten Vercel'e yönelik görünüyor (izler var, kullanıldığı **doğrulanmadı**). Mevcut IIS dokümanları b için hazır; b seçilirse onlar kullanılır.

**K2 — Yedekleme politikası?**
- **a)** Günlük tam + sık işlem günlüğü yedeği (istenen ana dönebilme, kayıp en fazla birkaç dakika), 30 gün saklama, ayrı konumda, ayda bir geri yükleme provası.
- **b)** Yalnız günlük tam yedek (kayıp en fazla 1 gün).
- **c)** Platformun otomatik yedeği (a seçeneğiyle benzer, yönetilen hosting'de hazır gelir).
- **Öneri: a/c** (klinik kayıtları için bir günlük veri kaybı kabul edilmesi zor). Query DB yedeği zorunlu değil (yeniden üretilebilir) ama restore süresini kısaltır. Hedef süre (RTO/RPO) sizin kabulünüz olmalı.

**K3 — KVKK danışmanlığı?**
- **a)** Pilottan önce bir KVKK uzmanı/avukatla veri envanteri, aydınlatma metni, saklama ve silme kuralları, veri işleyen sözleşmesi, VERBİS değerlendirmesi.
- **b)** Önce pilotu sınırlı/ücretsiz yapıp sonra hukuki çalışma.
- **c)** Hazır şablonlarla kendimiz.
- **Öneri: a**; gerçek müşteri ve hasta sahibi kişisel verisi işlenecek. Teknik dilim 8 bu çıktıya göre şekillenir. (Hukuki tavsiye vermiyorum; yalnız kod envanterini çıkardım.)

**K4 — Pilot klinik seçimi?**
- **a)** Tek şubeli, 2–5 kullanıcılı küçük klinik; mevcut veri kâğıt/Excel (taşıması kolay).
- **b)** Başka bir klinik yazılımı kullanan klinik (veri dışa aktarımı ve alışkanlık geçişi zor).
- **c)** Önce bir tanıdık klinikte "gölge" kullanım (gerçek iş eski yöntemle sürer, paralel girilir).
- **Öneri: a**, ilk 2 hafta c ile gölge. Hangi klinik, kaç hasta kaydı ve hangi biçimde veri var, bilinmeden dilim 7 başlamaz.

**K5 — E-posta gönderimi?**
- **a)** Alan adına bağlı e-posta gönderim hizmeti (SPF/DKIM'li); sızan Gmail parolası kullanılmaz ve iptal edilir.
- **b)** Pilotta Gmail hesabı ile devam (günlük limit, teslim sorunu riski).
- **Öneri: a**. D10 gerçek gönderim testi seçilen hesapla yapılır; parola yalnız gizli depoya girilir.

**K6 — Pilotta okuma modeli (Query DB) bayrakları kapalı kalsın mı?**
- **a)** Kapalı (şimdiki hâl; Command DB'den okuma, en az risk).
- **b)** Açık (pet/ödeme backfill + parity gerekir).
- **Öneri: a**; pilot hacminde yavaşlık ölçülürse (Dilim 9) açılır.

**K7 — Sabit parolalı varsayılan yönetici?** Üretim seed'inin bu hesabı oluşturmaması için kod değişikliği gerekir (Dilim 1). **Öneri: onay.**

**K8 — Müşteri verisi içe aktarma kapsamı?** a) Yalnız müşteri+hasta, aşı geçmişi elle; b) müşteri+hasta+aşı geçmişi; c) tüm klinik geçmiş. **Öneri: b**, c ilk sürümde değil.

**K9 — Yayın zamanı:** Aşama 4–8'in kalanı pilotla paralel mi yürüsün (ana plan "evet" diyor), yoksa pilot başlamadan hepsi mi bitsin? **Öneri:** ana plana uyum; pilot kapsamı Aşama 1–3 senaryolarıyla sınırlı başlasın.

---

## 7. Doğrulanmayanlar

- Hiçbir şey çalıştırılmadı: derleme, test, API, DB, sağlık ucu, e-posta, `dotnet ef` yok. Aşağıdaki tüm "var/yok" ifadeleri **dosya okuması ve arama** sonucudur.
- Git geçmişinde gizli bilgi taraması yapılmadı; `appsettings.Development.json`'daki dolu alanların gerçek/sandbox ayrımı yalnız adından ("sandbox" öneki) çıkarıldı.
- `RateLimiting:Enabled` anahtarının varsayılanı ve `RateLimitingExtensions` içeriği okunmadı; Production'da açık olduğu doğrulanmadı.
- Hesap kilitleme: yalnız `Lockout/FailedLogin` aramasında ilgili kod çıkmadı; farklı adla var olabilir.
- Tüm sorgu işleyicilerinde kiracı filtresi uygulandığı doğrulanmadı; yalnız global filtre sayımı ve IDOR belgesi okundu. Aşama 3–7 uçlarının IDOR kümesine eklendiği doğrulanmadı.
- Okuma (görüntüleme) denetimi olmadığı tüm işleyiciler taranmadan çıkarıldı.
- Eski aşı geçmişi girişinin (geçmiş tarihli) mümkün olup olmadığı incelenmedi.
- Migration sayısı (~57) dosya sayımından; `dotnet ef migrations list` bu makinede yanıltıcı (koordinatör notu).
- Staging/üretim ortamının var olup olmadığı bilinmiyor; belgeler yalnız plan. Frontend'in Vercel'de yayında olduğu doğrulanmadı.
- OpenTelemetry toplayıcı/gösterge paneli, uyarı kanalı depoda yok; canlıda başka yerde kurulu olabilir (bilinmiyor).
- `ReportsSharedLimits` sınır değerleri (200/50.000) arama çıktısından; dosya satırı belirtilmedi.
- Pilot hacmi için performans hiç ölçülmedi; indekslerin sorguları kapsadığı doğrulanmadı.
- Kişisel veri envanteri `Client/Pet/User` varlıklarından; diğer varlıklardaki (muayene notu, reçete, yatış vb.) serbest metin alanları sayılmadı.
- Hukuki konularda (KVKK, VERBİS, sağlık verisi) bu belge tavsiye değildir.
