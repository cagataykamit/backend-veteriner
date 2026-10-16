# Sürümlü hukuki metin kabulü — API sözleşmesi (ADR-029, TRIAL-009)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-029-surumlu-hukuki-kabul.md` (metinler: `legal/`, ADR-024). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (`BACKEND-CONTRACT-STANDARD.md` §9).

> **Durum:** Uygulandı (kod + test), dal `feat/trial009-backend`. Migration `20261016023233_AddLegalAcceptances` üretildi, **hiçbir paylaşılan/geliştirme veritabanına uygulanmadı**. **Yeni izin yok**, seed gerekmez (`/me` uçları gibi yalnız oturum ister).

## 1) Belgeler ve güncel sürüm

| `documentType` | Belge | Kim kabul eder |
|---|---|---|
| `Terms` | Kullanım Şartları | Herkes |
| `Privacy` | Gizlilik Politikası | Herkes |
| `DataProcessing` | Veri İşleme Sözleşmesi | Yalnız klinik yöneticisi: kiracı `Admin` veya `Owner` operation claim'i |

- Güncel sürümler **tek yerde**: `Application/LegalAcceptances/LegalDocumentCatalog.CurrentVersions` (şu an üçü de `"0.1"`, taslak). "0.x" sürüm hukuki onay bekliyor demektir.
- Bir sürüm değiştirilince (ör. `"0.1"` → `"1.0"`) o belgeyi gerektiren **herkes yeniden kabul eder**; eski kabuller silinmez.
- Kabul kuralı da aynı sınıfta: `RequiredFor` (rol → belgeler), `Pending`, `EnsureAcceptsCurrent`. `ClinicAdmin`, `Veteriner`, `Sekreter`, `PlatformAdmin` ve claim'i olmayanlar yalnız `Terms` + `Privacy`.
- Aydınlatma ve açık rıza klinik–müşteri belgeleridir (`KVKK_CONSENT_API_CONTRACT.md`), bu mekanizmaya girmez.

## 2) Kabul kaydı (`LegalAcceptances` tablosu)

| Sütun | Anlam |
|---|---|
| `Id` | guid |
| `UserId` | Kabul eden kullanıcı |
| `TenantId` | Kabul anındaki kiracı (klinik aboneliği) |
| `DocumentType` | int (`Terms=0`, `Privacy=1`, `DataProcessing=2`) |
| `Version` | Kabul edilen sürüm (en fazla 20 karakter) |
| `AcceptedAtUtc` | UTC kabul zamanı |

- Yalnız eklenir; güncelleme/silme yoktur, her kabul yeni satır. Yabancı anahtar yok (AuditLogs gibi; kullanıcı/kiracı silinse de iz kalır). İndeks: `(UserId, DocumentType)`, `TenantId`.
- **IP/cihaz bilgisi tutulmaz** (veri azaltma). Not: `POST /me/legal-acceptances` mevcut denetim kaydına (AuditBehavior) yazılır; denetim kaydı her denetlenen istekte olduğu gibi IP/UA içerir.
- **Mevcut kullanıcılar** için geriye dönük kayıt üretilmez; ilk girişte bekleyen kabul olarak görürler (ADR-029 madde 6).
- Kullanıcı bir belgenin **güncel sürümünü** daha önce kabul ettiyse o belge bekleyen değildir; eski sürüm kabulü sayılmaz.

## 3) `GET /api/v1/public/legal-documents` (anonim)

Hukuki sayfalar ve kayıt formu görünen sürümü buradan alır.

```json
[
  { "documentType": "Terms", "version": "0.1" },
  { "documentType": "Privacy", "version": "0.1" },
  { "documentType": "DataProcessing", "version": "0.1" }
]
```

## 4) `GET /api/v1/me/legal-acceptances`

Oturum (`[Authorize]`) yeterli, izin gerekmez. Yanıt **200**:

```json
{
  "requiresAcceptance": true,
  "pendingDocuments": [
    { "documentType": "Terms", "version": "0.1" },
    { "documentType": "Privacy", "version": "0.1" }
  ]
}
```

`requiresAcceptance = true` iken istemci paneli kabul penceresiyle engeller. Sunucu diğer uçları engellemez (istemci akışı; ADR-029 madde 5).

| Kod | HTTP | Durum |
|---|---|---|
| — | 401 | Oturum yok |

