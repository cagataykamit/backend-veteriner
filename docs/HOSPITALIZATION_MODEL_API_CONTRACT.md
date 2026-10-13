# Yatış veri modeli ve bakım talimatı API sözleşmesi — Aşama 7 Dilim 2

Backend tek doğruluk kaynağıdır. Kaynak: `docs/ASAMA7_DISCOVERY.md` (D1, D2, D5, D7 kabul edildi; R1–R4 giderilir), `docs/ASAMA5_DISCOVERY.md` ve `LAB_RESULT_EXAMINATION_LOCK_CONTRACT.md` (kilit deseni), `LAB_RESULT_STATUS_API_CONTRACT.md` (RowVersion/VisitId deseni). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme. Migration **var** (`Hospitalizations` yeni kolonlar + `CareInstructions` tablosu). **Yeni izin var** (`CareInstructions.*`): izin seed'i kullanıcıdan. **Kapsam dışı:** talimat çıktısı/yazdırma/"hazırlandı" izi (Dilim 3), tamamlanma kontrolü (Dilim 4), Bugün/özet DTO'larına yatış alanı eklemek (Dilim 1), sorumlu hekim, konum listesi (D5: yalnız serbest metin).

**Frontend için değişenler `[DEĞİŞTİ]` / `[YENİ]` ile işaretlidir.** Eski ekranlar değişiklik yapılmadan çalışmaya devam eder; farklar §6'da.

---

## 1) Yatış (`Hospitalization`) — yeni alanlar

| Alan | Tür | Yazma | Not |
|---|---|---|---|
| `visitId` `[YENİ]` | guid? | POST/PUT | D1. Yatışın doğrudan geliş (visit) bağı; opsiyonel. Randevulu/randevusuz gelişe bağlanabilir. Eski kayıtlarda `null`. |
| `location` `[YENİ]` | string? (≤200) | POST/PUT | D5. Serbest metin konum (ör. "Kafes 3"). Kapasite/oda yönetimi yok. |
| `dischargeNote` `[YENİ]` | string? (≤4000) | yalnız taburcu | Taburcu notu. Giriş notunu (`notes`) **ezmez** (R1). |
| `rowVersion` `[YENİ]` | string (Base64, 8 bayt) | GET'te okunur; PUT/taburcu'ya geri gönderilir | Eşzamanlılık sürümü (§4). |

Değişmeyenler: `admittedAtUtc`, `plannedDischargeAtUtc`, `dischargedAtUtc`, `reason`, `notes`, `examinationId`, `isActive` ve hepsinin kuralları (tek aktif yatış, taburcu sonrası değişmezlik, tarih pencereleri).

### Yanıtlar

- `GET /hospitalizations/{id}`: `visitId`, `location`, `dischargeNote`, `rowVersion` eklenir (kayıtların sonuna).
- `GET /hospitalizations` (liste): `visitId`, `location` eklenir. `rowVersion` listede yok.
- Hayvan timeline'ında yatış olayının `relatedVisitId` değeri artık yatışın `visitId`'sidir (önceden hep `null`). Bugün/özet DTO'ları **değişmedi**.

## 2) Geliş (visit) bağı kuralları (D1)

`visitId` verilirse (POST) veya verilmezse nasıl çözüleceği (lab ile aynı tek kural):

1. Bağlı muayene varsa ve muayenenin gelişi varsa, açık `visitId` bununla aynı olmalıdır; çelişirse 400 `Hospitalizations.ExaminationVisitMismatch`.
2. Açık `visitId` yoksa muayenenin gelişi **miras alınır**.
3. Geliş bulunmalı ve iptal edilmemiş olmalıdır: 404 `Visits.NotFound`.
4. Gelişin hayvanı/kliniği yatışınkiyle aynı olmalıdır: 400 `Hospitalizations.VisitPetMismatch` / `Hospitalizations.VisitClinicMismatch`.

PUT'ta `visitId` gönderilmezse: muayene bağı değişmediyse mevcut `visitId` korunur; muayene değiştiyse yeni muayenenin gelişi miras alınır (yoksa `null`). Açık `visitId` verilirse doğrulanıp değiştirilir. Geliş bağını yalnız temizleme (`null`'a çekme) yoktur.
Geliş tamamlandığında aktif yatış uyarısı bu dilimde **yok** (Dilim 4).

## 3) Kesinleşmiş muayene kilidi (D7)

Karar: yatış canlı klinik kayıttır (taburcu onu değiştirir). Lab'daki "kimlik alanları kilitli" kısmi kilidin aksine yatışta **yalnız muayene bağı** kilitlidir; Aşama 5 Dilim 0 kilit kuralı (`Examination.EnsureAcceptsLinkedRecords`, tek yerde `ExaminationLinkedRecordLock`) aynen kullanılır.

