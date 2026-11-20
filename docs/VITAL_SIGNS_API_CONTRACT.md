# Vital ölçümü API sözleşmesi — HOSP-004

Backend tek doğruluk kaynağıdır. Kaynak: backlog HOSP-004 ("Vital ölçümü ve yatış izlemi"), ADR-043 (AK-9: vital ölçümü ayrı tipli kayıt türü; K4 gerekçeli iptal, K5 çekirdek, AK-6 gerekçeli geç kayıt, AK-10 konu değişmez). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme ve backend (dal `feature/vital-olcumu`, 2026-11-20). Migration **var** (`AddVitalMeasurements`, yalnız yeni tablo; paylaşılan/üretim veritabanına uygulanmadı). **Yeni izin var** (`VitalMeasurements.*`): izin seed'i gerekir (§8). **Kapsam dışı:** frontend (ekran tasarımı seçeneklerle ayrıca sunulacak), hasta özet kartındaki `latestWeight`'in bu kayıtla birleşmesi (§10), muayene vital alanlarının aralıklarının sıkılaştırılması (§10).

## 0) Kararlar

| # | Karar | Gerekçe |
|---|---|---|
| D1 | Ayrı tipli aggregate `VitalMeasurement`, tablo `VitalMeasurements` | ADR-043 K1 ve AK-9; JSON alan yok |
| D2 | Alanlar muayene vital alanlarıyla **aynı ad ve birim**: `temperatureC`, `heartRateBpm`, `respiratoryRatePerMin`, `weightKg` | Eğilimde iki kaynak aynı sütunlarla birleşir |
| D3 | En az bir değer zorunlu; aralıklar tek yerde `VitalSignLimits` (domain) | Kural tek yerde; içe aktarmanın ateş (25–45) ve kilo üst sınırı (9999,99) aynı sabitleri kullanır |
| D4 | Bağ: **yatış veya muayene, ikisi birden değil**; bağ yoksa serbest ölçüm. Bağ ve konu (hasta, klinik) oluşturulduktan sonra değişmez | Yanlış bağ/hasta için gerekçeli iptal + yeni kayıt (ADR-043 AK-10). İkisi birden gerekirse yatışın kendi muayene bağı vardır |
| D5 | Ölçüm zamanı: en çok 7 gün geriye, ileri zaman yok (5 dk saat kayması payı) | Diğer klinik kayıtlarla aynı geç kayıt penceresi; ölçüm gelecekte olamaz |
| D6 | Yatışa bağlı ölçüm yatış süresi içinde olmalı (`admittedAtUtc` ≤ ölçüm ≤ `dischargedAtUtc`, taburcu yoksa üst sınır yok); iptal edilmiş yatışa ölçüm bağlanamaz | Eğilim yatış penceresinden okunur |
| D7 | Kesinleşmiş muayeneye bağlanan yeni ölçüm **gerekçeli geç kayıt** (5–500) olarak yazılır; bağlı muayenesi kesinleşmiş ölçüm düzenlenemez (409), iptal edilebilir | ADR-043 AK-6 / ADR-013 Ek 2 (tedavi ile aynı) |
| D8 | Gerekçeli iptal (5–500), silme yok; iptal edilen kayıt değişmez, liste/zaman çizelgesi/eğilimde varsayılan gizli | ADR-043 K4 (`RecordCancellationRules`) |
| D9 | `rowVersion` PUT ve iptalde **zorunlu**; yanıt `{ id, rowVersion }` | ADR-043 K5; geçiş dönemi yok (yeni tür) |
| D10 | Yeni izinler `VitalMeasurements.Read/Create/Update`; dağılım yatış/bakım talimatı ile aynı (§8) | Ölçüm muayeneye de bağlanabildiği için yatış izni yeniden kullanılmadı (ADR-043 K2: tür başına izin) |
| D11 | Kimliğe göre okuma/yazmada kapsam dışı klinik **404** (`VitalMeasurements.NotFound`) | IDOR: kaydın varlığı sızmaz |
| D12 | Eğilim ucu iki kaynağı birleştirir: vital ölçümü + muayene vitalleri; her nokta `source` taşır | Backlog: "Kilo geçmişi muayene vitalleri ile bu kaydı birleştirir" |

## 1) Veri modeli (`VitalMeasurement`)

