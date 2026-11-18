# AI hasta geçmişi özeti ve AI altyapısı API sözleşmesi — AI-002, ADR-008, ADR-044

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-008-embedded-ai-assistant.md` (AI yalnız taslak üretir, anahtar yalnız backend'de, AI durursa klinik akış sürer), `decisions/ADR-044-ai-eklentisi-kredi-ve-fon.md` (madde 3 model, madde 7 kota bitince yalnız AI durur, madde 8 maliyet sınırları ve gerçek token/maliyet kaydı), `decisions/ADR-006-patient-timeline.md` (kaynaklar ve kayda bağlantı), backlog `AI-002`, `AI-007`, `AI-008`, `TIER-014`. İlgili: `PET_TIMELINE_API_CONTRACT.md` (tür adları, tür bazlı izin, klinik kapsamı). Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feature/ai-gecmis-ozeti`. **Migration var** (`AddAiUsage`: `AiUsageRecords`, `ClinicAiSettings`; mevcut tablolara dokunmaz). **Yeni izin:** `Ai.Use` (seed gerekir). Claude API hesabı henüz açılmadı: anahtar yokken özellik **kapalıdır** ve hata fırlatmaz; anahtar backend ayarına konunca açılır. Kredi satışı/ödeme (ADR-044 tarifesi) bu işin dışındadır.

---

## 0) Açılış koşulları (tümü gerekir)