| İşlem | Bağlı/hedef muayene kesinleşmiş ise |
|---|---|
| `POST /hospitalizations` ile `examinationId` olarak kesinleşmiş muayene vermek | **Reddedilir**: 409 `Examinations.Finalized`. (Yatış, kesinleşmiş muayeneye bağlanamaz; gerekirse `examinationId` olmadan `visitId` ile bağlanır.) |
| `PUT` ile `examinationId` değişiyor ve eski **veya** yeni muayene kesinleşmiş | **Reddedilir**: 409 `Examinations.Finalized` (kayıt kesinleşmiş muayeneden ayrılamaz, kesinleşmiş muayeneye taşınamaz). |
| `PUT` — `examinationId` aynı kalıyor (neden, not, konum, planlanan çıkış, geliş vb.) | **Serbest.** |
| `POST /hospitalizations/{id}/discharge` | **Serbest.** |
| Bakım talimatı yazma/değiştirme | **Serbest** (§5). |

Muayeneye bağlı olmayan veya taslak muayeneye bağlı yatışlarda kısıt yoktur. Yazma audit kaydı ve transaction işaretlidir (§4).

## 4) Eşzamanlılık, transaction, audit (R3)

- `rowVersion` (GET'ten alınan Base64) `PUT /hospitalizations/{id}` ve `POST /hospitalizations/{id}/discharge` gövdesinde gönderilir. Veritabanındaki sürüm farklıysa **409 `Hospitalizations.ConcurrencyConflict`** (kullanıcıya "formu yenileyip tekrar deneyin"). Biçimi geçersizse (Base64/8 bayt değil) 400 `Hospitalizations.Validation`.
- **Geçiş dönemi kuralı `[GEÇİCİ]`:** eski ekranlar `rowVersion` bilmediği için bu dilimde alan **opsiyoneldir**; gönderilmezse sunucu okuduğu anki sürümü kullanır (koruma yalnız okuma–yazma arası dar pencerede). Frontend kartı alanı göndermeye başlayınca zorunlu yapılacaktır (muayene/lab deseni); bu ayrı bir ürün/teknik karar olarak raporda açıktır.
- **Yanıt değişti `[DEĞİŞTİ]`:** `PUT` ve `discharge` artık **200** + `{ "id": "guid", "rowVersion": "base64" }` döner (eski: 204; lab PUT deseni). Gövdeyi yok sayan eski istemci etkilenmez. `POST /hospitalizations` yanıtı (201 + id) değişmedi.
- Üç yazma komutu (`Create`, `Update`, `Discharge`) tek transaction'da çalışır ve audit işaretlidir: `Hospitalization.Create`, `Hospitalization.Update`, `Hospitalization.Discharge` (hedef: yatış/hayvan id'si).

## 5) Yatış uçları — gövdeler

### `POST /hospitalizations` (`Hospitalizations.Create`)

Eski gövde + `[YENİ]` `visitId?`, `location?`. Diğer kurallar aynı.

### `PUT /hospitalizations/{id}` (`Hospitalizations.Update`)

Eski tam gövde (neden, giriş, planlanan çıkış, muayene, `notes`…) + `[YENİ]`:

| Alan | Anlam |
|---|---|
| `visitId` | §2. `null` = mevcut bağı koru / muayeneden miras. |
| `location` | Kısmi güncelleme: `null` = dokunma, `""` (boşluk) = temizle, dolu = değiştir. (Eski ekran gönderiminde konum bozulmaz.) |
| `rowVersion` | §4. |

`notes` PUT'ta **eskisi gibi tam değiştirilir** (null/boş = temizle; eski sözleşme korunur).

### `POST /hospitalizations/{id}/discharge` (`Hospitalizations.Discharge`)

```json
{ "dischargedAtUtc": "datetime", "dischargeNote": "string?", "notes": "string? (eski ad)", "rowVersion": "base64?" }
```

- `[DEĞİŞTİ]` Taburcu notu artık `Notes`'a değil **`dischargeNote`**'a yazılır; giriş notu (`notes`) taburcuda **korunur** (R1).
- `dischargeNote` verilmezse eski ad `notes` taburcu notu olarak kabul edilir (eski ekran uyumu; frontend kartında kaldırılacak `[GEÇİCİ]`). İkisi de verilirse `dischargeNote` kazanır. `null` = dokunma, `""` = temizle.
- Taburcu notu da yatışın `dischargeNote` alanında görünür; yatış taburcu edilmiş olduğundan sonradan değiştirilemez (`Hospitalizations.AlreadyDischarged`, değişmedi).
- Hasta sahibine verilecek talimat **bu alan değildir**; ayrı varlıktır (§6).

## 6) Bakım talimatı (`CareInstruction`) — yeni varlık (D2: ayrı "Talimat")

