# AI taslak onay akışı ve AI denetim izi API sözleşmesi — AI-009, AI-010, ADR-008

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-008-embedded-ai-assistant.md` (madde 3: AI çıktısı taslaktır, kullanıcı onayı olmadan kayıt olmaz; madde 10: hassas klinik veri normal günlüğe yazılmaz), `decisions/ADR-044-ai-eklentisi-kredi-ve-fon.md`, backlog `AI-009`, `AI-010`. Altyapı ve açılış koşulları (anahtar, klinikte açma, `Ai.Use`, sınırlar): `AI_HISTORY_SUMMARY_API_CONTRACT.md` §0, §4. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feature/ai-onay-akisi` (`feature/ai-gecmis-ozeti` üstüne). **Migration var** (`AddAiDrafts`: yeni `AiDrafts` tablosu; `AiUsageRecords`'a `SourceRefsJson`, `DraftId`, `PromptVersion` sütunları). Yeni izin yok. Bu dalda taslak üreten **özellik yok**: AI-001/003/004 (3. dalga) bu akışı kullanacak; uçlar şimdilik yalnız test uygulayıcısıyla denendi.

---

## 1) Yaşam döngüsü

```
üretim (özellik ucu, 3. dalga) ──► Draft ──kabul──► Accepted | AcceptedWithEdits   (hedef kayda yazıldı)
                                     ├──at────► Discarded                          (hiçbir kayda yazılmadı)
                                     └──süre──► Expired                            (hiçbir kayda yazılmadı)
```

- **Kabul edilmemiş taslak hiçbir klinik kayda yazılmaz.** Üretim yalnız `AiDrafts` ve `AiUsageRecords` satırı yazar.
- Kabul, hedef kayda özelliğin **uygulayıcısıyla** (§4), mevcut komutlar üzerinden ve **tek transaction'da** yazar: uygulayıcı başarısızsa hedef kayıt da taslak da değişmez, taslak `Draft` kalır.
- **İçerik saklama:** `content` yalnız `Draft` durumunda tutulur. Kabul, atma ve süre dolumunda veritabanından **silinir** (kabulde içerik artık hedef kayıttadır). Karar alanları, kaynak kimlikleri ve hedef kalıcıdır (denetim izi, §6).
- **Süre:** `Ai:DraftLifetimeDays` (varsayılan **30 gün**, 1–365). Süresi geçen taslak okumada hemen `Expired` görünür ve karar verilemez; arka plan işi (`Ai:DraftExpiry`, saatlik) durumu kalıcı `Expired` yapar ve içeriği siler.
- **Eşzamanlılık:** kabul ve atma taslak başına kilitle sıralanır (iki sekme / çift tık iki kez uygulamaz); ikinci istek `409 AiDrafts.NotPending` alır.

## 2) Erişim (tüm uçlar)

| Koşul | Yoksa |
|---|---|
| `Ai.Use` | 403 (policy) |
| Aktif klinik (JWT `clinic_id`) ve kullanıcının o kliniğe erişimi | `400 Clinics.ClinicSelectionRequired` / klinik erişim hatası |
| Taslak aynı kiracıda **ve aktif klinikte** | `404 AiDrafts.NotFound` (başka klinikteki taslak var olarak görünmez) |
| Özelliğin uygulayıcısının izni (`requiredPermission`, ör. muayene notu için `Examinations.Update`) | `403 AiDrafts.Forbidden` |
| Özelliğin uygulayıcısı kayıtlı | `422 AiDrafts.FeatureNotSupported` |

Taslağı üreten kişi dışındaki, aynı klinikte aynı izinlere sahip kullanıcılar da taslağı görebilir ve karar verebilir (hekim devri). Klinik kapalıyken (`Ai.ClinicNotEnabled`) de mevcut taslaklar okunabilir ve karara bağlanabilir; yalnız üretim durur.

## 3) Uçlar — `api/v1/ai/drafts`

### `GET /api/v1/ai/drafts/{id}` → `200 AiDraftDto`

```json
{
  "id": "…",
  "feature": "ExamNoteStructure",
  "status": "Draft",
  "targetType": "Examination",
  "targetId": "…",
  "sources": [{ "type": "Examination", "id": "…" }, { "type": "Pet", "id": "…" }],
  "content": { "…": "özelliğe özgü yapılandırılmış içerik" },
  "model": "claude-haiku-5-5",
  "createdByUserId": "…",
  "createdAtUtc": "2026-11-19T09:00:00Z",
  "expiresAtUtc": "2026-12-19T09:00:00Z",
  "decidedByUserId": null,
  "decidedAtUtc": null,
  "appliedRecordId": null
}
```

- `feature`: `ExamNoteStructure` (AI-001), `OwnerExplanation` (AI-003), `DischargeInstructions` (AI-004). `PetHistorySummary` taslak üretmez.
- `status`: `Draft`, `Accepted`, `AcceptedWithEdits`, `Discarded`, `Expired` (okuma anındaki durum).
- `content`: yalnız `status: "Draft"` iken dolu; diğer durumlarda `null`. Şeması özelliğe özgüdür (3. dalga sözleşmeleri).
- `sources[].type`: zaman çizelgesi türleri ve `Pet`; istemci mevcut rota eşlemesiyle kayda bağlanır.
- `appliedRecordId`: kabulde yazılan kayıt (çoğu zaman `targetId`).

### `GET /api/v1/ai/drafts?targetType=Examination&targetId={id}` → `200 AiDraftDto[]`

Bir kaydın **karar bekleyen** (süresi geçmemiş) taslakları, yeniden eskiye, en çok 20. Ekran yeniden açılınca bekleyen taslağa dönmek içindir. Kullanıcının izni olmayan özelliklerin taslakları listede yer almaz. Boş liste `[]`. `targetType` zorunlu, en çok 40 karakter.

### `POST /api/v1/ai/drafts/{id}/accept` → `200 AiDraftDto`

Gövde (isteğe bağlı): `{ "editedContent": { … } }`

- `editedContent` yoksa (ya da üretilen içerikle **aynıysa**) üretilen içerik yazılır → `Accepted`.
- Farklıysa düzenlenmiş hali yazılır → `AcceptedWithEdits`. Şema doğrulaması uygulayıcıdadır (`400 AiDrafts.Validation`). En çok 50.000 karakter (JSON).
- Yanıt: güncel taslak (`content: null`, `decidedByUserId`, `decidedAtUtc`, `appliedRecordId`).
- Denetim kaydı: `Ai.Draft.Accept`, hedef `AiDraftId=…`; `editedContent` denetim yüküne **yazılmaz**. Uygulayıcının çağırdığı mevcut komutlar kendi denetim kayıtlarını yazar (ör. `Examination.Update`).
- Hedef komutun hataları (ör. `Examinations.Finalized`, kayıt sürümü çakışması) aynen döner; hiçbir şey yazılmaz, taslak `Draft` kalır.

### `POST /api/v1/ai/drafts/{id}/discard` → `200 AiDraftDto`

Gövde yok. Hiçbir kayda yazılmaz; `status: "Discarded"`, içerik silinir. Denetim kaydı: `Ai.Draft.Discard`, hedef `AiDraftId=…`.

## 4) Genişleme noktası — `IAiDraftApplier` (3. dalga için)

Taslak üreten her özellik iki parça ekler:

1. **Uygulayıcı** (`Application/Ai/Drafts/IAiDraftApplier.cs`), `services.AddScoped<IAiDraftApplier, …>()`:
   - `Feature` — `AiFeature` değeri.
   - `RequiredPermission` — hedef kaydın yazma izni (görme, kabul, atma için `Ai.Use`'a ek).
   - `ApplyAsync(AiDraftApplication, ct)` → `Result<Guid?>`: içeriği doğrular (`AiErrors.InvalidDraftContent`), hedef kayda **mevcut komutla** (`ISender.Send`) yazar, yazılan kaydın kimliğini döner. Taslağı kaydetmez. `AiDraftApplication.DraftId` hedef kayda "AI taslağından" izi olarak yazılabilir; `WasEdited` düzenlenip düzenlenmediğini söyler. Hata mesajları klinik metin içermez.
2. **Üretim ucu** (özelliğin kendi komutu): `AiUsageGuard.CheckAsync` → girdiyi kur (kimlik temizliği, izin süzmesi) → `AiInvoker.InvokeForDraftAsync(new AiInvocation(…, PromptVersion, …), new AiDraftTarget(type, id), parse, ct)` → `AiDraftDto.From(draft, now)`. `parse` model yanıtını doğrulayıp `JsonObject`'e çevirir; `null` ise `502 Ai.InvalidOutput`. Uygulayıcı kayıtlı değilse model **çağrılmaz** (`422 AiDrafts.FeatureNotSupported`).

Örnek (testteki uygulayıcı, `AiDraftIntegrationTests.TestExaminationNoteApplier`): `{"note": "…"}` içeriğini muayeneyi okuyup `UpdateExaminationCommand` ile `Notes` alanına yazar.

## 5) Hata kodları

| Kod | HTTP | Ne zaman |
|---|---|---|
| `AiDrafts.NotFound` | 404 | Taslak yok, başka kiracıda ya da başka klinikte |
| `AiDrafts.Forbidden` | 403 | Özelliğin izni yok |
| `AiDrafts.NotPending` | 409 | Taslağa zaten karar verilmiş |
| `AiDrafts.Expired` | 410 | Saklama süresi doldu; yeniden üretilmeli |
| `AiDrafts.FeatureNotSupported` | 422 | Özelliğin uygulayıcısı yok |
| `AiDrafts.Validation` | 400 | Düzenlenmiş içerik şemaya uymuyor / çok uzun |
| Hedef komutun kodları | komutun eşlemesi | Ör. `Examinations.Finalized` 409 |
| `Ai.InvalidOutput`, `Ai.Refused`, `Ai.Provider*`, sınır kodları | `AI_HISTORY_SUMMARY_API_CONTRACT.md` §5 | Üretim ucunda |

## 6) AI denetim izi (AI-010)

Klinik metin ve kişi bilgisi denetim kayıtlarına ve normal günlüklere **yazılmaz**. İz üç yerde, yalnız kimlik ve ölçüyle:

| Yer | İçerik |
|---|---|
| `AiUsageRecords` (her sağlayıcı çağrısı, başarısız dahil) | kim (`UserId`), ne zaman, klinik, özellik, konu (`SubjectId`), **kaynak kayıtlar** (`SourceRefsJson`: yalnız `{type,id}`, en çok 300), **model**, **istem sürümü** (`PromptVersion`, ör. `pet-history-summary/1`), sonuç, token, tahmini maliyet, süre, üretilen taslak (`DraftId`) |
| `AiDrafts` (karar alanları kalıcı) | özellik, hedef, kaynaklar, model, istem sürümü, **karar** (`Status`), **düzenlenip düzenlenmediği** (`AcceptedWithEdits`), kim / ne zaman karar verdi, yazılan kayıt (`AppliedRecordId`); içerik karar sonrası silinir |
| `AuditLogs` (mevcut denetim deseni) | `Ai.PetHistorySummary.Generate`, `Ai.Draft.Accept`, `Ai.Draft.Discard` (+ uygulayıcının çağırdığı komutların kendi kayıtları); düzenlenmiş içerik yükte yok (`JsonIgnore`) |

Normal günlüğe yalnız `AI {Feature} {Outcome}: model, prompt (sürüm), input/output token, durationMs` yazılır.

Bu izi okuyan bir uç bu dalda **yok** (veritabanında); platform yönetimi ya da klinik yöneticisi görünümü ayrı iş.

## 7) Bilinen sınırlar / açık noktalar

- Taslak üreten özellik bu dalda yok; uçların arayüzü ve özelliğe özgü içerik şemaları 3. dalgada.
- Klinik kapalıyken (`Ai.ClinicNotEnabled`) taslaklar okunur ve karara bağlanır (varsayılan; kapatma yalnız üretimi durdurur).
- Kabul edilen içeriğin AI taslağından geldiği bilgisi hedef kayda otomatik yazılmaz; isteyen uygulayıcı `DraftId`'yi kendi kaydına yazar (ör. muayene ek alanı ya da kayıt geçmişi).
- Süresi dolan taslak içeriği en geç bir tarama aralığı (varsayılan 60 dk) sonra veritabanından silinir; okuma anında hemen gizlenir.
