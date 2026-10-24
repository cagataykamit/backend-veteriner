# Yatış veri modeli ve bakım talimatı API sözleşmesi — Aşama 7 Dilim 2

Backend tek doğruluk kaynağıdır. Kaynak: `docs/ASAMA7_DISCOVERY.md` (D1, D2, D5, D7 kabul edildi; R1–R4 giderilir), `docs/ASAMA5_DISCOVERY.md` ve `LAB_RESULT_EXAMINATION_LOCK_CONTRACT.md` (kilit deseni), `LAB_RESULT_STATUS_API_CONTRACT.md` (RowVersion/VisitId deseni). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme. Migration **var** (`Hospitalizations` yeni kolonlar + `CareInstructions` tablosu). **Yeni izin var** (`CareInstructions.*`): izin seed'i kullanıcıdan. **Kapsam dışı:** talimat çıktısı/yazdırma/"hazırlandı" izi (Dilim 3), tamamlanma kontrolü (Dilim 4), Bugün/özet DTO'larına yatış alanı eklemek (Dilim 1), sorumlu hekim.

> **HOSP-002 — Yatış konum ataması (2026-10-24, dal `feat/hosp-002-ward-room`):** D5'teki "konum listesi yok" kararı değişti; klinik tanımlı **Bölüm / Oda** modeli eklendi (§10). Migration `20261024180200_AddHospitalizationWardsAndRooms` üretildi (`HospitalizationWards`, `HospitalizationRooms` tabloları + `Hospitalizations.RoomId`), **hiçbir paylaşılan/geliştirme veritabanına uygulanmadı**. **Yeni izin yok**, seed gerekmez (yalnız yeni composite okuma policy'si `Clinics_HospitalizationWardsRead`, §10.2).

**Frontend için değişenler `[DEĞİŞTİ]` / `[YENİ]` ile işaretlidir.** Eski ekranlar değişiklik yapılmadan çalışmaya devam eder; farklar §6'da ve §10.6'da.

---

## 1) Yatış (`Hospitalization`) — yeni alanlar

| Alan | Tür | Yazma | Not |
|---|---|---|---|
| `visitId` `[YENİ]` | guid? | POST/PUT | D1. Yatışın doğrudan geliş (visit) bağı; opsiyonel. Randevulu/randevusuz gelişe bağlanabilir. Eski kayıtlarda `null`. |
| `location` `[YENİ]` | string? (≤200) | POST/PUT | D5. Serbest metin konum (ör. "Kafes 3"); geriye dönük uyum için kalır. Oda seçilince (`roomId`) sunucu bu metni odadan türetir: **"Bölüm / Oda"** (§10.4). Kapasite/çakışma kontrolü yok. |
| `roomId` `[YENİ · HOSP-002]` | guid? | POST/PUT | Klinik tanımlı oda (§10). Eski kayıtlarda ve oda seçilmemişse `null`. |
| `roomName`, `wardName` `[YENİ · HOSP-002]` | string? | yalnız okuma | Odanın ve bölümünün **o anki** adı (pasifleşmiş olsa da döner). `roomId` null ise ikisi de null. |
| `dischargeNote` `[YENİ]` | string? (≤4000) | yalnız taburcu | Taburcu notu. Giriş notunu (`notes`) **ezmez** (R1). |
| `rowVersion` `[YENİ]` | string (Base64, 8 bayt) | GET'te okunur; PUT/taburcu'ya geri gönderilir | Eşzamanlılık sürümü (§4). |

Değişmeyenler: `admittedAtUtc`, `plannedDischargeAtUtc`, `dischargedAtUtc`, `reason`, `notes`, `examinationId`, `isActive` ve hepsinin kuralları (tek aktif yatış, taburcu sonrası değişmezlik, tarih pencereleri).

### Yanıtlar

- `GET /hospitalizations/{id}`: `visitId`, `location`, `dischargeNote`, `rowVersion` eklenir (kayıtların sonuna); HOSP-002 ile `roomId`, `roomName`, `wardName` eklenir.
- `GET /hospitalizations` (liste): `visitId`, `location` eklenir; HOSP-002 ile `roomId`, `roomName`, `wardName` eklenir. `rowVersion` listede yok.
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

