# Güvenlik yapılandırması (üretim/staging)

Ayarlar `appsettings.{Environment}.json` veya ortam değişkeniyle verilir (iç içe anahtar: `AllowedOrigins__0`).

| Ayar | Davranış |
|---|---|
| `AllowedOrigins` (dizi) | CORS için izinli frontend origin'leri. **Production/Staging'de boşsa uygulama başlamaz.** Development/test ortamında boşsa `http://localhost:4200`. |
| `AllowedHosts` (`;` ile ayrılmış) | Host süzmesi. **Production/Staging'de `*` veya boşsa uygulama başlamaz.** Taban değer yalnız loopback adresleridir; `appsettings.Production.json` içinde boştur, ortam değişkeniyle (`AllowedHosts`) doldurulmalıdır. |
| `Swagger:Enabled` (bool) | Swagger yalnız Development'ta açıktır; başka ortamda gerekiyorsa `true` verilir (varsayılan kapalı). |
| `ExternalApi:PermitLimitPerMinute` (int) | Dış API (TIER-009) anahtar başına dakikalık istek sınırı; 1–600 dışı ya da boşsa 60. Kimlik, kapsam ve hatalar: `EXTERNAL_API_CONTRACT.md`. |

Tüm yanıtlara `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` ve `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` (Swagger yolu hariç) eklenir. HSTS Development dışında açıktır (`UseHsts`).

## Yerel geliştirme ayarları

`src/Backend.Veteriner.Api/appsettings.Development.json` artık git'te izlenmez (`.gitignore`). Başlangıç noktası: `appsettings.Development.json.example`. Gizli değerler (bağlantı dizesi parolası, ödeme sağlayıcı anahtarları, JWT anahtarı) `dotnet user-secrets` ile verilir:

```
dotnet user-secrets set "Jwt:Key" "<değer>" --project src/Backend.Veteriner.Api
dotnet user-secrets set "Billing:Iyzico:ApiKey" "<değer>" --project src/Backend.Veteriner.Api
dotnet user-secrets set "Billing:Iyzico:SecretKey" "<değer>" --project src/Backend.Veteriner.Api
dotnet user-secrets set "Push:Fcm:ServiceAccountFile" "<firebase-hizmet-hesabi.json yolu>" --project src/Backend.Veteriner.Api
```

Testler ve CI bu yerel dosyaya bağımlı değildir: varsayılan/rollout bayrağı testleri `appsettings.Development.json` yerine izlenen `appsettings.Development.json.example` şablonunu okur (şablonda bayraklar production-safe `false` kalmalıdır).

## Açılış doğrulaması

Development, Production ve Staging ortamlarında şu ayarlar yoksa API başlamaz (hata iletisi yalnız ayar adını söyler, değer yazmaz): `Jwt:Key` (en az 32 karakter), `ConnectionStrings:DefaultConnection`, `ConnectionStrings:QueryConnection`, `Smtp:Pass`, `AllowedOrigins`, `AllowedHosts`. Production/Staging'de ayrıca `Smtp:Host` yer tutucu (`smtp.example.com`) kalamaz. Kural tek yerde: `StartupConfigurationValidator`, `HostSecurityConfiguration`. Test ortamları (`IntegrationTests`, `LoadTest`) kendi yapılandırmasını getirir ve bu denetimin dışındadır.

Push (`Push:Fcm`, MOBILE-003) isteğe bağlıdır: yoksa push kapalı çalışır (yalnız log); verilmiş ama okunamıyor ya da özel anahtar geçersizse API başlamaz (`FcmServiceAccount.Load`, ayrıntı `PORTAL_API_CONTRACT.md` §14.6).

## İlk yönetici (üretim)

`seed` ve `all` komutları **hiçbir ortamda** sabit parolalı `admin@example.com` hesabını, varsayılan kiracıyı, seed kliniği ve aboneliği OLUŞTURMAZ (bu hesap yalnız otomatik test altyapısında vardır); yalnız izinler, tür/ırk kataloğu, aşı tanımları ve rol bağları kurulur. İlk PlatformAdmin hesabı için (önce `seed` çalıştırılmış olmalı):

```
$env:DOTNET_ENVIRONMENT = "Production"
$env:VETINITY_FIRST_ADMIN_PASSWORD = "<güçlü parola>"   # verilmezse konsolda gizli girilir
dotnet run --project src/Backend.Veteriner.DbMigrator -- create-platform-admin --email <e-posta>
```

