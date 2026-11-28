# AI hasta geçmişi özeti ve AI altyapısı API sözleşmesi — AI-002, ADR-008, ADR-044

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-008-embedded-ai-assistant.md` (AI yalnız taslak üretir, anahtar yalnız backend'de, AI durursa klinik akış sürer), `decisions/ADR-044-ai-eklentisi-kredi-ve-fon.md` (madde 3 model, madde 7 kota bitince yalnız AI durur, madde 8 maliyet sınırları ve gerçek token/maliyet kaydı), `decisions/ADR-006-patient-timeline.md` (kaynaklar ve kayda bağlantı), backlog `AI-002`, `AI-007`, `AI-008`, `TIER-014`. İlgili: `PET_TIMELINE_API_CONTRACT.md` (tür adları, tür bazlı izin, klinik kapsamı). Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feature/ai-gecmis-ozeti`. **Migration var** (`AddAiUsage`: `AiUsageRecords`, `ClinicAiSettings`; mevcut tablolara dokunmaz). **Yeni izin:** `Ai.Use` (seed gerekir). Claude API hesabı henüz açılmadı: anahtar yokken özellik **kapalıdır** ve hata fırlatmaz; anahtar backend ayarına konunca açılır. Kredi satışı/ödeme (ADR-044 tarifesi) bu işin dışındadır. **4. dalga (`feature/ai-kota`):** kredi kotası, fon ve uyarılar (AI-007, §4a), platform yönetimi klinik listesi / fon / aylık kırılımlar (AI-008, §3); migration `AddAiCredits`. **TIER-013 Dilim 1 (`feature/ai-klinik-ayari`):** kliniğin kendi AI ayarı, aç/kapa ve bilgilendirme kabulü (§3a); migration `AddClinicAiAcknowledgement`.

---

## 0) Açılış koşulları (tümü gerekir)

