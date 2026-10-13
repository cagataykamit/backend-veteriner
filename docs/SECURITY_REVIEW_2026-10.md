# Backend güvenlik incelemesi (2026-10-13)

Kapsam: yalnız statik kod okuma (`C:\Users\DELL\source\repos\Veteriner`, `main` tabanı). Ürün kodu değiştirilmedi, canlı API'ye istek atılmadı, test paketi koşulmadı. Tek çalıştırılan komut: `dotnet list package --vulnerable --include-transitive` (salt okunur).

## Özet

- **Kritik: 0. Yüksek: 0.** Orta: 6. Düşük: 7.
- Tenant ve klinik izolasyonu incelenen uçlarda sağlam: JWT'deki `tenant_id` her zaman doluyor ve sorgu `tenantId` ile çakışırsa 403; yazma/okuma handler'ları kaydı `tenantId` ile spec üzerinden çekiyor, klinik kapsamı ya `IClinicReadScopeResolver` ya da `TenantWideClaimNames` + `UserClinics` ile uygulanıyor. Başka kiracı id'siyle IDOR yolu bulunmadı.
- Başlıca açıklar: izin sınırını aşan bileşik okuma (muayene ilişkili özeti), Swagger'ın her ortamda açık olması, hesap kilitleme yokluğu, güvenlik başlıkları/HSTS yokluğu, `Development` ayar dosyasında (git'te izli) sandbox ödeme anahtarları, CORS varsayılanının üretimde `localhost`.

## Bulgular

### Orta

**O1. Muayene ilişkili özeti, kayıt türü izinlerini atlıyor**
- Konum: `src/Backend.Veteriner.Api/Controllers/ExaminationsController.cs:206-211` (yalnız `Examinations.Read`), `src/Backend.Veteriner.Application/Examinations/Queries/GetRelatedSummary/GetExaminationRelatedSummaryQueryHandler.cs:121-130`.
- Kanıt: handler tedavi, reçete, lab, yatış ve **ödeme** kayıtlarını listeliyor; dosyada `HasPermission` çağrısı yok. Aynı tür veri `PetRecordPermissions.Split` ile (pet timeline/history-summary) tür izniyle süzülüyor; burada tutarsız.
- İstismar: `Examinations.Read` olup `Payments.Read`/`Treatments.Read`/`LabResults.Read` olmayan özel rol, muayene kimliğiyle bu kayıtların özetini (ödeme tutarları dahil) okur. Varsayılan rollerde `Veteriner` bunların hepsine sahip olduğundan yalnız özel/kısıtlı rollerde etkili.
- Öneri: tür başına `checker.CanRead(kind)`; izin yoksa o liste boş/`null`, `omitted` bilgisi. Çaba: küçük (~yarım gün + test).
- Ek not: `clinicId` aktif klinik yokken `null` geçiyor; bağlı kayıtlar muayeneye bağlı olduğundan pratik risk düşük, doğrulanmadı.

