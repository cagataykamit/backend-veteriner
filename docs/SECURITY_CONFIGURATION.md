# Güvenlik yapılandırması (üretim/staging)

Ayarlar `appsettings.{Environment}.json` veya ortam değişkeniyle verilir (iç içe anahtar: `AllowedOrigins__0`).

| Ayar | Davranış |
|---|---|
| `AllowedOrigins` (dizi) | CORS için izinli frontend origin'leri. **Production/Staging'de boşsa uygulama başlamaz.** Development/test ortamında boşsa `http://localhost:4200`. |
| `AllowedHosts` (`;` ile ayrılmış) | Host süzmesi. **Production/Staging'de `*` veya boşsa uygulama başlamaz.** Taban değer yalnız loopback adresleridir; `appsettings.Production.json` içinde boştur, ortam değişkeniyle (`AllowedHosts`) doldurulmalıdır. |
| `Swagger:Enabled` (bool) | Swagger yalnız Development'ta açıktır; başka ortamda gerekiyorsa `true` verilir (varsayılan kapalı). |

Tüm yanıtlara `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` ve `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` (Swagger yolu hariç) eklenir. HSTS Development dışında açıktır (`UseHsts`).

## Yerel geliştirme ayarları

`src/Backend.Veteriner.Api/appsettings.Development.json` artık git'te izlenmez (`.gitignore`). Başlangıç noktası: `appsettings.Development.json.example`. Gizli değerler (bağlantı dizesi parolası, ödeme sağlayıcı anahtarları, JWT anahtarı) `dotnet user-secrets` ile verilir:

```
dotnet user-secrets set "Jwt:Key" "<değer>" --project src/Backend.Veteriner.Api
dotnet user-secrets set "Billing:Iyzico:ApiKey" "<değer>" --project src/Backend.Veteriner.Api
dotnet user-secrets set "Billing:Iyzico:SecretKey" "<değer>" --project src/Backend.Veteriner.Api
```

## Açılış doğrulaması

Development, Production ve Staging ortamlarında şu ayarlar yoksa API başlamaz (hata iletisi yalnız ayar adını söyler, değer yazmaz): `Jwt:Key` (en az 32 karakter), `ConnectionStrings:DefaultConnection`, `ConnectionStrings:QueryConnection`, `Smtp:Pass`, `AllowedOrigins`, `AllowedHosts`. Production/Staging'de ayrıca `Smtp:Host` yer tutucu (`smtp.example.com`) kalamaz. Kural tek yerde: `StartupConfigurationValidator`, `HostSecurityConfiguration`. Test ortamları (`IntegrationTests`, `LoadTest`) kendi yapılandırmasını getirir ve bu denetimin dışındadır.

## İlk yönetici (üretim)

`seed` ve `all` komutları **Development dışında** sabit parolalı `admin@example.com` hesabını, varsayılan kiracıyı, seed kliniği ve aboneliği OLUŞTURMAZ; yalnız izinler, tür/ırk kataloğu, aşı tanımları ve rol bağları kurulur. İlk PlatformAdmin hesabı için (önce `seed` çalıştırılmış olmalı):

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

## İzin değişince oturumlar (`RevokeSessionsOnPermissionChange`)

Production'da **true** (kullanıcı kararı 2026-10-13); Development şablonunda ve kod varsayılanında false kalır (yerel geliştirme ve testler etkilenmez). Davranış koddan doğrulandı:

- **Hangi olay:** yalnız yönetim API'sindeki dört komut: rolün izin listesine izin ekleme/çıkarma (`operation-claim-permissions`: `AddPermissionToClaim`, `RemovePermissionFromClaim`) ve izin tanımını güncelleme/silme (`UpdatePermission`, `DeletePermission`).
- **Hangi oturumlar:** olaydan etkilenen kullanıcıların (o role ya da izne sahip olanlar) **tüm** etkin refresh token'ları iptal edilir (`RevokeAllByUserAsync`, neden `logout_all`; tüm cihazlar). Başka kullanıcılara dokunulmaz.
- **Yeniden giriş etkisi:** o ana kadar verilmiş access token, süresi bitene kadar (`Jwt:ExpMinutes` = 60 dk) eski izinlerle geçerlidir, çünkü yetki denetimi yalnız token içindeki izin claim'lerine bakar (güvenlik raporu O6). Süre dolunca yenileme (refresh) başarısız olur ve kullanıcı yeniden giriş yapar; yeni girişte güncel izinleri alır. Yani etki anlık değil, en geç 60 dk içinde olur; geri alınan yetkinin hemen kapanması için ayrıca token ömrü kısaltılmalı (O6, ürün kararı).
- **Tetiklemeyen yollar:** `DbMigrator seed` (rol-izin bağları doğrudan veritabanına yazılır, oturum iptali yapılmaz) ve kullanıcıya rol atama/çıkarma. Seed sonrası kullanıcıların yeni izinleri alması için çıkış-giriş yapması (ya da token yenilemesi) gerekir.
- **Yan etki:** yönetici izin düzenlerken ilgili kullanıcılar yeniden giriş yapar; toplu izin düzenlemelerini mesai dışına bırakın.

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

- Rate limit Production'da açık (`RateLimiting:Enabled` varsayılan true; genel 200/dk/IP; login/refresh için ayrı politikalar). Ters vekil arkasında istemci IP'si için `KnownProxies/KnownNetworks` hâlâ tanımsız (güvenlik raporu D6).
- SMTP: hata ayıklama günlüğünden kullanıcı adı, gönderen adresi ve parola uzunluğu çıkarıldı; yalnız "var/yok" bilgisi yazılır.