| Alan | Tür | Yazma | Not |
|---|---|---|---|
| `id`, `tenantId` | guid | sunucu | |
| `clinicId`, `petId` | guid | POST | Zorunlu; sonradan değişmez. |
| `hospitalizationId` | guid? | POST | D4, D6. |
| `examinationId` | guid? | POST | D4, D7. |
| `measuredAtUtc` | datetime | POST/PUT | Zorunlu; D5. |
| `temperatureC` | decimal? | POST/PUT | °C, 25,0–45,0; 1 ondalığa yuvarlanır. |
| `heartRateBpm` | int? | POST/PUT | Atım/dk, 1–400. |
| `respiratoryRatePerMin` | int? | POST/PUT | Solunum/dk, 1–200. |
| `weightKg` | decimal? | POST/PUT | kg, 0,01–9999,99; 2 ondalığa yuvarlanır (hasta profilindeki kilo ile aynı sütun biçimi `decimal(6,2)`; at ve sığır türleri katalogda olduğundan üst sınır 200 değil). |
| `note` | string? | POST/PUT | ≤ 500, kırpılır; boş = `null`. |
| `createdByUserId`, `createdAtUtc`, `updatedAtUtc?` | | sunucu | |
| `cancelledAtUtc?`, `cancelledByUserId?`, `cancelReason?` | | iptal | D8. |
| `addedAfterFinalization`, `lateEntryReason?` | bool, string? | POST | D7. |
| `rowVersion` | string (Base64, 8 bayt) | GET'te okunur; PUT/iptale geri gönderilir | D9. |

Yuvarlama `MidpointRounding.AwayFromZero` ile, aralık denetiminden **önce** yapılır (ör. `38,25` → `38,3`). Dört değerin hepsi boşsa 400.

## 2) Uçlar

| Uç | Policy | Yanıt |
|---|---|---|
| `POST /vital-measurements` | `VitalMeasurements.Create` | 201 + `guid` (`Location` → GET) |
| `PUT /vital-measurements/{id}` | `VitalMeasurements.Update` | 200 `{ id, rowVersion }` |
| `POST /vital-measurements/{id}/cancel` | `VitalMeasurements.Update` | 200 `{ id, rowVersion }` |
| `GET /vital-measurements/{id}` | `VitalMeasurements.Read` | 200 `VitalMeasurementDetailDto` |
| `GET /vital-measurements` | `VitalMeasurements.Read` | 200 `PagedResult<VitalMeasurementListItemDto>` |
| `GET /vital-measurements/series` | `VitalMeasurements.Read` | 200 `VitalSeriesDto` (§6) |
| `GET /vital-measurements/{id}/history` | `VitalMeasurements.Read` | 200 değişiklik geçmişi (`RECORD_HISTORY_API_CONTRACT.md`) |

Yazma komutları transaction ve audit işaretlidir: `VitalMeasurement.Create`, `VitalMeasurement.Update`, `VitalMeasurement.Cancel`.

### 2.1) `POST /vital-measurements`

```json
{
  "clinicId": "guid",
  "petId": "guid",
  "hospitalizationId": "guid?",
  "examinationId": "guid?",
  "measuredAtUtc": "2026-11-20T08:30:00Z",
  "temperatureC": 38.6,
  "heartRateBpm": 120,
  "respiratoryRatePerMin": 24,
  "weightKg": 12.45,
  "note": "string?",
  "lateEntryReason": "string? (bağlı muayene kesinleşmişse 5–500, zorunlu)"
}
```

Sıra: kiracı etkin → klinik bağlamı/erişimi → değerler ve zaman penceresi → klinik, hasta → bağ (yatış veya muayene) → geç kayıt.

### 2.2) `PUT /vital-measurements/{id}`

```json
{
  "id": "guid? (verilirse route ile aynı olmalı)",
  "measuredAtUtc": "datetime",
  "temperatureC": 38.4, "heartRateBpm": 110, "respiratoryRatePerMin": null, "weightKg": null,
  "note": null,
  "rowVersion": "base64 (zorunlu)"
}
```

- Değerler ve not **tam değiştirilir** (`null` = temizle); sonuçta en az bir değer kalmalıdır.
- Hasta, klinik ve bağlar gövdede yoktur, değişmez.
- Yatışa bağlı ölçümde yeni zaman yatış süresi içinde olmalıdır (D6).
- Bağlı muayene kesinleşmişse 409 `Examinations.Finalized` (geç kayıt olarak eklenmiş ölçüm dahil; düzeltme gerekçeli iptal + yeni kayıt).