Parola kuralları kullanıcı parolasıyla aynıdır; parola loglanmaz, mevcut bir e-postaya dokunulmaz. Girişte tek kiracı üyeliği zorunlu olduğundan hesap, müşteri kiracılarından ayrı, içi boş **"Platform" kiracısına** (`Tenant.PlatformTenantName`) bağlanır; kiracı yoksa oluşturulur, varsa yeniden kullanılır (klinik, abonelik, müşteri oluşturulmaz). "Platform" adlı kiracıda klinik veya abonelik varsa komut müşteri kiracısı olabileceği için durur. Müşteri kiracısı bu adı alamaz (kayıt, kiracı oluşturma ve kurum adı güncelleme doğrulayıcıları reddeder). Development'ta `seed` davranışı değişmedi (dev kimlikleri `DevTestVeterinarianSeeder` ayrı kalır).

**Platform yöneticisi müşteri verisine kendiliğinden erişemez.** PlatformAdmin yetkileri yalnız oturumdaki kiracı bağlamında geçerlidir (JWT `tenant_id` = "Platform"); başka kiracının klinik/verisi `404` döner (`ClinicsGetByIdIdorIntegrationTests`). Platform kiracısı abonelikli olmadığından yazma komutları (klinik, müşteri, davet vb.) `Subscriptions.NotFound` ile reddedilir. `platform_admin` claim'i JWT'ye yazılır ama API'de başka yerde okunmaz (kodda doğrulandı).

**Destek erişimi:** Hesap tek kiracılıdır; platform yöneticisi hesabı müşteri kiracısına davet edilemez (`Invites.UserBelongsToAnotherTenant`). Destek gerekirse klinik sahibi, destek için ayrı bir e-posta hesabını mevcut davet akışıyla (`CreateInvite`, atanabilir rol) üye yapar, iş bitince `RemoveMember` ile çıkarır; davet kayıtları izdir. Uç nokta yolları ve arayüz adımları bu belgede doğrulanmadı — doğrulanacak.

**Geçiş notu (önceki sürüm):** Önceki sürümde `create-platform-admin` ile "Varsayılan Kiracı"ya bağlanmış bir platform yöneticisi (geliştirme ortamı) olabilir. Komut var olan e-postaya dokunmadığı için o hesap otomatik taşınmaz; yeni "Platform" kiracısına geçmek için hesap silinip komut yeniden çalıştırılır ya da `UserTenants` satırı elle yeni kiracıya çevrilir (üretimde bu sürümden önce komut çalıştırılmadıysa geçiş gerekmez).

## Günlük (log) ve telemetri — karar

Hosting hedefi yönetilen bulut (Azure). Üretim şablonunda günlük **yalnız konsola (stdout) yapılandırılmış JSON** olarak yazılır (`CompactJsonFormatter`); platform (App Service / Container Apps) stdout'u toplar ve Log Analytics / Application Insights'a aktarır. Dosya sink'i varsayılan olarak kapalıdır (Linux `/var/log` ve Windows yol farkı ortadan kalktı); gerekirse yapılandırmayla eklenir, ör. `Serilog__WriteTo__1__Name=File`, `Serilog__WriteTo__1__Args__path=<yol>` (`Serilog.Sinks.File` pakette mevcut). OpenTelemetry (iz + metrik) OTLP ile yalnız `OTEL_EXPORTER_OTLP_ENDPOINT` tanımlıysa açılır (Development'ta yerel `http://localhost:4317` varsayılanı korunur); Azure Monitor için OTLP toplayıcı/Application Insights OTLP uç noktası verilir. Application Insights'a doğrudan SDK bağlama bu işte yapılmadı.

## İzin değişim damgası (SEC-002)

Yetki denetimi access token içindeki `permission` claim'lerine bakar. Yetkisi alınan kullanıcının eldeki token'la çalışmaya devam etmemesi için (güvenlik raporu O6, kullanıcı kararı 2026-11-09):