**O2. Swagger üretim dahil her ortamda açık**
- Konum: `Configuration/WebApplicationExtensions.cs:45-55` (`UseSwagger`/`UseSwaggerUI` koşulsuz; yalnız `/` yönlendirmesi Development'a bağlı). Çevre değişkeniyle kapatma yok.
- Etki: kimlik doğrulamasız tam API sözleşmesi ve izin adları. Doğrudan veri sızıntısı değil, keşfi kolaylaştırır.
- Öneri: `IsDevelopment()`/yapılandırma bayrağına bağla. Çaba: çok küçük.

**O3. Hesap kilitleme yok, giriş denemesi yalnız IP başına sınırlı; kullanıcı varlığı zamanlamayla ayırt edilebilir**
- Konum: `LoginCommandHandler.cs:111-134` (kullanıcı yoksa BCrypt çalıştırmadan dönüş; yanlış parolada sayaç yok), `Middleware/RateLimitingExtensions.cs:82-95` (login: IP başına 5/dk).
- Kanıt: `LockoutEnd`/`FailedLogin` benzeri alan yok (`grep` boş). Kullanıcı yokken yanıt, var olup parola yanlışken BCrypt süresi kadar sonra döner.
- İstismar: dağıtık IP'lerle tek hesaba yavaş tahmin; zamanlama farkıyla e-posta numaralandırma. Hata mesajı aynı olduğu için yalnız zamanlama sızar.
- Öneri: hesap bazlı ardışık hata sayacı + geçici kilit (veya hesap+IP birleşik limit), kullanıcı yokken sahte hash doğrulaması. Çaba: orta (migration gerekir; onay şart).

**O4. Güvenlik başlıkları ve HSTS yok; CORS varsayılanı üretimde `localhost`; `AllowedHosts: "*"`**
- Konum: `appsettings.json:236` (`AllowedHosts`), `WebApplicationBuilderExtensions.cs:134-146` (`AllowedOrigins` yoksa `http://localhost:4200`, `AllowCredentials`), `appsettings*.json` içinde `AllowedOrigins` anahtarı **yok**. `UseHsts`, `X-Content-Type-Options`, `X-Frame-Options`, CSP için hiçbir kod bulunmadı.
- Etki: üretimde ortam değişkeni/ayar atlanırsa ya frontend çalışmaz ya da yanlış origin izinlenir; başlıklar eksik. API JSON döndüğü için tarayıcı riski sınırlı.
- Öneri: üretimde `AllowedOrigins` zorunlu (yoksa başlangıçta hata), `UseHsts`, `nosniff`, `Host` listesi. Çaba: küçük.

**O5. `appsettings.Development.json` git'te izli ve sandbox ödeme anahtarı/gizli anahtar çiftiyle merchant bilgisi içeriyor**
- Konum: `src/Backend.Veteriner.Api/appsettings.Development.json:184-188` (`Billing:Iyzico:ApiKey`, `SecretKey` değerleri `sandbox-` önekli, `MerchantId`, geçici `ngrok` callback URL'si), satır 3-4 geliştirici makine adlı (Windows kimlik doğrulamalı) bağlantı dizesi. Değerler rapora yazılmadı. `git ls-files` ile izli, geçmişte en az 3 commit.
- Etki: yalnız sandbox olduğu doğrulanamadı (önek dışında doğrulama yok); depo dışarı çıkarsa anahtarlar sızar. Parola içeren bağlantı dizesi yok.
- Öneri: anahtarları user-secrets'a taşı, dosyayı temizle, gerekirse sandbox anahtarlarını iyzico panelinden yenile; geçmişten temizleme kararı kullanıcıda. Çaba: küçük (+ anahtar yenileme).

**O6. İzinler JWT içinde 60 dk önbellekli; rol/izin geri alma access token süresi boyunca etkisiz**
- Konum: `Api/Auth/PermissionAuthorizationHandler.cs:11-21` (yalnız token claim'leri), `LoginCommandHandler.cs:153-160`, `appsettings.json` `Jwt:ExpMinutes: 60`. (Klinik tarafı `UserOperationClaim` DB'den okunuyor, yani kısmen güncel.)
- Etki: yetkisi alınan kullanıcı en fazla 60 dk daha eski yetkisini kullanır; "yalnız yönetici" işlemleri (`*.Correct`) dahil. Refresh yeniden okur (`RefreshCommandHandler.cs:~110`).
- Öneri: süreyi kısalt (ör. 15 dk) ya da kritik düzeltme komutlarında `ICurrentUserPermissionChecker` ile DB doğrulaması. Çaba: küçük-orta. Ürün kararı.
- **Kısmi önlem (2026-10-13):** Production'da `RevokeSessionsOnPermissionChange=true`; yetkisi değişenin refresh token'ları düşer, ancak mevcut access token süresi (60 dk) bitene kadar geçerli kalır. Token ömrü/DB doğrulaması açık (`SECURITY_CONFIGURATION.md`).

### Düşük

**D1. "Yalnız yönetici" denen düzeltme izinleri rol tablosunda daha geniş dağıtılmış**
- Konum: `Application/Auth/RolePermissionBindings.cs:304-319` (ClinicAdmin: `Payments.Correct`, `Charges.Correct`, `Administrations.Correct`), `:392-394` (**Veteriner: `Administrations.Correct`**).
- Kanıt: uçlar `Charges.Correct`/`Payments.Correct`/`Administrations.Correct` ile doğru kısıtlı (`ChargesController.cs:84,106`, `PaymentsController.cs:124`, `AdministrationsController.cs:85`); sorun rol atamasında. Sekreter ve Veteriner'de `Charges.Correct`/`Payments.Correct` yok. Ödeme `PUT`'u (`Payments.Update`, Sekreter'de var) tutar/müşteri/klinik/para birimi değişiminde ayrıca `Payments.Correct` istiyor (`UpdatePaymentCommandHandler.cs:229`) ve doğrulandı.
- Öneri: ürün sahibi "yönetici" tanımını (Admin/Owner mı, ClinicAdmin dahil mi, Veteriner uygulama iptali mi) netleştirsin. Çaba: karar + seed (uyarı: seed yalnız birleşik main'den).
- **Karar (2026-10-13) ve durum:** yönetici düzeyi = Admin, Owner, ClinicAdmin (ADR-012). `Administrations.Correct` Veteriner'den çıkarıldı; seed eski bağı siler (`RolePermissionBindings.Revoked`). Matris: `SECURITY_CONFIGURATION.md`.

**D2. Stok geri alma (`/stock-movements/{id}/reverse`) `StockMovements.Create` izniyle**
- Konum: `StockMovementsController.cs:91-92`. Ayrı bir "düzeltme" izni yok; Sekreter'de `StockMovements.Create` var (`RolePermissionBindings.cs:472-473`), yani manuel stok girişi, düzeltme ve geri alma yapabilir.
- Öneri: `StockMovements.Correct` gibi ayrı izin düşün (ürün kararı). Çaba: küçük.
- **Karar (2026-10-13) ve durum:** `StockMovements.Correct` eklendi (Admin, Owner, ClinicAdmin); geri alma ucu bu izni ister, Sekreter hareket ekler ama geri alamaz. Uygulama iptali bu izne takılmaz (`STOCK_MOVEMENT_SAFETY_API_CONTRACT.md`).

**D3. Ödeme tutarı ve stok hareketi miktarında üst sınır yok**
- Konum: `CreatePaymentCommandValidator.cs:12` (`GreaterThan(0)`), `Domain/Payments/Payment.cs:~72` yalnız `<= 0` kontrolü; `CreateStockMovementCommandValidator.cs:38-45`. Karşılaştırma: `Charge.MaxQuantity/MaxUnitPrice` (`Domain/Charges/Charge.cs:13-14`) ve `Administration.MaxQuantity` var.
- Etki: devasa değerde SQL `decimal` taşması (500) veya anlamsız kayıt; tahsis toplamı eşitliği ödeme tarafında kısmen sınırlıyor. Taşmanın gerçekte 500 verdiği doğrulanmadı.
- Öneri: `Payment.MaxAmount`, stok miktarı üst sınırı. Çaba: küçük.

**D4. `GET /api/v1/admin/ping` kimlik doğrulamasız**
- Konum: `Controllers/Admin/PingController.cs:8-16` (`[Authorize]` yok; fallback politika da yok, `Program`'da `FallbackPolicy` bulunamadı). Diğer üç uç `HasPermission` ile korumalı. İçerik yalnız `pong`.
- Öneri: kaldır veya `[Authorize]`. Çaba: çok küçük.

**D5. Günlüklerde kişisel veri**
- Konum: `RequestPasswordResetHandler.cs:43,49,115` (e-posta düz yazılı, `Information`/`Warning`; "user NOT FOUND for {Email}" ayrıca hesap varlığını loga yazıyor), `ConfirmPasswordResetHandler.cs:58` (Debug: token'ın kısa kesiti). Giriş/yenileme logları `UserId`/`TenantId` kullanıyor (iyi).
- Öneri: e-postayı maskele/hash'le; token kesitini kaldır. Çaba: çok küçük.

**D6. Rate limit anahtarı ve genel limit**
- Konum: `RateLimitingExtensions.cs:99-115` (`refresh` anahtarı `IP:sub:UserAgent`; User-Agent değiştirerek sınır aşılır, refresh zaten tek kullanımlık token gerektiriyor), `:234-235` (IP yalnız `RemoteIpAddress`; `ForwardedHeaders` `KnownProxies` tanımsız ve `ForwardLimit=null`, `WebApplicationBuilderExtensions.cs:127-132`). Ters vekil arkasında doğru IP görülüp görülmediği **doğrulanmadı**. `owner-signup` ve `invites/*/signup-and-accept` anonim uçlarında özel limit yok (yalnız genel 200/dk/IP).
- Öneri: UA'yı anahtardan çıkar, anonim kayıt uçlarına ayrı limit, `KnownProxies/KnownNetworks`. Çaba: küçük.

**D7. Bağımlılık ve test altyapısı**
- `SQLitePCLRaw.lib.e_sqlite3 2.1.10` geçişli paket, önem **High** (GHSA-2m69-gcr7-jv3q), yalnız `Backend.Veteriner.IntegrationTests` projesinde; üretim projelerinde zafiyetli paket yok (`dotnet list package --vulnerable`, nuget.org kaynağı). Öneri: test altyapısını güncelle. Çaba: küçük.
- `IntegrationTestDatabaseGuard` (`tests/.../Infrastructure/IntegrationTestDatabaseGuard.cs:60-116`): yasaklı ad listesi + `VetinityCommandDb_IntegrationTests` önek zorunluluğu + `Prod/Development` içeren ad reddi; boş ad/çözülemeyen dize reddi. Dev/prod veritabanı adı için yeterli. Boşluk: **sunucu denetlenmiyor** (uzak bir SQL Server'da önek adlı veritabanı geçer; `EnsureDeleted` riski). Öneri: LocalDB/`localhost` zorunluluğu (opsiyonel bayrakla gevşet). Çaba: küçük. `appsettings.IntegrationTests.json:96` içinde sabit test JWT anahtarı var (yalnız test; kabul edilebilir).

## Düzgün olanlar

- **Tenant çözümü:** `TenantContextMiddleware` JWT `tenant_id` ile sorgu uyuşmazlığını 403'e çeviriyor; `Login/Refresh/SelectClinic` her token'a `tenant_id` yazıyor, kiracı üyeliği ve `IsActive` kontrol ediliyor; tek kiracı kuralı (`LoginCommandHandler.cs:169-194`).
- **Klinik kapsamı:** `ClinicReadScopeResolver` atanmamış klinikte `Clinics.AccessDenied`; GetById'lerde atanmamış klinik kaydı `NotFound` (varlık sızmıyor): `GetChargeByIdQueryHandler.cs:55-67`, `GetExaminationByIdQueryHandler.cs:66-84`, `GetLabResultByIdQueryHandler.cs`. Charges/Payments/PaymentAllocations/Administrations/Stock/Visits/Lab/Prescriptions handler'larının hepsinde kapsam çağrısı var (otomatik tarama; Pets ve Clients bilinçli kiracı geneli).
- **Yetki özelliği:** 44 controller'ın hepsinde sınıf veya eylem düzeyinde `[Authorize]`/izin var; yalnız `AllowAnonymous` olanlar auth, e-posta, parola, public, billing callback/webhook ve `ping` (D4). Charges/Payments/Administrations/LabResults/Visits/Examinations/Prescriptions/Stock uçlarının izin adları `PermissionCatalog` ve rol tablosuyla uyumlu.
- **Bugün tutarları:** `GetVisitsTodayQueryHandler.cs:124-129` `Charges.Read` yoksa tutar alanlarına dokunmuyor (null). Pet timeline/history-summary tür başına izinle süzülüyor (`PetRecordPermissions`). `GET /clients/{id}/balance` `Charges.Read` istiyor (`ClientsController.cs:175`).
- **Kütle atama / yetki ezme:** komutlarda `TenantId` yok (yalnız Tenants/VaccineDefinitions komutlarında ve onlarda `jwtTenantId != request.TenantId` denetimi), kiracı hep bağlamdan; ödeme/ücret gövdesinde `ClinicId` aktif klinik bağlamıyla ve atanmış klinikle doğrulanıyor.
- **Idempotency/eşzamanlılık:** `requestId` zorunlu ve içerik parmak izi (`Payments.RequestIdRequired`, `PaymentRequestFingerprint`), `DbUpdateConcurrencyException` yakalanıyor, muayene/lab `RowVersion` kodlu. Ücret miktar/birim fiyat sınırları domain'de.
- **Sayfa boyutu:** liste işleyicileri `Math.Clamp(..., 1, 200)`; rapor sorguları kendi `MaxPageSize` sabitleriyle kısıtlı.
- **Kimlik doğrulama:** BCrypt, refresh token SHA-256 hash'li, rotasyon + yeniden kullanım tespiti tüm oturumları iptal ediyor, parola sıfırlamada tüm refresh'ler iptal, `JWT` imza/issuer/audience/ömür doğrulaması ve `ClockSkew=0`, anahtar ≥32 karakter. Parola kuralları (`StrongPasswordRules`) kullanılıyor.
- **Hata yanıtı:** üretimde 500 için genel mesaj, stack trace yalnız Development (`ExceptionHandlingExtensions.cs:131-135`).
- **Ham SQL:** `FromSql`/`ExecuteSqlInterpolated` yok; `ExecuteSqlRaw` yalnız projeksiyon işlemcilerinde (`*ProjectionProcessor.cs`, sabit metin; parametre/enjeksiyon incelemesi tam yapılmadı). `IgnoreQueryFilters` yalnız iptal edilmiş kayıtları bulan `Charge/Payment` spec'lerinde ve spec'ler `TenantId`'yi hâlâ süzüyor (`ChargeForUpdateSpec` vb., tam okunmadı).
- **Gizli bilgi:** `appsettings.json` ve `Production.json` gizli alanları boş; `Development` için zorunlu user-secrets denetimi var.

## Doğrulanmadı

- Hiçbir uç canlı çalıştırılmadı; tüm sonuçlar kod okumasından. Ters vekil/dağıtım yapılandırması (forwarded headers, TLS, `AllowedOrigins` ortam değişkeni) incelenmedi.
- Her handler satır satır okunmadı; kapsam taraması grep tabanlı (kapsam çağrısının varlığı, doğruluğu değil). Okunmayan: Hospitalizations/Treatments/Vaccinations/Reminders/Reports/Outbox yazma yolları, `PaymentAllocations` tahsis hesabının eşzamanlılık davranışı, `Administrations` stok düşümü yarışı.
- `Examination` finalize/addendum akışında kilit ve yetki domain kuralları, `Timeline` sorgu özelliklerinin klinik kapsamı kısmen okundu.
- Giriş akışında `EmailConfirmed` zorunluluğu: giriş işleyicisinde denetim bulunmadı (ürün kararı olabilir), tam doğrulanmadı. Pasif/devre dışı kullanıcı bayrağı bulunamadı; var olup olmadığı doğrulanmadı.
- `owner-signup` ve davet uçlarında e-posta numaralandırma ve istismar davranışı incelenmedi.
- Sandbox anahtarlarının gerçekten yalnız sandbox'a ait olduğu (O5) doğrulanmadı.
- Ödeme `decimal` taşmasının yanıt kodu (D3) doğrulanmadı.
- Webhook imza doğrulaması (Stripe/iyzico) uygulama katmanında; incelenmedi.