### 2.3) `POST /vital-measurements/{id}/cancel`

```json
{ "reason": "string (5–500)", "rowVersion": "base64 (zorunlu)" }
```

Kesinleşmiş muayene kilidi ve yatışın durumu (taburcu, iptal) iptali engellemez.

### 2.4) `GET /vital-measurements/{id}`

```json
{
  "id": "guid", "tenantId": "guid", "clinicId": "guid", "petId": "guid", "petName": "Pamuk",
  "hospitalizationId": "guid?", "examinationId": "guid?",
  "measuredAtUtc": "datetime",
  "temperatureC": 38.6, "heartRateBpm": 120, "respiratoryRatePerMin": 24, "weightKg": 12.45,
  "note": "string?",
  "createdByUserId": "guid", "createdAtUtc": "datetime", "updatedAtUtc": "datetime?",
  "rowVersion": "base64",
  "cancelledAtUtc": "datetime?", "cancelReason": "string?",
  "addedAfterFinalization": false, "lateEntryReason": "string?"
}
```

İptal edilmiş kayıt da döner (`cancelledAtUtc` dolu).

### 2.5) `GET /vital-measurements` — hasta listesi

| Parametre | Anlamı |
|---|---|
| `petId` | **Zorunlu** (yoksa 400 `VitalMeasurements.Validation`). |
| `clinicId` | Opsiyonel klinik süzgeci; verilmezse kullanıcının erişebildiği tüm klinikler (hasta kartı klinikler arasıdır; zaman çizelgesi ile aynı kapsam kuralı). |
| `hospitalizationId`, `examinationId` | Opsiyonel bağ süzgeci. |
| `dateFromUtc`, `dateToUtc` | `measuredAtUtc` aralığı (iki uç dahil). `dateFromUtc > dateToUtc` → 400. |
| `includeCancelled` | Varsayılan `false`. |
| `page`, `pageSize` | 1…; `pageSize` 1–200 (varsayılan sayfa düzeni). |

Sıra: `measuredAtUtc` azalan, sonra `id` azalan (en yeni üstte). Sıralama parametresi yoktur.

```json
{ "items": [ {
    "id": "guid", "clinicId": "guid", "petId": "guid",
    "hospitalizationId": "guid?", "examinationId": "guid?",
    "measuredAtUtc": "datetime",
    "temperatureC": 38.6, "heartRateBpm": 120, "respiratoryRatePerMin": 24, "weightKg": 12.45,
    "note": "string?", "isCancelled": false, "addedAfterFinalization": false } ],
  "page": 1, "pageSize": 20, "totalItems": 1, "totalPages": 1 }
```

(Sayfa zarfı diğer listelerle aynı `PagedResult`.)

## 3) Doğrulama özeti

| Kural | Kod | HTTP |
|---|---|---|
| Değerlerin hepsi boş | `VitalMeasurements.ValueRequired` | 400 |
| Değer aralık dışında (§1) | `VitalMeasurements.Validation` | 400 |
| Biçim: `note` > 500, `clinicId`/`petId` boş, `hospitalizationId` ve `examinationId` birlikte, boş guid bağ, `measuredAtUtc` yok, liste/seri parametreleri | `Validation.FluentValidation` (`errors` alan bazlı) | 400 |
| `rowVersion` yok/geçersiz, iptal gerekçesi 5–500 değil | `VitalMeasurements.Validation` | 400 |
| Ölçüm zamanı 7 günden eski | `VitalMeasurements.MeasuredAtTooFarInPast` | 400 |
| Ölçüm zamanı gelecekte (5 dk payın ötesinde) | `VitalMeasurements.MeasuredAtInFuture` | 400 |
| Kesinleşmiş muayeneye gerekçesiz ölçüm | `VitalMeasurements.LateEntryReasonRequired` | 400 |
| Yatış süresi dışında ölçüm | `VitalMeasurements.OutsideHospitalization` | 400 |
| Bağlı kaydın hastası/kliniği farklı | `VitalMeasurements.HospitalizationPetMismatch`, `.HospitalizationClinicMismatch`, `.ExaminationPetMismatch`, `.ExaminationClinicMismatch` | 400 |

## 4) Hata kodları