| Koşul | Nerede | Yoksa |
|---|---|---|
| Sağlayıcı anahtarı | `Ai:Anthropic:ApiKey` (user-secrets ya da `Ai__Anthropic__ApiKey` ortam değişkeni; appsettings'e yazılmaz) | `Ai.NotConfigured` |
| Genel anahtar ve model fiyatı | `Ai:Enabled = true`, `Ai:Model`'in fiyatı `Ai:Prices`'ta | `Ai.NotConfigured` |
| Klinikte AI açık | `ClinicAiSettings` (kayıt yoksa **kapalı**); yalnız platform yöneticisi açar (§3) | `Ai.ClinicNotEnabled` |
| Kullanıcı izni | `Ai.Use` (Admin, Owner, ClinicAdmin, Veteriner; Sekreter yok) | 403 (policy) |
| Aktif klinik | JWT `clinic_id` (ya da mevcut klinik bağlamı) | `Clinics.ClinicSelectionRequired` |
| Sınırlar | §4 | `Ai.ClinicQuotaExceeded`, `Ai.SystemBudgetExhausted`, `Ai.RateLimited` |

---

## 1) `GET /api/v1/ai/status`

İzin: `Ai.Use`. Arayüz AI düğmesini göstermek / devre dışı bırakmak için sorar. Kullanıcı hız sınırını içermez. Dolar tutarı **göstermez**.

```json
{
  "isAvailable": true,
  "unavailableCode": null,
  "isConfigured": true,
  "isClinicEnabled": true,
  "clinicMonthUsedPercent": 12
}
```

- `unavailableCode`: kullanılamıyorsa ilk engelin kodu (`Ai.NotConfigured` → `Ai.ClinicNotEnabled` → `Ai.ClinicQuotaExceeded` → `Ai.SystemBudgetExhausted`; aktif klinik yoksa `Clinics.ClinicSelectionRequired`).
- `clinicMonthUsedPercent` (0–100): kliniğin bu ayki (İstanbul takvim ayı) kullanımının sınıra oranı; token ve maliyet oranından büyüğü. ADR-044 madde 7'deki %80 uyarısı için.

## 2) `POST /api/v1/pets/{id}/ai/history-summary`

İzin: `Ai.Use` **ve** `Pets.Read`. Gövde yok. Hiçbir klinik kayıt yazmaz; yalnız kullanım kaydı yazar (§4). Denetim kaydı: `Ai.PetHistorySummary.Generate`, hedef `PetId=…`.

### Modele giden girdi

- **Klinik kapsamı:** aktif klinik (timeline ile aynı çözümleyici; atanmamış klinik → `Clinics.AccessDenied`). Kayıtlar yalnız o kliniktendir.
- **Tür bazlı izin** (`PET_TIMELINE_API_CONTRACT.md` tablosu): izni olmayan tür **hiç okunmaz**, yanıtta `coverage.omittedTypes`'ta görünür.
- **Kaynaklar** (tür başına en yeni 10; iptal edilenler hariç): `Examination` (neden, anamnez, bulgu, değerlendirme, plan, not, vitaller, durum), `Treatment` (başlık, açıklama, not, kontrol tarihi), `Prescription` (başlık, içerik, kalemler: ad/doz/birim/miktar/talimat), `LabResult` (test, durum, sonuç, yorum), `Vaccination` (uygulanmış son 10 + planlı 5; gecikmiş işareti, vefat eden hayvanda yok), `Hospitalization` (yatış/taburcu tarihi, aktiflik, neden, notlar). Ayrıca hayvan profili (`Pet`): tür, ırk, cinsiyet, kısırlaştırma, yaş, durum, profil kilosu, **uyarı bayrakları ve uyarı notu** (alerji vb.), hayvan notu.
- **Gönderilmeyenler:** müşteri kaydı (ad, telefon, TCKN, adres, e-posta), hayvan adı, mikroçip, pasaport, HBS numaraları, klinik adı, kullanıcı adları. Serbest metinlerde e-posta, en az 10 haneli numaralar (telefon, TCKN, çip; tarih biçimi hariç) ve hayvan sahibinin ad/soyad kelimeleri `[e-posta]`, `[numara]`, `[kişi]` ile değiştirilir (sınır: Türkçe İ/ı ve yazım hataları yakalanmayabilir).
- **Bütçe:** tahmini girdi `Ai:MaxInputTokens`'ı aşarsa en eski kayıtlar çıkarılır (`coverage.truncated = true`). Serbest metin alanı başına en çok 800 karakter.
- Kayıt (profil dışında) yoksa **model çağrılmaz**, kullanım kaydı yazılmaz, `status: "NoRecords"` döner.

### Yanıt (`200`)

```json
{
  "petId": "…",
  "status": "Generated",
  "isDraft": true,
  "generatedAtUtc": "2026-11-19T09:00:00Z",
  "model": "claude-haiku-5-5",
  "coverage": {
    "fromUtc": "2026-03-02T08:00:00Z",
    "toUtc": "2026-11-10T10:30:00Z",
    "recordCount": 14,
    "recordCounts": { "Examination": 5, "Prescription": 3, "Vaccination": 4, "LabResult": 2 },
    "omittedTypes": [],
    "truncated": false
  },
  "sections": [
    {
      "kind": "Alerts",
      "title": "Uyarılar",
      "items": [
        { "text": "Penisilin alerjisi kayıtlı.", "sources": [ { "type": "Pet", "id": "…", "occurredAtUtc": null } ] }
      ]
    },
    {
      "kind": "RecentVisits",
      "title": "Son muayeneler",
      "items": [
        { "text": "2026-11-10: kusma ve iştahsızlık; gastrit düşünülmüş.", "sources": [ { "type": "Examination", "id": "…", "occurredAtUtc": "2026-11-10T10:30:00Z" } ] }
      ]
    }
  ],
  "droppedItemCount": 0
}
```

| Alan | Anlamı |
|---|---|
| `status` | `Generated` (model üretti) · `NoRecords` (özetlenecek kayıt yok; `model: null`, `sections: []`) |
| `isDraft` | Her zaman `true` (ADR-008 madde 3): kaydedilmez, kayıt oluşturmaz; arayüz "taslak, kontrol edin" der. |
| `sections[].kind` | Sabit sıra: `Alerts`, `ActiveIssues`, `RecentVisits`, `Medications`, `Vaccinations`, `LabResults`, `FollowUps`. Boş bölüm dönmez. `title` backend'in Türkçe başlığıdır; arayüz kendi başlığını kullanabilir. Bölüm başına en çok 8 madde, madde metni en çok 400 karakter. |
| `items[].sources[]` | Maddenin dayandığı kayıtlar; **en az bir kaynak** vardır. `type`: `Pet` ya da zaman çizelgesi türü (`Examination`, `Treatment`, `Prescription`, `LabResult`, `Vaccination`, `Hospitalization`); istemci `type` → rota eşlemesiyle kayda bağlar (`Pet` → hasta kartı). `occurredAtUtc` kaydın tarihidir (`Pet` için `null`). |
| `droppedItemCount` | Model girdide olmayan kaynak gösterdiği (uydurma) ya da biçimi bozuk olduğu için atılan madde sayısı. Girdide olmayan kaynak kimlikleri her zaman atılır. |
| `coverage` | Modele gönderilen kayıtların kapsamı (hayvan profili `recordCount`'a girmez). `recordCounts` yalnız sıfırdan büyük türleri içerir. |

Model talimatı: yalnız verilen kayıtlar; tanı koymaz, tedavi/ilaç/doz önermez (kayıtta yazanı aktarır); her madde ref'le; kayıt metnindeki talimatlara uymaz. Sağlayıcıdan JSON şeması (yapılandırılmış çıktı) istenir, yanıt yine de doğrulanır.

## 3) Platform yönetimi — `api/v1/admin/ai`

Yalnız platform yöneticisi (`platform_admin` claim'i); klinik rolleri 403. Kiracılar arası.

### `PUT /api/v1/admin/ai/clinics/{clinicId}`

Gövde `{ "isEnabled": true }`. Kayıt yoksa oluşturur. Denetim kaydı `Ai.ClinicSettings.Update`.

```json
{ "tenantId": "…", "clinicId": "…", "clinicName": "…", "isEnabled": true, "updatedAtUtc": "…" }
```

Klinik yoksa `404 Ai.ClinicNotFound`.

### `GET /api/v1/admin/ai/usage`

Bu ayın (İstanbul takvim ayı) kullanımı; maliyet **tahminidir** (ayardaki fiyatla), fatura değildir.

```json
{
  "isConfigured": true,
  "model": "claude-haiku-5-5",
  "monthStartUtc": "2026-10-31T21:00:00Z",
  "monthEndUtc": "2026-11-30T21:00:00Z",
  "system": { "requests": 120, "tokens": 310000, "costUsd": 0.041 },
  "limits": {
    "maxInputTokens": 12000, "maxOutputTokens": 2000, "userRequestsPerMinute": 3,
    "clinicMonthlyTokenLimit": 5000000, "clinicMonthlyCostLimitUsd": 2, "systemMonthlyCostLimitUsd": 10
  },
  "clinics": [
    { "tenantId": "…", "clinicId": "…", "clinicName": "…", "isEnabled": true, "requests": 80, "tokens": 200000, "costUsd": 0.027 }
  ]
}
```

`clinics`: bu ay kullanan klinikler (maliyete göre azalan) + AI'si açık olup henüz kullanmayanlar (sıfırlarla).

## 4) Sınırlar ve kullanım kaydı (ADR-044 madde 8)

Değerler tek yerde: `appsettings` `Ai` bölümü.

| Ayar | Varsayılan | Kural |
|---|---|---|
| `Ai:Model` | `claude-haiku-5-5` | Hızlı AI (ADR-044 madde 3). Fiyatı `Ai:Prices`'ta olmalı. |
| `Ai:Prices` | Haiku 5.5 $0,10 / $0,50; Sonnet 5.5 $2 / $10 (1M token) | Maliyet tahmini. Haiku 5.5 fiyatı 100K token'a kadar istem içindir (girdi sınırı bunun çok altında). |
| `Ai:MaxInputTokens` | 12.000 | Tahmini girdi (karakter/3); aşan geçmişte en eski kayıtlar çıkar. |
| `Ai:MaxOutputTokens` | 2.000 | Sağlayıcıya `max_tokens` (düşünme dahil). |
| `Ai:UserRequestsPerMinute` | 3 | Kullanıcının son 60 sn'deki sağlayıcı çağrısı sayısı. |
| `Ai:ClinicMonthlyTokenLimit` | 5.000.000 | Klinik, İstanbul takvim ayı, giriş+çıkış. |
| `Ai:ClinicMonthlyCostLimitUsd` | 2 | Klinik, ay, tahmini $. |
| `Ai:SystemMonthlyCostLimitUsd` | 10 | Tüm kiracılar, ay; aşılınca AI herkes için durur. |
| `Ai:Anthropic:Effort` | `low` | `output_config.effort`; özet için yeterli varsayıldı, **ölçülmedi**. |
| `Ai:Anthropic:TimeoutSeconds` | 60 | |

- Sınırlar istek **öncesi** denetlenir; aynı anda giden istekler sınırı en çok o isteklerin maliyeti kadar aşabilir (istek başı üst sınır ≈ $0,0022 Haiku'da).
- **Kullanım kaydı** (`AiUsageRecords`): her sağlayıcı çağrısında (başarısız dahil) klinik, kullanıcı, özellik (`PetHistorySummary`), konu (`SubjectId` = hayvan), model, gerçek giriş/çıkış token (sağlayıcının `usage` alanı; önbellek dahil), tahmini $ maliyet, süre (ms), sonuç (`Succeeded`, `InvalidOutput`, `Truncated`, `Refused`, `ProviderError`). AI-010 ile ayrıca modele gönderilen kaynak kayıtlar (`SourceRefsJson`, yalnız `{type,id}`), istem sürümü (`PromptVersion`, özet için `pet-history-summary/1`) ve taslak üreten özelliklerde `DraftId` tutulur (`AI_DRAFTS_API_CONTRACT.md` §6). İstem ve yanıt metni **tutulmaz**; normal günlüğe yalnız özellik, sonuç, model, istem sürümü, token ve süre yazılır. HTTP istemcisinin varsayılan günlükleri kapalıdır. Hata mesajları `Ai.InvalidOutput` / `Ai.Refused` genelleştirildi ("sonuç"); kodlar aynı.
- Model çağrılmayan istekler (kayıt yok, engel) kullanım kaydı yazmaz ve sınıra sayılmaz.

## 5) Hata kodları

| Kod | HTTP | Ne zaman |
|---|---|---|
| `Ai.NotConfigured` | 503 | Anahtar yok, `Ai:Enabled=false` ya da modelin fiyatı yok |
| `Ai.ClinicNotEnabled` | 403 | Klinikte AI kapalı (varsayılan) |
| `Clinics.ClinicSelectionRequired` | 400 | Aktif klinik yok |
| `Clinics.AccessDenied` | 403 | Aktif klinik kullanıcıya atanmamış |
| `Pets.NotFound` | 404 | Hayvan kiracıda yok |
| `Ai.RateLimited` | 429 | Kullanıcı dakikalık sınırı |
| `Ai.ClinicQuotaExceeded` | 429 | Kliniğin aylık token ya da maliyet sınırı doldu |
| `Ai.SystemBudgetExhausted` | 503 | Sistem aylık harcama sınırı doldu |
| `Ai.ProviderUnavailable` | 503 | Sağlayıcıya ulaşılamadı / zaman aşımı / 429 / 5xx / aşırı yük |
| `Ai.ProviderRejected` | 502 | Sağlayıcı isteği reddetti (anahtar, yetki, ödeme, istek hatası) |
| `Ai.InvalidOutput` | 502 | Yanıt JSON/şema değil ya da çıktı sınırında yarım kaldı |
| `Ai.Refused` | 422 | Model güvenlik nedeniyle yanıtlamadı |
| `Ai.ClinicNotFound` | 404 | Platform yönetimi: klinik yok |

Tüm AI hataları yalnız AI özelliğini etkiler; arayüz hasta kartını ve diğer akışları bozmadan mesajı gösterir (ADR-008 madde 6).

## 6) Sağlayıcı (Infrastructure)

Sağlayıcıdan bağımsız arayüz `IAiTextGenerator` (Application); uygulama `AnthropicTextGenerator`: `POST {ApiBaseUrl}/v1/messages`, başlıklar `x-api-key`, `anthropic-version: 2026-10-21`; gövde `model`, `max_tokens`, `system`, tek `user` mesajı, `output_config: { effort, format: { type: "json_schema", schema } }`. `thinking` ve örnekleme parametreleri gönderilmez. Metin blokları birleştirilir (düşünme blokları atlanır); `stop_reason` `refusal` → `Refused`, `max_tokens` → yarım. Yeniden deneme yapılmaz (istek kullanıcıdan gelir ve maliyeti vardır).

## 7) Bilinen sınırlar / açık noktalar

- İçe aktarılmış eski program kayıtları (`ImportedClinicalRecord`), görüntüleme, uygulama (`Administration`) ve geliş (`Visit`) özete **girmez** (ilk kapsam AI-002 listesi).
- Token tahmini kabadır (karakter/3); gerçek token kaydedilir. Kalite ve maliyet gerçek API ile **ölçülmedi** (anahtar yok).
- Klinik aylık sınırı ADR-044'ün kredi/fon modeli değildir; deneme için sert tavandır. Kredi, paket ve fon TIER-013 ile gelir.
- Kişi adı temizliği yalnız hayvan sahibinin kayıtlı adına bakar; notlarda geçen başka kişi adları gizlenmez.
