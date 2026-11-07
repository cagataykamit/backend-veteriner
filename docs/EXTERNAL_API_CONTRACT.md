# Dış API (salt okunur) ve API anahtarları sözleşmesi — TIER-009 ilk dilimi

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-009` ("Paket: Kurumsal. Kiracıya özel anahtarla, belgeli ve sınırlı dış API. KVKK ve kiracı izolasyonu tasarımda ele alınır."), ADR-035 (paketler), ADR-040 (kiracı izolasyonu), `roadmap/HUKUKI-ONAY-LISTESI.md`. İlgili: `PLAN_FEATURES_API_CONTRACT.md` (paket kuralı ve 403 gövdesi), `AUTH_TENANT_CONTRACT.md` (panel JWT'si), `SECURITY_CONFIGURATION.md`.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-009-api-erisimi`. Migration `AddApiKeysAndChangeTracking` (yalnız yerel DB). **Yeni izin** `ApiKeys.Manage` (Admin, Owner). **Yeni paket özelliği** `ExternalApi` (yalnız Kurumsal). Yazma ucu yoktur. §10'daki maddeler **çalışma varsayımıdır, kullanıcı onayı bekliyor.** Swagger'ın üretimde kapalı olması değişmedi (`Swagger:Enabled`); bu belge dış geliştiriciye verilecek sözleşmedir.

---

## 1) Kavram

- Klinik (kiracı) panelden **API anahtarı** üretir ve kendi aracına verir. Araç anahtarla kiracının müşteri, hayvan, randevu ve aşı listelerini **okur**.
- Kiracı **yalnız anahtardan** çözülür. İstekte kiracı parametresi yoktur; `tenantId` gönderilirse 400 döner. ADR-040 kiracı filtresi bu yolda anahtarın kiracısıyla çalışır, sorgular ayrıca kiracı koşulu taşır (ikinci savunma hattı).
- Anahtar kiracı genelidir: tüm klinikleri (şubeleri) okur. Kayıtlarda `clinicId` vardır; şube kısıtlı anahtar bu dilimde yoktur.

## 2) Kimlik doğrulama