- **Damga:** erişimi değiştiren her komut commit sonrası `IPermissionCacheInvalidator`'ı çağırır; bu çağrı izin önbelleğini düşürür ve kullanıcıya "izinleri şu an değişti" damgası yazar (`PermissionChangeStamps`, kural tek yerde). Komutlar: kiracı üyesine rol ekleme/çıkarma (`AssignTenantMemberRole`, `RemoveTenantMemberRole`), yönetim API'sinden rol atama/kaldırma (`AssignUserOperationClaim`, `RemoveUserOperationClaim`, `AdminAddUserClaim`, `AdminRemoveUserClaim`; platform yöneticisi rolü dahil), rolün izin listesi (`AddPermissionToClaim`, `RemovePermissionFromClaim`; o roldeki tüm kullanıcılar), izin tanımı (`UpdatePermission`, `DeletePermission`), üyelikten çıkarma (`RemoveTenantMember`), klinik ataması ve kaldırma (`AssignTenantMemberClinic`, `RemoveTenantMemberClinic`). Kullanıcıyı pasife alma komutu kodda yok; eklendiğinde aynı çağrıyı yapmalıdır.
- **Denetim:** personel JWT şemasında `OnTokenValidated` (`PermissionChangeStampValidation`): token'ın `iat` değeri (saniye) damgadan küçükse kimlik doğrulama başarısız, yanıt `401` (`WWW-Authenticate: Bearer error="invalid_token"`). Eşitlik kabul edilir (aynı saniyede yenilenen token reddedilseydi yenileme döngüye girerdi); damgayla aynı saniyede üretilmiş eski bir token'ın kabul edilme payı ihmal edilir. `sub` ya da `iat` olmayan token reddedilir. Hasta sahibi portalı şeması ve dış API anahtarı bu denetimin dışındadır.
- **İstemci:** frontend 401'de bir kez sessizce yeniler ve isteği tekrarlar (`core/api/auth.interceptor.ts`; eşzamanlı yenilemeler `AuthService.refreshSession` içinde tek istekte birleşir); yeni token güncel izinle gelir. Frontend değişikliği gerekmedi. Üyelikten çıkarılan kullanıcının yenilemesi `Auth.TenantNotMember` ile reddedilir, oturum kapanır.
- **Süreç başlangıcı genel damgadır:** API açılışından önce üretilmiş tüm token'lar 401 alır (damgalar bellekte olduğundan yeniden başlatmada kaybolur; açık kalmasın diye). Her yeniden başlatma ya da deploy sonrası açık oturumlar bir kez sessizce yenilenir.
- **Seed yolu:** `DbMigrator seed` damga yazmaz; seed sonrası API süreci yeniden başlatılırsa genel damga devreye girer.
- **Damganın ömrü:** `Jwt:ExpMinutes` kadar tutulur; damgadan önce üretilmiş token'lar bu süre içinde zaten dolar.
- **Tek API süreci varsayımı:** damga `IMemoryCache`'te, süreç belleğindedir; süreçler arasında paylaşılmaz. Birden fazla API süreci (yatay ölçek, aynı anda iki örnek, iki sürümün birlikte çalıştığı geçiş) çalıştırılırsa bir süreçte yazılan damgayı diğeri görmez ve eski token orada `Jwt:ExpMinutes` (30 dk) boyunca geçerli kalır. O gün damga paylaşılan önbelleğe (ör. Redis, `IDistributedCache`) ya da kullanıcı satırında bir sütuna (ör. `Users.PermissionsChangedAtUtc`, migration gerekir) taşınmalı; değişiklik `PermissionChangeStamps` ile sınırlı kalır. Bugünkü kurulumun tek süreç olduğu bu işte doğrulanmadı.

**Token ömrü:** `Jwt:ExpMinutes` = **30** (`appsettings.json`, `appsettings.Production.json`, `JwtOptions` varsayılanı; önceki değer 60). Tek süreç varsayımı bozulursa gecikmenin üst sınırı budur.

## İzin değişince oturumlar (`RevokeSessionsOnPermissionChange`)

Production'da **true** (kullanıcı kararı 2026-10-13); Development şablonunda ve kod varsayılanında false kalır (yerel geliştirme ve testler etkilenmez). Davranış koddan doğrulandı:

