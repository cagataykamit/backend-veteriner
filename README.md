# Backend.Veteriner

**Veteriner** ürünü için ASP.NET Core backend’i. Şu aşamada depo; kimlik doğrulama, yetkilendirme, kalıcılık, gözlemlenebilirlik ve platform API **çekirdeğini** içerir. Sektörel domain (ör. klinik, hasta, randevu) ayrı modüller olarak eklenecek şekilde tasarlanmıştır.

## Bu repo nedir?

**Backend.Veteriner**, veteriner iş kurallarının üzerine inşa edileceği **çekirdek + platform** katmanıdır. Ürün bounded context’leri (klinikler, hayvanlar, randevular vb.) bu çekirdeğe eklenir; aşağıda listelenen örnek modüller şu an repoda **yoktur**.

## Ne içerir?

| Alan | Açıklama |
|------|----------|
| **Auth / Users** | Giriş, yenileme, oturum yönetimi, kullanıcı yaşam döngüsü (platform seviyesi) |
| **JWT / refresh / sessions** | Erişim ve yenileme token’ları; oturum listeleme / iptal |
| **Permission tabanlı authorization** | İzin kataloğu, policy handler, operasyon claim’leri |
| **Result + CQRS + MediatR** | `Result` tabanlı uygulama yanıtları; command/query pipeline (validation, audit, logging vb.) |
| **Audit / correlation / outbox** | İstek korelasyonu, denetim izi yazımı, transactional outbox altyapısı |
| **Persistence** | EF Core, `AppDbContext`, konfigürasyonlar, repository desenleri, migration tabanı |
| **Seeding** | İzin ve temel platform verisi için seed akışları |
| **Middleware / Swagger / Health** | API sözleşmesi, sürümleme, sağlık kontrolleri, ortak middleware |
| **Platform admin controller’ları** | Kullanıcı, izin, claim ve outbox gibi **platform** uçları |
| **Veteriner domain iskeleti** | `Tenant`, `Clinic`, `Client`, `Pet`, `Appointment` agregatları; Application’da Create + GetById + sayfalı liste sorguları (API uçları sonraki adımda) |

## Ne içermez?

Şu an kaynak ağaçta **yok** (gelecekte ayrı modül veya ayrı proje olarak eklenebilir):

- **Veteriner iş domain’i** — klinik, pet, randevu, fatura vb. (henüz implementasyon yok)
- Eski şablondan kalan örnek modül isimleri: **Contact**, **Organization**, **Tasks** (kod veya placeholder repository kalmadı)

`docs/` altındaki bazı analiz belgeleri geçmiş şablon örneklerine (ör. Contact) referans verebilir; canlı kod için `src/` esas alınmalıdır ([`docs/README.md`](docs/README.md)).

## Mimari yaklaşım

- **Katmanlı yapı:** `Backend.Veteriner.Api` → `Application` → `Domain` ← `Infrastructure`
- **Uygulama katmanı:** MediatR ile command/query; FluentValidation; ortak pipeline davranışları
- **Domain:** Agregatlar, domain event arabirimleri, `Result` / hata modeli (ürün domain’i burada minimal tutulur)
- **Altyapı:** EF Core, outbox, e-posta gönderimi, JWT, önbellek invalidation vb.

Ayrıntılı kurallar için: [`docs/architecture/foundation-rules.md`](docs/architecture/foundation-rules.md).

## Hızlı başlangıç

```bash
git clone <repo-url>
cd Veteriner
dotnet restore Veteriner.sln
dotnet build Veteriner.sln -c Debug
```

API giriş projesi: `src/Backend.Veteriner.Api`  
Yerel geliştirmede varsayılan SQL veritabanı adı **`VeterinerDb`** (`appsettings.Development.json` içindeki `ConnectionStrings:DefaultConnection`). İsterseniz **User Secrets** veya ortam değişkenleri ile üzerine yazın; JWT anahtarı için de User Secrets kullanın.

## Test çalıştırma

```bash
dotnet test Veteriner.sln -c Debug
```

- **Birim / uygulama testleri:** `tests/Backend.Veteriner.Application.Tests`, `tests/Backend.Veteriner.Domain.Tests`
- **Entegrasyon testleri:** `tests/Backend.Veteriner.IntegrationTests` (`appsettings.IntegrationTests.json` → LocalDB, varsayılan DB adı **`VeterinerDb_IntegrationTests`**)

## Ürün modülü ekleme (özet)

1. Veteriner domain kodunu `Application` / `Domain` / `Infrastructure` içinde **ayrı klasörler** (veya ileride ayrı derlemeler) ile ekleyin; mevcut çekirdek katman kurallarına uyun.
2. `PermissionCatalog` ve seed’lere ürün izinlerini ekleyin; EF migration’ları ürün modeline göre üretin.
3. API’de ürün controller’larını ekleyin; mevcut JWT ve permission policy yapısı ile hizalayın.

Operasyonel adımlar için: [`docs/architecture/new-module-checklist.md`](docs/architecture/new-module-checklist.md).

## Park edilen konular

- **`DomainEventOutboxInterceptor`:** Şu an bilinçli olarak etkinleştirilmemiştir. İlk **gerçek domain event** use-case’inde (serileştirme, tip kaydı, outbox sözleşmesi ile birlikte) ele alınacaktır. Detay: [`docs/architecture/foundation-rules.md`](docs/architecture/foundation-rules.md) içindeki ilgili bölüm.

## Güvenlik notu

**Production secret’ları (şifreler, JWT signing key, SMTP parolası, üretim connection string’leri) bu repoda tutulmaz.**  
`appsettings.Production.json` yalnızca **şema/örnek** içindir; `Jwt:Key`, `Smtp:Pass`, connection string parolaları ve benzeri değerler ortamda doldurulmalıdır.  
Yapılandırma; ortam değişkenleri, secret store, User Secrets (yalnızca geliştirme) ve güvenli CI değişkenleri ile sağlanmalıdır.

---

*Ek mimari notlar ve geçmiş analizler `docs/` altındaki diğer markdown dosyalarında bulunabilir; canonical kurallar `docs/architecture/foundation-rules.md` dosyasındadır.*