Eski gövde + `[YENİ]` `visitId?`, `location?`, `[YENİ · HOSP-002]` `roomId?` (§10.4). Diğer kurallar aynı.

### `PUT /hospitalizations/{id}` (`Hospitalizations.Update`)

Eski tam gövde (neden, giriş, planlanan çıkış, muayene, `notes`…) + `[YENİ]`:

| Alan | Anlam |
|---|---|
| `visitId` | §2. `null` = mevcut bağı koru / muayeneden miras. |
| `location` | Kısmi güncelleme: `null` = dokunma, `""` (boşluk) = temizle, dolu = değiştir. (Eski ekran gönderiminde konum bozulmaz.) `roomId` gönderildiyse yok sayılır. |
| `roomId` `[HOSP-002]` | Kısmi güncelleme: `null` = dokunma (eski ekran), dolu = odayı değiştir (§10.4). |
| `clearRoom` `[HOSP-002]` | `true` = oda bağını kaldır; `roomId` ile birlikte gönderilemez (400 `Hospitalizations.Validation`). Konum metni `location` kuralına göre kalır/temizlenir. |
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
- Geliş §2 kuralıyla çözülür: açık `visitId` yoksa önce yatıştan, sonra muayeneden miras alınır; yatışın gelişi ile çelişirse `CareInstructions.VisitMismatch`, muayenenin gelişi ile çelişirse `CareInstructions.ExaminationVisitMismatch` (400).
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
| `Hospitalizations.RoomNotFound`, `.RoomInactive`, `HospitalizationWards.*`, `HospitalizationRooms.*` `[HOSP-002]` | | §10.5. |

## 9) Seed ve dağıtım notları

- Yeni izinler: `CareInstructions.Read/Create/Update`. `DbMigrator seed` **birleşik main'den** kullanıcı tarafından çalıştırılır; sonra kullanıcı çıkış–giriş yapar. Mevcut özel roller izinleri elle almalıdır.
- Query DB / read model etkilenmez (yatış ve talimat komut DB'den okunur).
- HOSP-002: yeni izin yok; `Clinics.Update` (yazma) ve `Hospitalizations.Read` / `Clinics.Read` (okuma) mevcut izinlerdir. Migration `AddHospitalizationWardsAndRooms` dağıtımda uygulanır; yeni tablolar boş gelir, mevcut yatışlarda `RoomId = null`.

---

## 10) Yatış konum ataması — Bölüm / Oda (HOSP-002) `[YENİ]`

Ürün kararı (2026-10-24): iki seviyeli, **klinik bazlı** tanım. **Bölüm** (ward; ör. "Yoğun bakım", "Kedi servisi") ve altında **Oda/kafes** (room). Kapasite ve çakışma kontrolü **yok**: aynı odaya birden fazla aktif yatış atanabilir. Tanımlar Ayarlar > Tanımlar'da klinik seçilerek yönetilir; yatış kaydı **odaya** bağlanır, bölüm odadan türetilir. Tanımlar silinmez, **pasife alınır**: pasif oda/bölüm yeni yatışa atanamaz, mevcut yatışlar etkilenmez (odası pasifleşen yatış odasında kalır, DTO'da adı görünür).

### 10.1) Veri modeli

