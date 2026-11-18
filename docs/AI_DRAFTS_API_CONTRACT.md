# AI taslak onay akışı, AI denetim izi ve taslak üreten klinik özellikler — AI-009, AI-010, AI-001, AI-003, AI-004, ADR-008

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-008-embedded-ai-assistant.md` (madde 3: AI çıktısı taslaktır, kullanıcı onayı olmadan kayıt olmaz; madde 10: hassas klinik veri normal günlüğe yazılmaz), `decisions/ADR-044-ai-eklentisi-kredi-ve-fon.md`, backlog `AI-009`, `AI-010`, `AI-001`, `AI-003`, `AI-004`, arayüz tasarımı `ux/ai-arayuz-tasarim.md` (A · yan panel). Altyapı ve açılış koşulları (anahtar, klinikte açma, `Ai.Use`, sınırlar): `AI_HISTORY_SUMMARY_API_CONTRACT.md` §0, §4. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test).
> - Akış ve denetim (§1–§6): dal `feature/ai-onay-akisi`. Migration `AddAiDrafts`.
> - Klinik özellikler (§7–§8): dal `feature/ai-taslaklar` (`feature/ai-onay-akisi` üstüne). **Migration var** (`AddOwnerExplanations`: yeni `OwnerExplanations` tablosu; `AiDrafts`'a `(TenantId, AppliedRecordId)` dizini). Yeni izin yok. Kayıt DTO'larına `aiProvenance` eklendi (muayene detayı, bakım talimatı).

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
- `content`: yalnız `status: "Draft"` iken dolu; diğer durumlarda `null`. Şeması özelliğe özgüdür (§7).
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

## 4) Genişleme noktası — `IAiDraftApplier`

Taslak üreten her özellik iki parça ekler (örnekler: §7'deki üç özellik, `Application/Ai/ClinicalDrafts/`):

1. **Uygulayıcı** (`Application/Ai/Drafts/IAiDraftApplier.cs`), `services.AddScoped<IAiDraftApplier, …>()`:
   - `Feature` — `AiFeature` değeri.
   - `RequiredPermission` — hedef kaydın yazma izni (görme, kabul, atma için `Ai.Use`'a ek).
   - `ApplyAsync(AiDraftApplication, ct)` → `Result<Guid?>`: içeriği doğrular (`AiErrors.InvalidDraftContent`), hedef kayda **mevcut komutla** (`ISender.Send`) yazar, yazılan kaydın kimliğini döner. Taslağı kaydetmez. Hata mesajları klinik metin içermez.
   - `IsEdited(generated, accepted)` (isteğe bağlı; varsayılan JSON derin karşılaştırma) — içerik metin dışı kabul seçeneği taşıyorsa (AI-001 `replaceFields`) yalnız metni karşılaştırır.
2. **Üretim komutu**: `AiDraftGenerator.GenerateAsync(feature, prepare, ct)` sırayı tek yerde tutar: istek bağlamı → uygulayıcı ve izin (yoksa model çağrılmaz) → `prepare` (hedef kaydı yükle, girdiyi kur) → `AiUsageGuard` → `AiInvoker.InvokeForDraftAsync` → `AiDraftDto`. İstem `AiDraftPrompt(Version, System, Schema)` olarak verilir.

## 5) Hata kodları

| Kod | HTTP | Ne zaman |
|---|---|---|
| `AiDrafts.NotFound` | 404 | Taslak yok, başka kiracıda ya da başka klinikte |
| `AiDrafts.Forbidden` | 403 | Özelliğin izni yok |
| `AiDrafts.NotPending` | 409 | Taslağa zaten karar verilmiş |
| `AiDrafts.Expired` | 410 | Saklama süresi doldu; yeniden üretilmeli |
| `AiDrafts.FeatureNotSupported` | 422 | Özelliğin uygulayıcısı yok |
| `AiDrafts.Validation` | 400 | Düzenlenmiş içerik şemaya uymuyor / çok uzun |
| Hedef komutun kodları | komutun eşlemesi | Ör. `Examinations.Finalized` 409, `Examinations.Validation` 400 (birleşen metin uzunluğu), `Examinations.ConcurrencyConflict` 409 |
| `Ai.InvalidOutput`, `Ai.Refused`, `Ai.Provider*`, sınır kodları | `AI_HISTORY_SUMMARY_API_CONTRACT.md` §5 | Üretim ucunda |

## 6) AI denetim izi (AI-010)

Klinik metin ve kişi bilgisi denetim kayıtlarına ve normal günlüklere **yazılmaz**. İz üç yerde, yalnız kimlik ve ölçüyle:

| Yer | İçerik |
|---|---|
| `AiUsageRecords` (her sağlayıcı çağrısı, başarısız dahil) | kim (`UserId`), ne zaman, klinik, özellik, konu (`SubjectId`), **kaynak kayıtlar** (`SourceRefsJson`: yalnız `{type,id}`, en çok 300), **model**, **istem sürümü** (`PromptVersion`, ör. `pet-history-summary/1`), sonuç, token, tahmini maliyet, süre, üretilen taslak (`DraftId`) |
| `AiDrafts` (karar alanları kalıcı) | özellik, hedef, kaynaklar, model, istem sürümü, **karar** (`Status`), **düzenlenip düzenlenmediği** (`AcceptedWithEdits`), kim / ne zaman karar verdi, yazılan kayıt (`AppliedRecordId`); içerik karar sonrası silinir |
| `AuditLogs` (mevcut denetim deseni) | `Ai.PetHistorySummary.Generate`, `Ai.ExamNoteStructure.Generate`, `Ai.OwnerExplanation.Generate`, `Ai.DischargeInstructions.Generate`, `Ai.Draft.Accept`, `Ai.Draft.Discard` (+ uygulayıcının çağırdığı komutların kendi kayıtları: `Examination.Update`, `OwnerExplanation.Create`, `CareInstruction.Create`); serbest not ve düzenlenmiş içerik yükte yok (`JsonIgnore`) |

Normal günlüğe yalnız `AI {Feature} {Outcome}: model, prompt (sürüm), input/output token, durationMs` yazılır. Kayıtta görünen iz için §8.

## 7) Taslak üreten klinik özellikler (AI-001, AI-003, AI-004)

Ortak kurallar:

- **Gövde:** `{ "text": "…" }` (`AiNoteDraftBody`). AI-001'de zorunlu serbest not; AI-003/004'te isteğe bağlı "hekimin vurgulamak istedikleri" (gövde hiç gönderilmeyebilir). Not **saklanmaz**; yalnız modele gider ve denetim yüküne yazılmaz.
- **Yanıt:** `200 AiDraftDto` (§3) — `status: "Draft"`, `content` özelliğe özgü (aşağıda). Taslak kayda yazılmaz; karar §3 uçlarıyla (`accept` / `discard`). Ekran yeniden açılınca `GET /api/v1/ai/drafts?targetType=…&targetId=…` bekleyen taslağı döndürür.
- **Kimlik bilgisi modele gitmez:** hayvan adı `[hasta]`, sahip adı `[kişi]`, telefon/TCKN/çip `[numara]`, e-posta `[e-posta]` olur; müşteri kaydının yalnız adı okunur. Model metinde `[hasta]` yazar, backend taslağa yazmadan önce **hayvanın adıyla değiştirir** (istemci yer tutucu görmez; ad yoksa "hastanız").
- **Girdi:** aktif klinikteki kayıt (başka klinikteki kayıt `404`). AI-003/004'te muayenenin alanları, muayeneye bağlı reçeteler (kalemleriyle) ve tedaviler — yalnız kullanıcının okuma izni olan türler, iptaller hariç, tür başına en çok 10 — ve hasta profili (tür, ırk, cinsiyet, kısırlaştırma, yaş, kilo, uyarı bayrakları; ad/çip/not yok).
- **Hatalar:** üretim ucunda `AI_HISTORY_SUMMARY_API_CONTRACT.md` §5 (sınır, sağlayıcı, `Ai.InvalidOutput`, `Ai.Refused`), `Clinics.ClinicSelectionRequired` 400, `AiDrafts.Forbidden` 403, kaydın `…NotFound` 404'ü, `400` doğrulama (not uzunluğu). Yanıt geçersizse taslak oluşmaz ama kullanım kaydı yazılır.
- **İstemler** sürümlü ve tek yerde: `Application/Ai/ClinicalDrafts/ClinicalAiPrompts.cs` (`exam-note-structure/1`, `owner-explanation/1`, `discharge-instructions/1`). Model kayıtta olmayan tanı, ilaç, doz eklemez; kayıt metinleri veridir.

### 7.1 AI-001 — Muayene notunu alanlara ayırma (`ExamNoteStructure`)

`POST /api/v1/examinations/{id}/ai/note-draft` — izin `Ai.Use` + `Examinations.Update`. Gövde `{ "text": "serbest not" }` zorunlu, en çok **8000** karakter.

- Kesinleşmiş muayene → `409 Examinations.Finalized` (model çağrılmaz). Yeni muayenede önce muayene kaydedilmeli (taslağın hedefi muayene kimliğidir).
- Modele yalnız not ve hasta profili gider; muayenenin mevcut alanları **gönderilmez** (model yalnız notu böler).
- `targetType: "Examination"`, `targetId`: muayene; `sources`: muayene + hasta.

`content`:

```json
{
  "fields": {
    "visitReason": "Karamel kusma",
    "anamnesis": "3 gündür kusuyor",
    "findings": "Karın palpasyonunda hassasiyet",
    "assessment": "…", "plan": "…", "notes": "…",
    "weightKg": 4.2, "temperatureC": 38.9, "heartRateBpm": 120, "respiratoryRatePerMin": 24
  },
  "replaceFields": []
}
```

- `fields`'ta yalnız notta karşılığı olan alanlar bulunur; olmayan alan **hiç gelmez** (arayüz "Notta karşılığı yok · boş kalır" gösterir). Metin alanları: `visitReason`, `anamnesis`, `findings`, `assessment`, `plan`, `notes`. Sayılar: `weightKg`, `temperatureC` (ondalık), `heartRateBpm`, `respiratoryRatePerMin` (tam sayı).
- **Kabul** (`POST /api/v1/ai/drafts/{id}/accept`, `editedContent` aynı şemada):
  - Kullanıcı bir alanı aktarmak istemiyorsa `fields`'tan çıkarır (ya da `null`/boş metin gönderir).
  - **Varsayılan "Sona ekle":** metin alanı muayenedeki metnin sonuna **satır satır** eklenir; muayenede zaten bulunan satır tekrar eklenmez (şablon birleştirme kuralı, `ExaminationTextMerge.MergeLines`).
  - **"Yerine yaz":** alan adı `replaceFields`'a yazılır (ör. `["visitReason"]`); muayenedeki metin taslaktakiyle değişir.
  - Vital değer verildiyse mevcut değerin yerine geçer.
  - Yazım mevcut `UpdateExaminationCommand` ile, muayenenin **güncel** sürümüyle yapılır: kesinleşme (`409 Examinations.Finalized`), uzunluk sınırları (birleşen metin dahil; `400 Examinations.Validation`) ve vital aralıkları orada denetlenir. Kabul, formda kaydedilmemiş değişikliklerin üstüne yazmaz ama formdaki sürümü eskitir: istemci kabulden önce formu kaydetmeli, kabulden sonra muayeneyi yeniden okumalıdır.
  - `appliedRecordId` = muayene. **Düzenlendi mi** yalnız `fields` karşılaştırılarak bulunur; yalnız `replaceFields` değiştiyse `Accepted`.
- Şema dışı içerik → `400 AiDrafts.Validation` (bilinmeyen alan, yanlış tür, `replaceFields`'ta sayı alanı, aktarılacak alan yok).

### 7.2 AI-003 — Hasta sahibine sade açıklama (`OwnerExplanation`)

`POST /api/v1/examinations/{id}/ai/owner-explanation-draft` — izin `Ai.Use` + `Examinations.Read` + `Examinations.Update`. Gövde isteğe bağlı `{ "text": "vurgulanacaklar" }`, en çok 2000.

- Kesinleşmiş muayenede de çalışır (açıklama muayene kaydını değiştirmez).
- `content`: `{ "text": "Karamel için muayenede …" }` — düz metin, paragraflar arası boş satır, en çok **4000** karakter (model ~1500 hedefler).
- **Kabul:** muayeneye bağlı **ayrı** bir "hasta sahibine açıklama" kaydı oluşur (`OwnerExplanations` tablosu; `CreateOwnerExplanationCommand`); `appliedRecordId` = açıklama kimliği. Muayene alanları değişmez. Kayıt oluşturulduktan sonra değişmez (düzeltme için yeniden üretilir; bugün elle oluşturma ve silme ucu yok).
- **Okuma:** `GET /api/v1/examinations/{id}/owner-explanations` (izin `Examinations.Read`) → yeniden eskiye:

```json
[{
  "id": "…", "examinationId": "…", "petId": "…",
  "text": "…",
  "sourceAiDraftId": "…",
  "createdByUserId": "…", "createdByName": "Dr. Ayşe Kaya", "createdAtUtc": "…",
  "aiProvenance": [{ "draftId": "…", "feature": "OwnerExplanation", "edited": true, "acceptedByUserId": "…", "acceptedByName": "…", "acceptedAtUtc": "…" }]
}]
```

  Hata: `404 Examinations.NotFound` (yok / başka klinik). Kopyala ve yazdır istemcide.

### 7.3 AI-004 — Taburcu / evde bakım talimatı (`DischargeInstructions`)

Kayıt yeri: mevcut **bakım talimatı** (`CareInstruction`, `care-instructions` uçları; "hasta sahibine verilecek taburcu / evde bakım talimatı"). Yatıştaki `dischargeNote` hekimin iç taburcu notudur; ona yazılmaz, yalnız girdiye alınır.

- `POST /api/v1/examinations/{id}/ai/care-instruction-draft` — izin `Ai.Use` + `Examinations.Read` + `CareInstructions.Create`. Girdi: muayene + bağlı reçete/tedavi. Kabulde talimat **muayeneye** bağlanır (geliş muayeneden miras).
- `POST /api/v1/hospitalizations/{id}/ai/care-instruction-draft` — izin `Ai.Use` + `Hospitalizations.Read` + `CareInstructions.Create`. Girdi: yatış (neden, notlar, taburcu notu, tarihler) + yatışın muayenesi varsa onun alanları ve reçeteleri. İptal edilmiş yatış → `409 Hospitalizations.AlreadyCancelled`. Kabulde talimat **yatışa** bağlanır (muayene ve geliş yatıştan miras).
- Gövde isteğe bağlı `{ "text": "eklenecekler" }`, en çok 2000. `targetType`: `Examination` ya da `Hospitalization`.
- `content`: `{ "text": "İlaçlar:\n- …\nKontrol:\n- …" }` — en çok **4000** (model ~2500 hedefler). Başlıklar kayıtta karşılığı olanlardan: İlaçlar, Beslenme ve su, Hareket ve dinlenme, Yara ve bakım, Dikkat edilecek belirtiler, Kontrol.
- **Kabul:** mevcut `CreateCareInstructionCommand`; `appliedRecordId` = talimat kimliği. Talimat sonra mevcut `PUT /api/v1/care-instructions/{id}` ile düzenlenebilir (iz kalır).

## 8) Kayıtta "AI taslağından" izi (AI-010)

Kabul edilmiş taslak kayda kalıcı iz bırakır; kaynak `AiDrafts`'ın kalıcı karar alanlarıdır (hedef tabloya ayrı sütun yazılmaz). Kaydı okuyabilen herkes görür (`Ai.Use` gerekmez), klinik metin içermez:

```json
"aiProvenance": [
  { "draftId": "…", "feature": "ExamNoteStructure", "edited": false,
    "acceptedByUserId": "…", "acceptedByName": "Dr. Ayşe Kaya", "acceptedAtUtc": "2026-11-19T14:36:00Z" }
]
```

| Kayıt | Alan |
|---|---|
| Muayene detayı `GET /api/v1/examinations/{id}` | `aiProvenance` (AI-001 kabulleri; bir muayeneye birden çok olabilir, eskiden yeniye) |
| Bakım talimatı `GET /api/v1/care-instructions/{id}` ve liste | `aiProvenance` (AI-004) |
| Sahibe açıklama listesi | `aiProvenance` (AI-003) + `sourceAiDraftId` |

- `edited`: hekim taslağı kabul etmeden önce değiştirdi mi (`AcceptedWithEdits`). Kabulden sonra kayıt elle değişse de iz kalır; "kabulden sonra düzenlendi" ayrımı bu dalda yok (kaydın `updatedAtUtc` > `acceptedAtUtc` ile istemci çıkarabilir).
- AI-001'de iz muayene düzeyindedir: hangi alanların AI'dan geldiği saklanmaz (arayüz aktarım anındaki "N alan AI taslağından dolduruldu" şeridini kendi bilgisiyle gösterir).
- Boş liste = iz yok.

## 9) Bilinen sınırlar / açık noktalar

- Klinik kapalıyken (`Ai.ClinicNotEnabled`) taslaklar okunur ve karara bağlanır (varsayılan; kapatma yalnız üretimi durdurur).
- Süresi dolan taslak içeriği en geç bir tarama aralığı (varsayılan 60 dk) sonra veritabanından silinir; okuma anında hemen gizlenir.
- `[hasta]` yer tutucusu ek almaz (istem kuralı); model yine de ek getirirse ("Karamel'nın") Türkçe ek uyumu bozuk olabilir — hekim kabulden önce düzeltir.
- Bakım talimatı (`CareInstruction.Create`) denetim yükü mevcut desenle metni içerir (AI'dan bağımsız, önceden de böyle).
- Sekreterde `Ai.Use` yok: sekreter bakım talimatı yazabilir ama AI taslağı üretemez (2026-11-19 kararı).