- **Hangi olay:** yalnız yönetim API'sindeki dört komut: rolün izin listesine izin ekleme/çıkarma (`operation-claim-permissions`: `AddPermissionToClaim`, `RemovePermissionFromClaim`) ve izin tanımını güncelleme/silme (`UpdatePermission`, `DeletePermission`).
- **Hangi oturumlar:** olaydan etkilenen kullanıcıların (o role ya da izne sahip olanlar) **tüm** etkin refresh token'ları iptal edilir (`RevokeAllByUserAsync`, neden `logout_all`; tüm cihazlar). Başka kullanıcılara dokunulmaz.
- **Yeniden giriş etkisi (SEC-002 sonrası):** izin değişim damgası access token'ı hemen 401'e düşürür; refresh token da iptal edildiği için yenileme başarısız olur ve kullanıcı **hemen** yeniden giriş yapar (önceden access token süresi dolana kadar, en geç 60 dk sonra). Ayar false olsaydı kullanıcı sessizce yenileyip güncel izinle devam ederdi. Damga gecikmeyi kapattığından bu ayarın gerekliliği yeniden değerlendirilebilir (ürün kararı, açık).
- **Tetiklemeyen yollar:** `DbMigrator seed` (rol-izin bağları doğrudan veritabanına yazılır, oturum iptali yapılmaz) ve kullanıcıya rol atama/çıkarma. Seed sonrası kullanıcıların yeni izinleri alması için token yenilemesi gerekir (bkz. "İzin değişim damgası", seed yolu).
- **Yan etki:** yönetici izin düzenlerken ilgili kullanıcılar yeniden giriş yapar; toplu izin düzenlemelerini mesai dışına bırakın.
- **Kalıcılık düzeltmesi (SEC-002 ile):** `AddPermissionToClaim` ve `RemovePermissionFromClaim` önceden veritabanına yalnız bu ayar true iken (refresh deposunun aynı `AppDbContext` üzerindeki `SaveChanges` çağrısıyla) yazılıyordu; ayar false iken değişiklik kaybolurdu. Artık iki komut da değişikliği kendisi kaydeder. Aynı eksik yönetim API'sindeki `RemoveUserOperationClaim`'de de vardı (rol kaldırma hiç kaydedilmiyordu); düzeltildi.

## Düzeltme izinleri (rol-izin matrisi)

Yönetici düzeyi (ADR-012) = Admin, Owner, ClinicAdmin. Tüm `*.Correct` izinleri yalnız bu rollerdedir; kaynak `RolePermissionBindings.Map` (testler: `CorrectionPermissionMatrixTests`, seed: `RolePermissionBindingSeederTests`).

| İzin | Admin | Owner | ClinicAdmin | Veteriner | Sekreter |
|---|:-:|:-:|:-:|:-:|:-:|
| `Charges.Correct` (ücret düzeltme/iptal) | ✔ | ✔ | ✔ | – | – |
| `Payments.Correct` (ödeme düzeltme/iptal) | ✔ | ✔ | ✔ | – | – |
| `Administrations.Correct` (uygulama iptali) | ✔ | ✔ | ✔ | – | – |
| `StockMovements.Correct` (stok hareketini geri alma) | ✔ | ✔ | ✔ | – | – |
| `StockMovements.Create` (hareket ekleme) | ✔ | ✔ | ✔ | – | ✔ |

Uygulama iptalinin yazdığı otomatik ters hareket `StockMovements.Correct` istemez: yetki ucun izninde (`Administrations.Correct`) aranır, stok iadesi iç komutla yapılır. `StockMovements.Correct` yalnız `POST /stock-movements/{id}/reverse` ucunu açar. `Administrations.Correct` Veteriner'den çıkarıldığı için seed, `RolePermissionBindings.Revoked` ile eski bağı siler (yalnız bu çift; diğer manuel atamalara dokunmaz). Yeni izin eklendiğinden `DbMigrator seed` gerekir (yalnız birleşik `main`'den).

## Gözden geçirme notları

- Rate limit Production'da açık (`RateLimiting:Enabled` varsayılan true; genel 200/dk/IP; login/refresh için ayrı politikalar). Ters vekil arkasında istemci IP'si için `ForwardedHeaders:KnownProxies` (IP dizisi) ayarlanabilir; boşken yalnız loopback'e güvenilir, canlı değeri henüz tanımlanmadı (güvenlik raporu D6). `KnownNetworks` yok.
- SMTP: hata ayıklama günlüğünden kullanıcı adı, gönderen adresi ve parola uzunluğu çıkarıldı; yalnız "var/yok" bilgisi yazılır.