| Kod | HTTP | Ne zaman |
|---|---|---|
| `VitalMeasurements.NotFound` | 404 | Kayıt yok, başka kiracıda ya da kullanıcının klinik kapsamı dışında (D11) |
| `VitalMeasurements.ConcurrencyConflict` | 409 | `rowVersion` eski |
| `VitalMeasurements.AlreadyCancelled` | 409 | İptal edilmiş kayıtta PUT ya da yeniden iptal |
| `VitalMeasurements.RouteIdMismatch` | 400 | PUT gövdesindeki `id` route ile farklı |
| `Examinations.Finalized` | 409 | D7 (PUT) |
| `Hospitalizations.AlreadyCancelled` | 409 | İptal edilmiş yatışa yeni ölçüm |
| `Pets.NotFound` / `Clinics.NotFound` / `Hospitalizations.NotFound` / `Examinations.NotFound` | 404 | Bağlı kayıt kiracıda yok |
| `Clinics.AccessDenied` | 403 | Oluştururken kullanıcının atanmadığı klinik |
| `VitalMeasurements.ClinicContextMismatch` | 400 | Gövdedeki klinik aktif klinik bağlamıyla çelişiyor |
| `Tenants.TenantInactive` | 403 | Pasif kiracıda yazma |
| (policy) | 403 | İzin yok |
| §3'teki doğrulama kodları | 400 | |

## 5) Zaman çizelgesi ve değişiklik geçmişi