| Konu | Kural |
|---|---|
| Başlık | `X-Api-Key: <anahtar>`; tek değer. |
| Panel oturumu | Dış uçta `Authorization` başlığı varsa (panel JWT'si dahil, anahtarla birlikte gönderilse de) istek **401 `ExternalApi.BearerNotAccepted`** alır. |
| Anahtar panelde | Panel uçları yalnız personel JWT'sini doğrular; `X-Api-Key` ya da anahtarın `Bearer` olarak gönderilmesi panelde 401'dir. Ayrı kimlik şemaları: personel `Bearer`, portal `PortalBearer`, dış API `ExternalApiKey`. |
| Biçim | `vtk_` + 8 karakter ön ek + `_` + 43 karakter (32 rastgele baytın base64url hâli); toplam 56 karakter. Örnek biçim: `vtk_xxxxxxxx_<43 karakter>` (gerçek anahtar belgeye yazılmaz). |
| Saklama | Sunucuda yalnız anahtarın tamamının **SHA-256 özeti** ve ilk 12 karakteri (ön ek, tanıma için) saklanır. Anahtar 256 bit rastgele olduğu için yavaş parola özeti gerekmez. Açık metin yalnız oluşturma yanıtında **bir kez** döner (`Cache-Control: no-store`); sunucu onu yeniden üretemez. |
| Doğrulama | Ön ek tüm kiracılarda benzersizdir; kayıt ön ekle bulunur (`ITenantScope.AsSystem`, mimari testte izinli), özet `CryptographicOperations.FixedTimeEquals` ile karşılaştırılır. Ön ek bulunmasa da özet hesaplanıp karşılaştırılır. |
| Son kullanım | Başarılı doğrulamada `lastUsedAtUtc` yazılır; en çok dakikada bir (`ApiKey.LastUsedPrecision`). |
| Kural yeri | Biçim, üretim ve karşılaştırma `ApiKeySecret`; doğrulama, kiracı bağlama ve paket kapısı `ExternalApiAccess`; şema `ExternalApiKeyAuthenticationHandler`. |

## 3) Kapsam, paket ve abonelik

| Kapsam | Uç |
|---|---|
| `clients:read` | `GET /api/external/v1/clients` |
| `pets:read` | `GET /api/external/v1/pets` |
| `appointments:read` | `GET /api/external/v1/appointments` |
| `vaccinations:read` | `GET /api/external/v1/vaccinations` |

- Anahtar oluşturulurken en az bir kapsam seçilir; yalnız seçilenler geçer. Kapsamı olmayan uç **403 `ExternalApi.ScopeMissing`**.
- Paket `ExternalApi` (yalnız Kurumsal, `SubscriptionPlanCatalog`) içermiyorsa **403 `Subscriptions.FeatureNotInPlan`** (`requiredPlanCode: "Premium"`). Deneme seçilen pakete göre davranır (Kurumsal denemesi açık).
- Pasif kiracı, süresi dolmuş deneme, salt okunur ya da iptal edilmiş abonelikte dış API kapalıdır: 403 (`Tenants.TenantInactive`, `Subscriptions.TenantReadOnly`, `Subscriptions.TenantCancelled`). Paneldeki okuma bu kuraldan bağımsız açık kalır.
- Paket düşünce anahtarlar **silinmez**, listede görünür ve iptal edilebilir; istekleri 403 döner. Paket geri yükselince aynı anahtar yeniden çalışır.
- Kapı denetleyici sınıfındadır (`[ExternalApiAccess]`); bu sınıfa eklenen yeni uç kapıdan kendiliğinden geçer.

## 4) Listeleme, sayfa ve `updatedSince`

Dört uç aynı parametreleri alır:

| Parametre | Tür | Kural |
|---|---|---|
| `updatedSince` | ISO 8601 tarih-saat | Dahil alt sınır. Saat dilimi yoksa UTC sayılır. Kayıt `changedAtUtc >= updatedSince` ise gelir. |
| `cursor` | opak metin | Önceki yanıttaki `nextCursor` aynen gönderilir; bozuksa 400 `ExternalApi.Validation`. |
| `pageSize` | tamsayı | Varsayılan 100, **en çok 200**; aralık dışı değer 1–200'e çekilir. |

Yanıt:

```json
{ "items": [ … ], "pageSize": 100, "nextCursor": "NjM5MDAwMDAwMDAwMDAwMDAwLjAwMDAw…" }
```

- Sıra (`changedAtUtc`, `id`) artandır; `nextCursor` null ise son sayfadır. İmleç son kaydın bu ikilisidir (keyset), sayfa numarası yoktur: gezinirken değişen kayıt sona taşınır, atlanmaz.
- `changedAtUtc` kaydın eklendiği ya da değiştiği andır; kaydetme anında altyapı damgalar (`ChangeTrackedSaveChangesInterceptor`, `IChangeTracked`). Migration öncesi satırlar: müşteri ve aşıda `UpdatedAtUtc ?? CreatedAtUtc`, hayvan ve randevuda migration anı.
- Önerilen eşitleme: ilk seferde `updatedSince` olmadan tüm sayfalar; sonraki seferlerde son görülen en büyük `changedAtUtc` ile `updatedSince` (aynı anı taşıyan kayıtlar yeniden gelebilir, `id` ile tekilleştirilir).
- **Silinen kayıt bildirilmez** (bu dilimde silme akışı yok; bkz. §10).

## 5) Verilen alanlar (KVKK asgari veri)

Alanlar varlıktan doğrudan değil, tek tek seçilerek verilir (`ExternalReadFeatures.cs`). Bu tablo dışında alan eklemek sözleşme değişikliğidir.

**Müşteri** (`clients`; peşin satış müşterisi listelenmez)

| Alan | Tür | Not |
|---|---|---|
| `id` | uuid | |
| `fullName` | string | |
| `phone` | string? | |
| `email` | string? | |
| `createdAtUtc` | tarih-saat | |
| `changedAtUtc` | tarih-saat | |

**Hayvan** (`pets`)

| Alan | Tür | Not |
|---|---|---|
| `id`, `clientId` | uuid | |
| `name` | string | |
| `species` | string? | Tür adı |
| `breed` | string? | Katalog ırkı, yoksa serbest metin |
| `gender` | `Male` \| `Female` \| null | |
| `birthDate` | tarih? | |
| `isNeutered` | bool | |
| `status` | `Active` \| `Deceased` | |
| `changedAtUtc` | tarih-saat | |

**Randevu** (`appointments`)

| Alan | Tür | Not |
|---|---|---|
| `id`, `clinicId`, `petId` | uuid | |
| `scheduledAtUtc` | tarih-saat | |
| `durationMinutes` | int | |
| `appointmentType` | `Examination` \| `Vaccination` \| `Checkup` \| `Surgery` \| `Grooming` \| `Consultation` \| `Other` | |
| `status` | `Scheduled` \| `Completed` \| `Cancelled` \| `NoShow` | |
| `changedAtUtc` | tarih-saat | |

**Aşı** (`vaccinations`)

| Alan | Tür | Not |
|---|---|---|
| `id`, `clinicId`, `petId` | uuid | |
| `vaccineName` | string | Uygulama anındaki ad |
| `status` | `Scheduled` \| `Applied` \| `Cancelled` | |
| `appliedAtUtc`, `dueAtUtc` | tarih-saat? | |
| `changedAtUtc` | tarih-saat | |

**Verilmeyenler:** TCKN/VKN, vergi dairesi, unvan ve fatura adresi; müşteri adresi; KVKK rıza kayıtları ve iletişim tercihleri; hayvan notu, uyarı bayrakları ve notu, kilo, renk, mikroçip/pasaport/özel protokol/HBS numaraları, ölüm tarihi; randevu notu, nedeni ve hekim; aşı notu, uygulama yolu ve bölgesi, muayene bağlantısı ve HBS durumu; muayene, tedavi, reçete, laboratuvar, görüntüleme ve yatış içeriği; ücret, ödeme ve bakiye; portal hesabı, oturum ve cihaz verisi; personel kimliği; hiçbir parola, token ya da özet.

## 6) Panel: anahtar yönetimi

Tümü personel JWT'si ve **`ApiKeys.Manage`** ister (yalnız Admin ve Owner; anahtar kiracının tüm müşteri verisini okuduğu için en dar bağ).

| Uç | Gövde / yanıt | Kural |
|---|---|---|
| `GET /api/v1/api-keys` | `{ items: ApiKeyDto[], availableScopes: string[], maxActiveKeys: 10 }` | Her pakette açık. Etkinler önce, yeniden eskiye. |
| `POST /api/v1/api-keys` | `{ name, scopes[] }` → `{ key: ApiKeyDto, plainTextKey }` | Kurumsal ister (403). Ad zorunlu, en çok 100; kapsam en az bir, bilinmeyen kapsam 400 `ApiKeys.Validation`. Kiracı başına en çok 10 etkin anahtar (409 `ApiKeys.LimitReached`). `Cache-Control: no-store`. |
| `POST /api/v1/api-keys/{id}/revoke` | 204 | Her pakette ve salt okunur abonelikte açık (güvenlik işlemi). Geri alınmaz; ikinci iptal 409 `ApiKeys.AlreadyRevoked`; başka kiracının anahtarı 404 `ApiKeys.NotFound`. |

`ApiKeyDto`: `id, name, prefix, scopes[], createdByUserId, createdByName, createdAtUtc, lastUsedAtUtc, isRevoked, revokedAtUtc, revokedByName`. Özet ve gizli kısım hiçbir yanıtta yoktur.

Denetim kaydı: `ApiKey.Create` (hedef `Name=…;Scopes=…`; istek gövdesinde anahtar yoktur) ve `ApiKey.Revoke` (hedef `ApiKeyId=…`). Dış API okumaları bu dilimde denetim kaydına yazılmaz; anahtar başına son kullanım tutulur (§10).

## 7) Sınırlar

| Sınır | Değer | Yer |
|---|---|---|
| İstek | Anahtar başına dakikada 60 (kayan pencere), `ExternalApi:PermitLimitPerMinute` ile 1–600 | `RateLimitingExtensions.ExternalApiPolicy` |
| Bölüm anahtarı | Başlıktaki biçimi geçerli anahtarın ön eki (gizli kısım bölüm anahtarına girmez); biçimsiz/eksik anahtar IP bölümüne düşer | aynı |
| Genel | IP başına `RateLimiting:GlobalPermitLimit` (varsayılan 200/dk) ayrıca geçerli | mevcut |
| Sayfa | En çok 200 kayıt | `ExternalPaging.MaxPageSize` |
| Anahtar | Kiracı başına en çok 10 etkin | `ApiKey.MaxActivePerTenant` |

Sınır aşılırsa 429 `RateLimit.Exceeded`, `Retry-After` başlığı ve `retryAfterSeconds`.

## 8) Hata kodları

Tüm hatalar `application/problem+json`, `code`, `traceId`, `correlationId` alanlarıyla.

| HTTP | `code` | Durum |
|---|---|---|
| 400 | `ExternalApi.Validation` | `tenantId` parametresi gönderildi ya da `cursor` bozuk |
| 401 | `ExternalApi.KeyMissing` | `X-Api-Key` yok (yanıtta `WWW-Authenticate: ApiKey header="X-Api-Key"`) |
| 401 | `ExternalApi.KeyInvalid` | Biçim bozuk, anahtar bulunamadı ya da özet eşleşmedi, birden çok başlık değeri |
| 401 | `ExternalApi.KeyRevoked` | Anahtar iptal edilmiş |
| 401 | `ExternalApi.BearerNotAccepted` | İstekte `Authorization` başlığı var |
| 403 | `ExternalApi.ScopeMissing` | Anahtarın bu uç için kapsamı yok |
| 403 | `Subscriptions.FeatureNotInPlan` | Paket Kurumsal değil (`feature`, `currentPlanCode`, `requiredPlanCode` alanlarıyla) |
| 403 | `Tenants.TenantInactive`, `Subscriptions.TenantReadOnly`, `Subscriptions.TenantCancelled` | Kiracı ya da abonelik kullanılamaz |
| 429 | `RateLimit.Exceeded` | Hız sınırı |

## 9) Örnek istekler

```http
GET /api/external/v1/clients?pageSize=100 HTTP/1.1
Host: api.example.com
X-Api-Key: vtk_xxxxxxxx_<gizli kısım>
```

```http
GET /api/external/v1/appointments?updatedSince=2026-11-01T00:00:00Z&cursor=<önceki nextCursor> HTTP/1.1
X-Api-Key: vtk_xxxxxxxx_<gizli kısım>
```

```bash
curl -H "X-Api-Key: $VETINITY_API_KEY" "https://api.example.com/api/external/v1/vaccinations?updatedSince=2026-11-01T00:00:00Z"
```

```json
{
  "items": [
    { "id": "…", "clinicId": "…", "petId": "…", "vaccineName": "Kuduz", "status": "Applied",
      "appliedAtUtc": "2026-11-02T09:15:00Z", "dueAtUtc": null, "changedAtUtc": "2026-11-02T09:15:03.1234567Z" }
  ],
  "pageSize": 100,
  "nextCursor": null
}
```

Anahtar ortam değişkeninde ya da gizli kasada tutulmalı; adreste (query string) taşınmaz.

## 10) Çalışma varsayımları — kullanıcı onayı bekliyor

1. Anahtar kiracı geneli; şube kısıtlı anahtar yok. Yönetim izni yalnız Admin ve Owner (ClinicAdmin almaz).
2. Kapsamlar kaynak başına okuma; yazma kapsamı yok. Kiracı başına en çok 10 etkin anahtar; anahtarın süresi dolmaz (iptal edilene kadar geçerli).
3. Hız sınırı anahtar başına 60/dk; sayfa en çok 200.
4. Süresi dolmuş deneme, salt okunur ya da iptal abonelikte dış API kapalı (panel okuması açık).
5. Verilen alanlar §5 tablosu; müşteri adresi ve iletişim tercihi dahil değil.
6. Silinen kaydın bildirimi yok; okumalar denetim kaydına yazılmaz, yalnız anahtarın son kullanımı tutulur.
7. Kurumsal denemesi dış API'yi açar (paket kuralı TIER-001 ile aynı).

## 11) Testler

- Domain: `ApiKeyTests` (ad ve kapsam kuralları, özet/ön ek uzunluğu, geri alınmaz iptal, seyrek son kullanım).
- Application: `ApiKeySecretTests` (biçim, tekillik, yalnız tam anahtar, bozuk biçimler), `ExternalPagingTests` (bağlamsız kiracı kapalı, sayfa üst sınırı, UTC, imleç gidiş-dönüşü, bozuk imleç), `ApiKeysPermissionBindingsTests`, `TenantPlanFeatureEvaluatorTests`, `TenantIsolationArchitectureTests` (sistem kapsamı izinli listesi).
- Entegrasyon: `ExternalApiIntegrationTests` — kiracılar arası izolasyon ve `tenantId` reddi; hassas alan ve anahtar malzemesinin yanıtta olmaması, yalnız özetin saklanması, denetim; yanlış/eksik/iptal anahtar 401; paket düşünce 403 ve anahtarın kalması, alt pakette oluşturma 403, iptal açık; panel JWT'si dış uçta ve anahtar panel ucunda 401, izinsiz yönetim 403; kapsam 403; imleç ve `updatedSince`; anahtar başına hız sınırı (429, diğer anahtar etkilenmez).

## 12) Bilinen sınırlar

- Etkin anahtar sınırı kilitsizdir; aynı anda iki istek 11. anahtarı açabilir.
- Biçimi geçerli rastgele ön eklerle hız sınırı bölümleri çoğaltılabilir; genel IP tavanı ve özet doğrulaması bunu sınırlar.
- Ön ekin var olup olmadığı yanıt süresinden çıkarılabilir; ön ek gizli değildir (panelde görünür), gizli kısım 256 bittir.
- `ChangedAtUtc` yalnız EF kaydetme yolunda damgalanır; bu tablolarda `ExecuteUpdate` kullanılmamalıdır.