Hasta sahibine verilecek taburcu/evde bakım talimatı ayrı kayıttır; yatışa, muayeneye ve/veya geliş kaydına **opsiyonel** bağlanır (ayaktan hasta için de yazılabilir). Talimat metni yatış notunu ezmez. Çıktı üretimi, yazdırma ve "hazırlandı/yazdırıldı" izi Dilim 3'tür.

| Alan | Tür | Not |
|---|---|---|
| `id`, `tenantId`, `clinicId`, `petId` | guid | `clinicId` ve `petId` zorunlu. |
| `hospitalizationId`, `examinationId`, `visitId` | guid? | Bağlar opsiyonel; **oluşturulduktan sonra değişmez**. |
| `text` | string (1–4000, kırpılır) | Talimat metni. |
| `createdByUserId`, `createdAtUtc`, `updatedByUserId?`, `updatedAtUtc?` | | Sunucu atar. |
| `rowVersion` | string (Base64) | PUT'ta zorunlu. |

| Uç | Policy | Not |
|---|---|---|
| `POST /care-instructions` | `CareInstructions.Create` `[YENİ]` | 201 + id. Gövde: `clinicId`, `petId`, `hospitalizationId?`, `examinationId?`, `visitId?`, `text`. |
| `PUT /care-instructions/{id}` | `CareInstructions.Update` `[YENİ]` | Gövde: `text`, `rowVersion` (zorunlu), `id?`. Yanıt 200 + `{ id, rowVersion }`. Yalnız metin değişir. 409 `CareInstructions.ConcurrencyConflict`. |
| `GET /care-instructions/{id}` | `CareInstructions.Read` `[YENİ]` | Yukarıdaki alanların tümü. |
| `GET /care-instructions` | `CareInstructions.Read` `[YENİ]` | Sayfalı; süzgeçler `clinicId` (klinik bağlamı yoksa zorunlu), `petId`, `hospitalizationId`, `examinationId`, `visitId`; en yeni üstte. |

İzinler: Admin, Owner, ClinicAdmin, Veteriner → Read/Create/Update; Sekreter → yalnız Read (yatış ile aynı dağılım). **Seed gerekir** (aşağıda).

Bağ kuralları (oluştururken):
- Tüm bağlı kayıtlar kiracıya ait, aynı `petId` ve `clinicId`'de olmalıdır (`CareInstructions.HospitalizationPetMismatch` / `…ClinicMismatch`, `ExaminationPetMismatch` / `ExaminationClinicMismatch`; bulunamazsa 404 `Hospitalizations.NotFound` / `Examinations.NotFound` / `Visits.NotFound`).
- Yatış ve muayene birlikte verilirse yatışın muayenesi (varsa) aynı olmalı: `CareInstructions.HospitalizationExaminationMismatch`.
- Geliş §2 kuralıyla çözülür: açık `visitId` yoksa önce yatıştan, sonra muayeneden miras alınır; yatış/muayene gelişi ile çelişirse `CareInstructions.VisitMismatch`.
- Kesinleşmiş muayene **talimat için kilit değildir** (D7: talimat serbest); taburcu talimatı kesinleşmeden sonra yazılabilir. Taburcu edilmiş yatışa talimat yazılabilir.

## 7) Migration

Tek migration:
- `Hospitalizations`: `VisitId` (null), `Location` (nvarchar(200), null), `DischargeNote` (nvarchar(4000), null), `RowVersion` (rowversion; mevcut satırlar otomatik dolar). Mevcut kayıtlar ve export'lar etkilenmez; yeni kolonlar boş gelir.
- `CareInstructions` tablosu (yeni, boş).

Geriye uyum özeti: eski `POST/PUT/discharge` gövdeleri değişmeden kabul edilir; tek davranış farkı taburcuda `notes`'un `Notes` yerine `DischargeNote`'a yazılması (veri kaybını önlemek için bilinçli) ve PUT/discharge yanıtının 204 yerine 200 olmasıdır.

## 8) Hata kodları (yeni/değişen)

| Kod | HTTP | Ne zaman |
|---|---|---|
| `Hospitalizations.ConcurrencyConflict` | 409 | `rowVersion` eski. |
| `Examinations.Finalized` | 409 | §3. |
| `Hospitalizations.ExaminationVisitMismatch`, `.VisitPetMismatch`, `.VisitClinicMismatch` | 400 | §2. |
| `Visits.NotFound` | 404 | Geliş yok/iptal. |
| `CareInstructions.*` | | §6. |

## 9) Seed ve dağıtım notları

- Yeni izinler: `CareInstructions.Read/Create/Update`. `DbMigrator seed` **birleşik main'den** kullanıcı tarafından çalıştırılır; sonra kullanıcı çıkış–giriş yapar. Mevcut özel roller izinleri elle almalıdır.
- Query DB / read model etkilenmez (yatış ve talimat komut DB'den okunur).