- `GET /pets/{id}/timeline`: yeni tür **`VitalMeasurement`** (`typeOrder` = 13), varsayılan tür kümesinde; `VitalMeasurements.Read` ister (yoksa `omittedTypes`). `eventAtUtc` = `measuredAtUtc`; `title` = `"Vital ölçümü"`; `summary` = değerlerin kısa metni (ör. `"38,6 °C · Nabız 120/dk · Solunum 24/dk · 12,45 kg"`; not timeline'a konmaz); `status` iptalde `"Cancelled"`, aksi halde `null`; `isVoided` iptalde `true` (varsayılan gizli, `includeVoided=true` ile gelir); `relatedExaminationId` = bağlı muayene; `addedAfterFinalization` D7; `linkRef` = `{ type: "VitalMeasurement", id }`. Yatış bağı satırda yoktur (detay ucunda).
- Değişiklik geçmişi: tür adı `vitalMeasurement`; oluşturma, değer/zaman/not değişikliği ve iptal satır üretir (`TemperatureC`, `HeartRateBpm`, `RespiratoryRatePerMin`, `WeightKg`, `MeasuredAtUtc`, `Note`, `CancelledAtUtc`, `CancelReason`, …).

## 6) Eğilim (seri) ucu — `GET /vital-measurements/series`

Hasta kartı ve yatış detayı grafiği için iki kaynağı birleştirir. Yalnız okuma, sayfasız.

| Parametre | Anlamı |
|---|---|
| `petId` | **Zorunlu**. Hasta kiracıda yoksa 404 `Pets.NotFound`. |
| `clinicId` | Opsiyonel; verilmezse kullanıcının erişebildiği klinikler. |
| `hospitalizationId` | Opsiyonel. Verilirse yatış aynı hastanın olmalı ve kapsamda olmalı (yoksa 404 `Hospitalizations.NotFound`); pencere `[admittedAtUtc, dischargedAtUtc]` (taburcu yoksa üst sınırsız) olur ve klinik **yatışın kliniğine** daraltılır. |
| `fromUtc`, `toUtc` | Opsiyonel: `fromUtc <= measuredAtUtc < toUtc` (yarı açık; zaman çizelgesi ile aynı). Yatış penceresiyle kesişimi alınır. `fromUtc >= toUtc` → 400. |
| `limit` | Varsayılan 500, en çok 1000. Daha fazla nokta varsa **en yeni** `limit` nokta döner ve `truncated: true`. |

Kaynaklar (yatış penceresinde **bağından bağımsız** o hastanın tüm ölçümleri gelir):

| `source` | Nereden | `measuredAtUtc` | Koşul |
|---|---|---|---|
| `VitalMeasurement` | Vital ölçümü kaydı | `measuredAtUtc` | İptal edilmemiş |
| `Examination` | Muayenenin vital alanları | `vitalsMeasuredAtUtc` | En az bir vital dolu; kullanıcıda `Examinations.Read` yoksa bu kaynak atlanır ve `omittedSources` içinde döner |

```json
{
  "points": [
    { "source": "Examination", "sourceId": "guid", "clinicId": "guid", "measuredAtUtc": "2026-11-19T09:00:00Z",
      "temperatureC": 39.1, "heartRateBpm": null, "respiratoryRatePerMin": null, "weightKg": 12.6,
      "hospitalizationId": null, "examinationId": "guid (= sourceId)" },
    { "source": "VitalMeasurement", "sourceId": "guid", "clinicId": "guid", "measuredAtUtc": "2026-11-19T15:00:00Z",
      "temperatureC": 38.6, "heartRateBpm": 120, "respiratoryRatePerMin": 24, "weightKg": null,
      "hospitalizationId": "guid?", "examinationId": "guid?" }
  ],
  "truncated": false,
  "omittedSources": []
}
```

- Sıra **artan** zaman (grafik sırası), eşit zamanda `source` sonra `sourceId`.
- Nokta, kaynaktaki dolu değerleri taşır; boş değer `null` (grafik çizgisi boşluğu atlar). Kilo geçmişi = `weightKg` dolu noktalar.
- `Examination` noktasında `examinationId` = `sourceId`, `hospitalizationId` = `null`. `VitalMeasurement` noktasında kaydın kendi bağlarıdır.
- Muayenenin durumu (taslak/kesinleşmiş) süzülmez; muayene iptali yoktur.

## 7) Migration

`AddVitalMeasurements`: yalnız yeni `VitalMeasurements` tablosu (iptal ve geç kayıt tutarlılık denetim kısıtları, `TenantId+PetId+MeasuredAtUtc`, `TenantId+ClinicId`, `TenantId+HospitalizationId`, `TenantId+ExaminationId` indeksleri). Mevcut tablolara dokunmaz. Paylaşılan/üretim veritabanına uygulanmadı; dağıtımda uygulanır.

## 8) İzinler ve seed

| İzin | Admin | Owner | ClinicAdmin | Veteriner | Sekreter |
|---|---|---|---|---|---|
| `VitalMeasurements.Read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `VitalMeasurements.Create` | ✓ | ✓ | ✓ | ✓ | — |
| `VitalMeasurements.Update` (düzenleme + iptal) | ✓ | ✓ | ✓ | ✓ | — |

Yatış ve bakım talimatı ile aynı dağılım (tıbbi kayıtlar resepsiyonda yalnız okunur). Sistemde ayrı hemşire/teknisyen rolü yoktur. **Seed gerekir:** dağıtımdan sonra `DbMigrator seed` (birleşik main'den) kullanıcı tarafından çalıştırılır, kullanıcılar çıkış–giriş yapar; özel roller izinleri elle almalıdır. Seed çalışmadan önce bu uçlar platform yöneticisi dışında 403 döner, zaman çizelgesinde tür `omittedTypes`'ta görünür.

## 9) Diğer etkiler

- Kurum veri dışa aktarımı: yeni dosya `vital_olcumleri.csv` (`DATA_EXPORT_API_CONTRACT.md`).
- İçe aktarma geri alma: hastaya bağlı vital ölçümü hastanın silinmesini engeller, neden kodu `HasVitalMeasurements` (`IMPORT_API_CONTRACT.md` §9).
- Örnek klinik verisi temizliği hastanın vital ölçümlerini de siler.
- Query DB / read model etkilenmez (komut DB'den okunur).

## 10) Açık / sonraki dilim

- Hasta özet kartındaki `latestWeight` bugün yalnız muayeneden okunur (`PET_TIMELINE_API_CONTRACT.md` §1); vital ölçümüyle birleşmesi DTO'ya alan ekler (`examinationId` yerine kaynak kimliği) — ayrı dilim.
- Muayene vital alanları yalnız `> 0` denetler; `VitalSignLimits` aralıklarına çekilmesi mevcut davranışı değiştirir — ürün kararı.

## 11) Testler

- Domain: değer aralıkları, yuvarlama, en az bir değer, not uzunluğu, iptal (gerekçe, zaten iptal, iptal sonrası değişmezlik), geç kayıt gerekçesi.
- Application: oluşturma (bağ uyuşmazlıkları, yatış penceresi, iptal edilmiş yatış, kesinleşmiş muayene geç kaydı), güncelleme (sürüm, kesinleşmiş muayene kilidi, kapsam 404), iptal, eğilim birleştirme (kaynak, sıra, yatış penceresi, sınır, izin).
- Entegrasyon: HTTP mutlu yol (oluştur → detay → liste → PUT → iptal → geçmiş → zaman çizelgesi), kapsam dışı klinik 404, izin 403, eski sürüm 409, eğilimin muayene kilosunu birleştirmesi.