## 5) `POST /api/v1/me/legal-acceptances`

```json
{ "documents": [ { "documentType": "Terms", "version": "0.1" }, { "documentType": "Privacy", "version": "0.1" } ] }
```

- İstemci `pendingDocuments` listesini gösterdiği sürümlerle geri gönderir.
- Her bekleyen belge için **yeni satır** yazılır; zaten güncel olan veya kullanıcıya gerekmeyen (ör. yönetici olmayanın `DataProcessing`) belgeler yok sayılır. Bekleyen yoksa yazmadan 200 (tekrar gönderim güvenli).
- Abonelik salt okunur/iptal iken de çalışır (`IIgnoreTenantWriteSubscriptionGuard`); kiracı bağlamı (JWT `tenant_id`) gerekir.
- Denetim: `Account.LegalAcceptance` (hedef `SelfService`), başarılı ve başarısız denemeler.

Yanıt **200**: `{ "requiresAcceptance": false, "pendingDocuments": [] }`.

| Kod | HTTP | Durum |
|---|---|---|
| (validation) | 400 | `documents` boş/yok, aynı belge iki kez, `version` boş/uzun |
| (model doğrulama) | 400 | Tanınmayan `documentType` metni |
| `LegalAcceptances.Required` | 400 | Bekleyen bir belge gönderilmedi (ör. yönetici `DataProcessing`'i atladı) |
| `LegalAcceptances.VersionOutdated` | 409 | Gönderilen sürüm güncel değil; istemci `GET /me/legal-acceptances` ile yenileyip güncel metni tekrar kabul ettirir |
| `Tenants.ContextMissing` | 400 | Token'da kiracı yok |
| — | 401 | Oturum yok |

## 6) Kayıt akışlarında zorunlu kabul (sunucu doğrular)

İki uca yeni alan eklendi: `acceptedLegalDocuments` (aynı öğe şekli `{ documentType, version }`). Doğrulama **hesap oluşturulmadan önce** yapılır; başarıda gereken her belge için kabul satırı yazılır (tenant = yeni/davet kiracısı).

### `POST /api/v1/public/owner-signup`

Kayıt olan klinik yöneticisidir (`Admin`): `Terms`, `Privacy`, `DataProcessing` güncel sürümleri zorunlu.

```json
{
  "planCode": "Basic", "tenantName": "…", "clinicName": "…", "clinicCity": "…", "email": "…", "password": "…",
  "acceptedLegalDocuments": [
    { "documentType": "Terms", "version": "0.1" },
    { "documentType": "Privacy", "version": "0.1" },
    { "documentType": "DataProcessing", "version": "0.1" }
  ]
}
```

### `GET /api/v1/public/invites/{token}` → yeni alan `requiredLegalDocuments`

Davetteki role göre yeni hesabın kabul etmesi gerekenler (`Admin` daveti üçü, diğer roller `Terms` + `Privacy`), `[{ documentType, version }]`.

### `POST /api/v1/public/invites/{token}/signup-and-accept`

```json
{ "password": "…", "acceptedLegalDocuments": [ { "documentType": "Terms", "version": "0.1" }, { "documentType": "Privacy", "version": "0.1" } ] }
```

`requiredLegalDocuments` belgelerinin güncel sürümü zorunlu; fazlası yok sayılır.

### Ortak hatalar (iki kayıt ucu)

| Kod | HTTP | Durum |
|---|---|---|
| `LegalAcceptances.Required` | 400 | `acceptedLegalDocuments` yok veya gereken bir belge eksik |
| `LegalAcceptances.VersionOutdated` | 409 | Gönderilen sürüm güncel değil (sayfa eski) |

> **Geriye uyumsuz:** `acceptedLegalDocuments` göndermeyen eski istemcilerin kaydı 400 alır. Frontend aynı sürümle (`feat/trial009-frontend`) yayımlanmalıdır.

### Kapsam dışı

- `POST /public/invites/{token}/accept` (oturum açık mevcut kullanıcı) kabul istemez; kullanıcı yeni rolüne göre eksik belgeyi panelde bekleyen kabul olarak görür (ör. `Admin` rolü alan kullanıcıya `DataProcessing`).
- E-imza, PDF kopyası, kabul geçmişi ekranı (ADR-029 madde 7).