| Varlık | Alanlar | Kural |
|---|---|---|
| `HospitalizationWard` | `id`, `clinicId`, `name` (1–100, kırpılır), `sortOrder` (int), `isActive` | Kiracı + klinik içinde **aktif** bölümler arasında ad benzersiz (case-insensitive; DB'de filtreli unique indeks `IsActive = 1`). Pasif bölümle aynı ad yeniden kullanılabilir; pasif bölüm yeniden etkinleştirilirken ad çakışırsa 409. |
| `HospitalizationRoom` | `id`, `wardId`, `name` (1–100, kırpılır), `sortOrder`, `isActive` | Aynı bölümün **aktif** odaları arasında ad benzersiz (aynı kural). Oda bölümler arası **taşınmaz**. Bölüm pasifken oda eklenebilir/düzenlenebilir; ancak pasif bölümdeki oda yatışa atanamaz. |

### 10.2) Uçlar — tanımlar (`/api/v1/clinics/{id}/hospitalization-wards`)

| Uç | Policy | Yanıt | Not |
|---|---|---|---|
| `GET /clinics/{id}/hospitalization-wards` | **`Clinics_HospitalizationWardsRead`** (any-of: `Hospitalizations.Read`, `Clinics.Read`, `Clinics.Update`) | 200 `HospitalizationWardDto[]` | Aktif/pasif **hepsi**; `sortOrder`, sonra ad sıralı; her bölümün `rooms` dizisi aynı sırayla. Yatış formu `isActive` ile süzer; Tanımlar ekranı hepsini gösterir. |
| `POST /clinics/{id}/hospitalization-wards` | `Clinics.Update` | 201 + `guid` (`Location` başlığı GET listesi) | Gövde `{ "name": "string", "sortOrder": 0 }`. |
| `PUT /clinics/{id}/hospitalization-wards/{wardId}` | `Clinics.Update` | 200 `HospitalizationWardDto` | Gövde `{ "id"?: guid, "name", "sortOrder" }`; `id` verilirse route ile aynı olmalı (400 `HospitalizationWards.RouteIdMismatch`). Aktif/pasif bu uçla değişmez. |
| `POST /clinics/{id}/hospitalization-wards/{wardId}/deactivate` | `Clinics.Update` | 200 `HospitalizationWardDto` | İdempotent. Odalar değişmez. |
| `POST /clinics/{id}/hospitalization-wards/{wardId}/activate` | `Clinics.Update` | 200 `HospitalizationWardDto` | İdempotent; ad çakışırsa 409 `HospitalizationWards.DuplicateName`. |
| `POST /clinics/{id}/hospitalization-wards/{wardId}/rooms` | `Clinics.Update` | 201 + `guid` | Gövde `{ "name", "sortOrder" }`. |
| `PUT /clinics/{id}/hospitalization-wards/{wardId}/rooms/{roomId}` | `Clinics.Update` | 200 `HospitalizationRoomDto` | Gövde `{ "id"?: guid, "name", "sortOrder" }`; `id` route ile farklıysa 400 `HospitalizationRooms.RouteIdMismatch`. |
| `POST …/rooms/{roomId}/deactivate` · `POST …/rooms/{roomId}/activate` | `Clinics.Update` | 200 `HospitalizationRoomDto` | İdempotent; etkinleştirmede ad çakışırsa 409 `HospitalizationRooms.DuplicateName`. |

Klinik erişimi tüm uçlarda aynıdır (çalışma saatleri deseni): klinik kiracıya ait değilse 404 `Clinics.NotFound`; tenant-wide olmayan kullanıcı (ClinicAdmin vb.) yalnız atandığı klinikte okur/yazar, değilse 403 `Clinics.AccessDenied`. Yazma uçlarında izin handler'da da denetlenir (403 `Auth.PermissionDenied`).

```json
// HospitalizationWardDto
{
  "id": "guid", "clinicId": "guid", "name": "Yoğun bakım", "sortOrder": 1, "isActive": true,
  "rooms": [
    { "id": "guid", "wardId": "guid", "name": "Kafes 1", "sortOrder": 1, "isActive": true },
    { "id": "guid", "wardId": "guid", "name": "Kafes 2", "sortOrder": 2, "isActive": false }
  ]
}
```

### 10.3) Yatış DTO'ları `[DEĞİŞTİ]`

`GET /hospitalizations/{id}` ve `GET /hospitalizations` kayıtlarına eklenir (kayıtların sonuna): `roomId` (guid?), `roomName` (string?), `wardName` (string?). Adlar tanımın **o anki** adıdır (yeniden adlandırma geçmiş yatışlara yansır); `location` ise atama anında türetilmiş **metin**dir ve odayı/bölümü yeniden adlandırmak onu değiştirmez. Kart: `roomId` varsa "wardName / roomName", yoksa `location` serbest metni gösterilir.

### 10.4) Yatışta oda seçimi (`POST /hospitalizations`, `PUT /hospitalizations/{id}`)

| Durum | Davranış |
|---|---|
| `roomId` verildi | Oda **yatışın kliniğine** ve kiracıya ait olmalı (değilse 404 `Hospitalizations.RoomNotFound`; başka kiracının/kliniğin odası sızdırılmaz). Oda **ve bölümü** aktif olmalı (değilse 409 `Hospitalizations.RoomInactive`). Başarıda `location` sunucuda `"{wardName} / {roomName}"` olarak yazılır; gövdedeki `location` yok sayılır. |
| PUT: `roomId` mevcut odayla aynı | Oda/bölüm pasifleşmiş olsa da **kabul edilir** (mevcut yatış etkilenmez); konum metni yeniden türetilir. |
| PUT: `roomId` = `null` | Oda bağına dokunulmaz (eski ekran uyumu). İstisna: PUT ile `clinicId` değişiyorsa eski kliniğin odası bağlı kalamaz → bağ kaldırılır, `location` metni kalır. |
| PUT: `clearRoom: true` | Oda bağı kaldırılır (`roomId` null olur); `location` metni `location` alanı kuralına göre kalır (`null`) veya temizlenir (`""`). `roomId` ile birlikte gönderilirse 400. |
| Taburcu edilmiş yatış | Oda değişmez (409 `Hospitalizations.AlreadyDischarged`, mevcut kural). |

Oda doğrulaması muayene/geliş doğrulamalarından **sonra**, yazımdan önce yapılır; hata durumunda kayıt değişmez.

### 10.5) Hata kodları (HOSP-002)

| Kod | HTTP | Ne zaman |
|---|---|---|
| `Hospitalizations.RoomNotFound` | 404 | `roomId` kiracıda/klinikte yok (başka kiracı, başka klinik, silinmiş). |
| `Hospitalizations.RoomInactive` | 409 | Oda veya bölümü pasif; yeni atama. |
| `Hospitalizations.Validation` | 400 | `roomId` boş guid; `roomId` + `clearRoom` birlikte. |
| `HospitalizationWards.NotFound` / `HospitalizationRooms.NotFound` | 404 | Tanım klinikte yok (oda için bölüm de uyuşmalı). |
| `HospitalizationWards.DuplicateName` / `HospitalizationRooms.DuplicateName` | 409 | Aktif tanımlar arasında ad çakışması (oluştur, yeniden adlandır, etkinleştir). |
| `HospitalizationWards.Validation` / `HospitalizationRooms.Validation` | 400 | Ad boş veya 100 karakterden uzun. |
| `HospitalizationWards.RouteIdMismatch` / `HospitalizationRooms.RouteIdMismatch` | 400 | Body `id` route ile farklı. |
| `Clinics.NotFound` / `Clinics.AccessDenied` / `Auth.PermissionDenied` | 404 / 403 / 403 | Klinik erişimi (§10.2). |

### 10.6) Frontend etki özeti

- **Ayarlar > Tanımlar > Yatış bölümleri (yeni ekran):** klinik seç → `GET …/hospitalization-wards`; bölüm ekle/adlandır/sırala, pasife al/etkinleştir; bölüm altında oda ekle/adlandır/sırala, pasife al/etkinleştir. Silme yok. `Clinics.Update` olmayan kullanıcıya salt okunur.
- **Yatış formu (oluştur/düzenle):** klinik seçilince `GET …/hospitalization-wards` ile aktif bölüm → aktif oda seçicisi; seçilen `roomId` POST/PUT'a gider, `location` alanı gönderilmez (sunucu türetir). "Oda yok / serbest metin" seçeneği için `roomId` göndermeden `location` gönderilir. Düzenlemede odayı kaldırmak için `clearRoom: true`. Mevcut yatış pasif odadaysa seçicide o oda "(pasif)" etiketiyle gösterilmeli (aynı `roomId` geri gönderilebilir).
- **Yatış listesi/kartı:** `wardName / roomName` (yoksa `location`).
- **Eski ekranlar:** `roomId`/`clearRoom` göndermeyen PUT oda bağını değiştirmez; `location` kuralı aynen.
- Smoke: bölüm/oda CRUD + 409 çakışma; odalı yatış oluştur → detay/liste `roomName`/`wardName`/`location`; pasif oda → 409; başka klinik odası → 404; `clearRoom`.