| Koşul | Nerede | Yoksa |
|---|---|---|
| Sağlayıcı anahtarı | `Ai:Anthropic:ApiKey` (user-secrets ya da `Ai__Anthropic__ApiKey` ortam değişkeni; appsettings'e yazılmaz) | `Ai.NotConfigured` |
| Genel anahtar ve model fiyatı | `Ai:Enabled = true`, `Ai:Model`'in fiyatı `Ai:Prices`'ta | `Ai.NotConfigured` |
| Klinikte AI açık | `ClinicAiSettings` (kayıt yoksa **kapalı**); klinik yöneticisi bilgilendirmeyi kabul ederek açar (§3a), platform yöneticisi kabulsüz açıp kapatabilir (§3) | `Ai.ClinicNotEnabled` |
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
  "clinicMonthUsedPercent": 12,
  "monthlyCredits": 2000,
  "creditsUsedThisMonth": 240.5,
  "creditsRemaining": 1759.5,
  "fundCredits": 0,
  "quotaResetsAtUtc": "2026-11-30T21:00:00Z"
}
```

- `unavailableCode`: kullanılamıyorsa ilk engelin kodu (`Ai.NotConfigured` → `Ai.ClinicNotEnabled` → `Ai.ClinicQuotaExceeded` → `Ai.SystemBudgetExhausted`; aktif klinik yoksa `Clinics.ClinicSelectionRequired`).
- `clinicMonthUsedPercent` (0–100): kliniğin bu ayki (İstanbul takvim ayı) kullanım oranı = harcanan / (harcanan + kalan kredi); token/maliyet sigortası daha yüksekse o. Kredi ya da sigorta bitince 100, öncesinde en çok 99. ADR-044 madde 7'deki %80 uyarısı için (§4a).
- Kredi alanları (AI-007, §4a): `monthlyCredits` bu ayın kotası (paket hediyesi), `creditsUsedThisMonth` bu ay harcanan (kota + fon), `creditsRemaining` kalan aylık kota + fon bakiyesi, `fundCredits` fon bakiyesi (devreder), `quotaResetsAtUtc` kotanın yenileneceği an (sonraki İstanbul ayının 1'i 00:00). İki ondalıklı; arayüz tam sayıya aşağı yuvarlar. Aktif klinik yoksa hepsi `null`. Dolar yok.

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

Yalnız platform yöneticisi (`platform_admin` claim'i); klinik rolleri 403. Kiracılar arası. Hiçbir yanıtta klinik metin (istem, yanıt, hasta bilgisi) yoktur; yalnız klinik/kiracı adı, sayılar, model ve özellik.

### Klinik satırı (`AiAdminClinicDto`)

```json
{
  "tenantId": "…", "tenantName": "Pati Klinikleri", "clinicId": "…", "clinicName": "Merkez", "isClinicActive": true,
  "planCode": "Premium", "isEnabled": true, "updatedAtUtc": "…",
  "monthlyCredits": 2000, "creditsUsedThisMonth": 240.5, "creditsRemaining": 2009.5, "fundCredits": 250,
  "usedPercent": 10, "quotaResetsAtUtc": "2026-11-30T21:00:00Z"
}
```

`planCode` abonelik yoksa `null` (kota 0). `usedPercent` kliniğin gördüğü oranla aynıdır (§1).

### `GET /api/v1/admin/ai/clinics?search=`

- `search` yok: AI ayarı olan (bir kez açılmış ya da kapatılmış) klinikler; açıklar önce, sonra ada göre.
- `search` (en az 2 karakter, yoksa `400 Ai.Validation`): klinik ya da kiracı adına uyan en çok 50 klinik (açılacak kliniği bulmak için).

### `PUT /api/v1/admin/ai/clinics/{clinicId}`

Gövde `{ "isEnabled": true }`. Kayıt yoksa oluşturur. Denetim kaydı `Ai.ClinicSettings.Update`. Yanıt klinik satırı. Klinik yoksa `404 Ai.ClinicNotFound`.

### `POST /api/v1/admin/ai/clinics/{clinicId}/fund-grants` — fon ekleme (AI-007)

Gövde `{ "credits": 250, "note": "Pilot telafisi" }`: `credits` 1–100.000 tam sayı, `note` en çok 200 karakter (isteğe bağlı). Ücretli satın alma açılana kadar fonun tek yolu budur. Fon aylık kotadan sonra harcanır ve devreder; geri alınmaz (yanlış eklemede destek kaydıyla düzeltilir). Denetim kaydı `Ai.Fund.Grant`. Yanıt güncel klinik satırı. Hatalar: `400 Validation.FluentValidation`, `404 Ai.ClinicNotFound`.

### `GET /api/v1/admin/ai/clinics/{clinicId}/fund-grants`

Fon hareketleri, yeniden eskiye, en çok 100: `[{ "id", "kind": "Manual", "credits", "note", "createdByUserId", "createdAtUtc" }]`.

### `GET /api/v1/admin/ai/usage?month=yyyy-MM` — maliyet takibi (AI-008)

İstanbul takvim ayının kullanımı (`month` yoksa bu ay; biçim bozuksa `400`). Maliyet **tahminidir** (ayardaki fiyatla), fatura değildir.

```json
{
  "isConfigured": true,
  "model": "claude-haiku-5-5",
  "month": "2026-11",
  "isCurrentMonth": true,
  "monthStartUtc": "2026-10-31T21:00:00Z",
  "monthEndUtc": "2026-11-30T21:00:00Z",
  "system": { "requests": 120, "failedRequests": 2, "inputTokens": 280000, "outputTokens": 30000, "tokens": 310000,
              "credits": 91.2, "fundCredits": 0, "costUsd": 0.041 },
  "budget": { "limitUsd": 10, "spentUsd": 0.041, "remainingUsd": 9.959, "usedPercent": 0 },
  "limits": {
    "maxInputTokens": 12000, "maxOutputTokens": 2000, "userRequestsPerMinute": 3,
    "clinicMonthlyTokenLimit": 20000000, "clinicMonthlyCostLimitUsd": 2, "systemMonthlyCostLimitUsd": 10,
    "creditValueUsd": 0.00045, "planMonthlyCredits": { "Basic": 0, "Pro": 500, "Premium": 2000 }
  },
  "clinics": [ { "tenantId": "…", "tenantName": "…", "clinicId": "…", "clinicName": "…", "isEnabled": true, "usage": { /* system ile aynı alanlar */ } } ],
  "features": [ { "feature": "PetHistorySummary", "usage": { … } } ],
  "models": [ { "model": "claude-haiku-5-5", "usage": { … } } ],
  "users": [ { "userId": "…", "userName": "…", "email": "…", "tenantId": "…", "tenantName": "…", "clinicId": "…", "clinicName": "…", "usage": { … } } ]
}
```

- `usage` / `system` alanları: istek sayısı, başarısız istek (`Outcome != Succeeded`), gerçek giriş/çıkış token, harcanan kredi, fondan karşılanan kredi, tahmini $.
- `budget`: sistem aylık sınırına uzaklık (`Ai:SystemMonthlyCostLimitUsd`).
- `clinics`, `features`, `models` maliyete göre azalan. Bu ay için AI'si açık olup henüz kullanmayan klinikler de sıfırlarla gelir; geçmiş aylarda yalnız kullananlar.
- `users` (2026-11-20, kötüye kullanım takibi): ayın en çok harcayan **50** kullanıcı–klinik satırı, maliyete sonra istek sayısına göre azalan. Kullanıcı birden çok klinikte kullandıysa her klinik ayrı satırdır. `userName` görünen ad (yoksa boş), `email` hesabın e-postası. Klinik metni dönmez. Kullanıcı başına dakikada `userRequestsPerMinute` sınırı ayrıca geçerlidir.
- `feature` değerleri: `PetHistorySummary`, `ExamNoteStructure`, `OwnerExplanation`, `DischargeInstructions`.

## 3a) Klinik ayarı — `api/v1/clinics/{clinicId}/ai-settings` (ADR-044 E1, TIER-013 Dilim 1)

Klinik ayarlarındaki "Yapay zekâ" sayfası için. Klinik kiracıda olmalı, atanmış klinik kapsamı uygulanan kullanıcı o kliniğe atanmış olmalı (`ClinicDefinitionAccess`; online randevu ve portal ayarlarıyla aynı kural). Dolar, platform iç notu ve fonu ekleyen kişi dönmez. Migration `AddClinicAiAcknowledgement` (`ClinicAiSettings`: `AcknowledgedVersion`, `AcknowledgedAtUtc`, `AcknowledgedByUserId`; mevcut satırlar boş kalır).

### `GET` — izin `Clinics.Read`

```json
{
  "clinicId": "…",
  "isConfigured": true,
  "isEnabled": false,
  "isAvailable": false,
  "unavailableCode": "Ai.ClinicNotEnabled",
  "updatedAtUtc": null,
  "hasAcknowledged": false,
  "acknowledgedAtUtc": null,
  "acknowledgement": { "version": 1, "items": ["Yapay zekâ yalnız taslak üretir; …", "…"] },
  "monthlyCredits": 2000,
  "creditsUsedThisMonth": 0,
  "creditsRemaining": 2000,
  "fundCredits": 0,
  "usedPercent": 0,
  "quotaResetsAtUtc": "2026-11-30T21:00:00Z",
  "creditGrants": [ { "id": "…", "kind": "Manual", "credits": 250, "createdAtUtc": "…" } ]
}
```

- Durum ve kredi alanları `/ai/status` (§1) ile aynı hesaptır (`AiUsageGuard`, `AiCreditAccount`); `unavailableCode` sırası da aynı. `usedPercent` §1'deki `clinicMonthUsedPercent`.
- `acknowledgement`: açmadan önce gösterilecek bilgilendirme; metnin tek yeri `AiAcknowledgement` (Application). Metin değişince sürüm artar; açık klinik açık kalır, yeniden açmak yeni sürümün kabulünü ister.
- `hasAcknowledged`: güncel sürüm (ya da daha yenisi) bu klinikte kabul edilmiş mi. Platform yöneticisinin açması kabul sayılmaz.
- `creditGrants`: eklenen krediler, yeniden eskiye, en çok 20. `kind` şimdilik yalnız `Manual`.

### `PUT` — izin `Clinics.Update` (Admin, Owner, ClinicAdmin)

Gövde `{ "isEnabled": true, "acknowledgementVersion": 1 }`. `acknowledgementVersion` yalnız açarken ve güncel sürüm bu klinikte kabul edilmemişse gerekir; o zaman güncel sürüme eşit olmalıdır, değilse `400 Ai.AcknowledgementRequired` (kayıt değişmez). Kabul kimin ve ne zaman yaptığıyla saklanır; sonraki açmalarda yeniden sorulmaz. Kapatmak kabul istemez, veri silmez. Kayıt yoksa oluşturulur. Denetim kaydı `Ai.ClinicSettings.UpdateByClinic`. Yanıt `GET` ile aynı. Süresi dolmuş (salt okunur) abonelikte yazma koruması geçerlidir. Hatalar: `403` (izin ya da klinik kapsamı), `404` (klinik kiracıda yok), `400 Ai.AcknowledgementRequired`.

## 4) Sınırlar ve kullanım kaydı (ADR-044 madde 8)

Değerler tek yerde: `appsettings` `Ai` bölümü.

| Ayar | Varsayılan | Kural |
|---|---|---|
| `Ai:Model` | `claude-haiku-5-5` | Hızlı AI (ADR-044 madde 3). Fiyatı `Ai:Prices`'ta olmalı. |
| `Ai:Prices` | Haiku 5.5 $0,10 / $0,50; Sonnet 5.5 $2 / $10 (1M token) | Maliyet tahmini. Haiku 5.5 fiyatı 100K token'a kadar istem içindir (girdi sınırı bunun çok altında). |
| `Ai:MaxInputTokens` | 12.000 | Tahmini girdi (karakter/3); aşan geçmişte en eski kayıtlar çıkar. |
| `Ai:MaxOutputTokens` | 2.000 | Sağlayıcıya `max_tokens` (düşünme dahil). |
| `Ai:UserRequestsPerMinute` | 3 | Kullanıcının son 60 sn'deki sağlayıcı çağrısı sayısı. |
| `Ai:CreditValueUsd` | 0,00045 | Bir kredinin $ değeri (§4a). |
| `Ai:PlanMonthlyCredits` | Basic 0, Pro 500, Premium 2.000 | Paket hediyesi, klinik başına aylık (§4a). |
| `Ai:ClinicMonthlyTokenLimit` | 20.000.000 | **Sigorta.** Klinik, İstanbul takvim ayı, giriş+çıkış. Kredi kotasının üstünde kalmalı (2.000 kredi Haiku'da girdi ağırlığına göre ~5–8M token). |
| `Ai:ClinicMonthlyCostLimitUsd` | 2 | **Sigorta.** Klinik, ay, tahmini $ (2.000 kredi ≈ $0,90). Büyük fon eklenen klinikte artırılmalı. |
| `Ai:SystemMonthlyCostLimitUsd` | 10 | Tüm kiracılar, ay; aşılınca AI herkes için durur. |
| `Ai:Anthropic:Effort` | `low` | `output_config.effort`; özet için yeterli varsayıldı, **ölçülmedi**. |
| `Ai:Anthropic:TimeoutSeconds` | 60 | |

- Sınırlar istek **öncesi** denetlenir; aynı anda giden istekler sınırı en çok o isteklerin maliyeti kadar aşabilir (istek başı üst sınır ≈ $0,0022 Haiku'da).
- **Kullanım kaydı** (`AiUsageRecords`): her sağlayıcı çağrısında (başarısız dahil) klinik, kullanıcı, özellik (`PetHistorySummary`), konu (`SubjectId` = hayvan), model, gerçek giriş/çıkış token (sağlayıcının `usage` alanı; önbellek dahil), tahmini $ maliyet, süre (ms), sonuç (`Succeeded`, `InvalidOutput`, `Truncated`, `Refused`, `ProviderError`). AI-010 ile ayrıca modele gönderilen kaynak kayıtlar (`SourceRefsJson`, yalnız `{type,id}`), istem sürümü (`PromptVersion`, özet için `pet-history-summary/1`) ve taslak üreten özelliklerde `DraftId` tutulur (`AI_DRAFTS_API_CONTRACT.md` §6). İstem ve yanıt metni **tutulmaz**; normal günlüğe yalnız özellik, sonuç, model, istem sürümü, token ve süre yazılır. HTTP istemcisinin varsayılan günlükleri kapalıdır. Hata mesajları `Ai.InvalidOutput` / `Ai.Refused` genelleştirildi ("sonuç"); kodlar aynı.
- Model çağrılmayan istekler (kayıt yok, engel) kullanım kaydı yazmaz ve sınıra sayılmaz.

## 4a) Kredi kotası ve fon (AI-007, ADR-044 madde 2, 5–7)

Kuralın tek yeri `AiClinicQuota` (Application); hesap `AiCreditAccount`.

- **Kredi:** her sağlayıcı çağrısının gerçek token'ından tahmini maliyet → `kredi = maliyet / Ai:CreditValueUsd`, iki ondalık, istek başına yukarı yuvarlanmaz. Standart istek (2.000 giriş + 500 çıkış) Haiku'da 1, Sonnet'te 20 kredi. Başarısız ama token harcayan çağrı da harcar (`AiUsageRecords.Credits`).
- **Kota:** klinik başına aylık, paket hediyesi (`Ai:PlanMonthlyCredits`, kiracının paketinden; abonelik yoksa 0). **Deneme süresinde (`TenantSubscription.Status = Trialing`) kota 0'dır**; hediye ücretli abonelik başlayınca verilir, denemedeki klinik yalnız platformun eklediği fonu kullanabilir (ADR-044 madde 5, 2026-11-20 kullanıcı kararı). Kural `AiCreditAccount.Build` içinde tektir; klinik durumu ve platform yönetimindeki klinik satırı aynı değeri gösterir. Eklenti satışı açılınca eklentinin kredisi buraya eklenecek. İstanbul takvim ayı; devretmez.
- **Fon:** `AiCreditGrants` (yalnız eklenir; şimdilik yalnız platform yöneticisi elle, §3). Bakiye = eklenen − harcanan (tüm zamanlar); devreder. Değişen sayaç tutulmaz (eşzamanlı isteklerde kayıp yazma olmaz).
- **Harcama sırası:** önce kalan aylık kota, artanı fon (`AiUsageRecords.FundCredits`). İstek öncesi onaylandığı için son istek kalanı aşabilir; aşan kısım fondan düşer ve fon eksiye inebilir (sonraki fon eklemesi önce bunu kapatır).
- **Durma:** kalan kredi (kota + fon) 0 ya da token/$ sigortası dolduysa `429 Ai.ClinicQuotaExceeded`; yalnız AI durur.
- **Uyarı:** isteğin harcamasından sonra oran %80'e ulaştıysa `AiQuotaNearlyUsed`, kredi bittiyse `AiQuotaExhausted` kullanıcı bildirimi (tür 12 / 13; `targetType: "AiUsage"`, `targetId` = klinik). Alıcılar: kliniğe erişen ve `Clinics.Update` izni olanlar (klinik yöneticisi; e-belge durdurma uyarısıyla aynı). Her eşik ayda bir kez (`ClinicAiSettings.QuotaWarningMonthStartUtc/Level`); %80 atlanıp doğrudan bitilirse yalnız bitiş uyarısı gider. Fon eklenip oran düşse de aynı ay yeniden %80 uyarısı gitmez.

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
