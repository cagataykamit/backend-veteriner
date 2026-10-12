# Lab istem/durum API sözleşmesi — Aşama 6 Dilim 1

Backend tek doğruluk kaynağıdır. Kaynak: `docs/ASAMA6_DISCOVERY.md` (D1, D2, D3, D5 kabul edildi), `docs/LAB_RESULT_EXAMINATION_LOCK_CONTRACT.md` (Dilim 0). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme. **Kapsam dışı:** dosya yükleme (Dilim 2/4), klinik geneli bekleyenler ucu (Dilim 5), `RequestId` idempotency, ücret kaynağı bağı, yeni izin.

---

## 1) Kavramlar

- **Tek kayıt, durum alanı (D1).** Mevcut `LabResult` kaydı istek ve sonucu birlikte taşır; ayrı istem varlığı yoktur.
- **Durumlar (D3):** `Requested` (İstendi) → `Resulted` (Sonuçlandı); ikisinden de `Cancelled` (İptal). `Cancelled` son durumdur.
- **"Değerlendirme bekliyor" durum değildir.** `awaitingInterpretation` türetilmiş bayraktır: `status = Resulted` **ve** `interpretation` boş. Durum makinesine girmez; hekim yorum yazınca kalkar.
- **Sonuç alanları (`resultText`, `resultDateUtc`) `Requested` durumunda boştur**, `Resulted` durumunda ikisi de dolu olmalıdır (ikisi birlikte verilir/verilmez).
- **İstek zamanı sunucuda atanır** (`requestedAtUtc = şimdi`), istemci veremez. `requestedByUserId` oturumdaki kullanıcıdır.
- **Geliş bağı (`visitId`).** Opsiyonel. `visitId` verilmezse ve muayenenin gelişi varsa muayeneden miras alınır.

## 2) Geriye uyumlu dönüşüm (migration)

Mevcut tüm kayıtlar `Resulted` olur. `requestedAtUtc` = eski `resultDateUtc` (eski timeline konumu değişmez), `requestedByUserId` = null, `visitId` = null, iptal alanları null. `rowVersion` otomatik dolar. Eski `POST` gövdesi (sonuç metni + tarih dolu) aynen `Resulted` kayıt üretir.

> Not: Mevcut kayıtlarda `interpretation` boşsa `awaitingInterpretation = true` döner (eski veride "değerlendirme bekliyor" ayrımı yapılamaz). İstemci bunu eski kayıtlarda göstermek istemiyorsa karar gerekir (açık soru).

---

## 3) Uçlar ve yetkiler

Yeni izin **yoktur** (seed gerekmez).

| Uç | Policy | Not |
|---|---|---|
| `POST /lab-results` | `LabResults.Create` | İstem (sonuçsuz) veya doğrudan sonuçlu kayıt. 201 + id. |
| `PUT /lab-results/{id}` | `LabResults.Update` | Tam gövde + **`rowVersion` zorunlu**. İstem düzenler; `Requested`'a sonuç yazılırsa `Resulted` olur. |
| `POST /lab-results/{id}/cancel` | `LabResults.Update` | Gerekçeli iptal (`rowVersion` zorunlu). Yeni izin açılmadı (Veteriner/yönetim zaten `Update` sahibi; Sekreter yalnız okur). |
| `GET /lab-results/{id}` | `LabResults.Read` | Yeni alanlarla. |
| `GET /lab-results` | `LabResults.Read` | Yeni alanlar + `status` süzgeci. |

### `POST /lab-results`

```json
{
  "clinicId": "guid", "petId": "guid", "examinationId": "guid?", "visitId": "guid?",
  "testName": "string",
  "resultDateUtc": "datetime?", "resultText": "string?",
  "interpretation": "string?", "notes": "string?"
}
```

| `resultText` / `resultDateUtc` | Sonuç |
|---|---|
| ikisi de yok/boş | `Requested` kaydı. `interpretation` verilemez (sonuç yokken yorum anlamsız) → `LabResults.Validation`. |
| ikisi de dolu | `Resulted` kaydı. Tarih penceresi: en çok 7 gün geçmiş, 2 yıl gelecek (`LabResults.DateTooFarInPast/InFuture`, değişmedi). |
| yalnız biri | 400 `LabResults.Validation`. |

Kesinleşmiş muayeneye ekleme serbesttir (Dilim 0, değişmedi). Yanıt: 201 + `id` (değişmedi). İstemci `rowVersion` için kaydı GET ile okur.

### `PUT /lab-results/{id}`

Gövde POST ile aynı alanlar + `rowVersion` (GET'ten alınan Base64, zorunlu) + `id` (opsiyonel, rota ile uyuşmalı). Yanıt: **200** + `{ "id": "guid", "rowVersion": "base64" }` (muayene PUT deseni; eski 204 yerine — eski istemciler gövdeyi yok sayabilir).

| Mevcut durum | Gövde | Sonuç |
|---|---|---|
| `Requested` | sonuç yok | Alanlar güncellenir, `Requested` kalır. |
| `Requested` | sonuç metni + tarih | **`Resulted`** olur (sonuç girişi). |
| `Resulted` | sonuç metni + tarih | Güncellenir. |
| `Resulted` | sonuç boş | 400 `LabResults.Validation` (sonuç geri silinemez; yanlış kayıt iptal edilir). |
| `Cancelled` | herhangi | 409 `LabResults.AlreadyCancelled`. |

Kesinleşmiş muayene kilidi (Dilim 0) aynen geçerlidir: yalnız sonuç alanları (`resultDateUtc`, `resultText`, `interpretation`, `notes`) serbest; `testName`, `petId`, `clinicId`, `examinationId` değişimi `409 Examinations.Finalized`. **Sonuç girişi (`Requested`→`Resulted`) kesinleşmiş muayenede serbesttir** (D2).
`visitId` PUT'ta verilmezse: muayene değişmediyse mevcut bağ korunur, muayene değiştiyse yeni muayenenin gelişi miras alınır; verilirse doğrulanır ve değiştirilebilir. Muayenenin gelişi ile `visitId` çelişirse `LabResults.ExaminationVisitMismatch` (400).

### `POST /lab-results/{id}/cancel`

```json
{ "reason": "5-500 karakter", "rowVersion": "base64" }
```

Yanıt 200 + `{ id, rowVersion }`. Kurallar:
- Gerekçe `CorrectionReason` kuralı (5–500, kırpılmış) → `LabResults.Validation`.
- Zaten iptalse 409 `LabResults.AlreadyCancelled`.
- `Requested` kaydı iptali kesinleşmiş muayenede de serbesttir (henüz sonuç yazılmadı, klinik içerik değişmez).
- `Resulted` kaydı iptali, kayıt kesinleşmiş muayeneye bağlıysa reddedilir (`409 Examinations.Finalized`): girilmiş klinik sonuç kilitli muayeneden kaldırılamaz. Muayenesiz/taslak muayeneli kayıtta serbesttir.
- İptal kaydı silmez; `cancelledAtUtc/cancelledByUserId/cancelReason` dolar.

### Eşzamanlılık

`rowVersion` SQL Server `rowversion` (8 bayt) Base64 metnidir. PUT/cancel'da gönderilen sürüm veritabanındakiyle uyuşmazsa **409 `LabResults.ConcurrencyConflict`**; istemci formu yenileyip tekrar dener (iki sekmede son yazan kazanmaz). `rowVersion` eksik/geçersiz → 400 `LabResults.Validation` (kayıt bulunması ve klinik yetkisi denetimlerinden **sonra** doğrulanır; yetkisiz istek 403/404 alır).

---

## 4) Okuma

### `GET /lab-results/{id}` — `LabResultDetailDto`

Eklenen/değişen alanlar: `status` (`"Requested"|"Resulted"|"Cancelled"`), `requestedAtUtc`, `requestedByUserId?`, `visitId?`, `awaitingInterpretation`, `cancelledAtUtc?`, `cancelledByUserId?`, `cancelReason?`, `rowVersion`; **`resultDateUtc` ve `resultText` artık nullable**.

### `GET /lab-results` — `LabResultListItemDto`

Eklenen: `status`, `requestedAtUtc`, `awaitingInterpretation`; `resultDateUtc` nullable. Yeni süzgeç `status`. **Varsayılan olarak `Cancelled` gizlidir** (Aşama 4 D4); `status=Cancelled` ile görülür. `dateFromUtc/dateToUtc` ve sıralama (yeniden eskiye) artık **`requestedAtUtc`** üzerindendir (eski kayıtlarda sonuç tarihiyle aynı). `search` `resultText`'i de tarar (null güvenli).

### Özetler ve timeline

- **Muayene ilişkili özet** (`ExaminationRelatedLabResultItemDto`) ve **hayvan geçmiş özeti** (`PetHistoryLabResultItemDto`): `status`, `requestedAtUtc`, `awaitingInterpretation` eklenir, `resultDateUtc` nullable; iptaller listelenmez.
- **Timeline (D5):** lab tek satırdır, **`requestedAtUtc`** zamanında durur; sonuçlanınca aynı satırın `status`'ü `Resulted` olur. `status` = durum adı, iptal `isVoided = true` (varsayılan gizli, `includeVoided` ile gelir), `relatedVisitId = visitId`. Başlık `testName`.

---

## 5) Hata kodları

| Durum | Kod | HTTP |
|---|---|---|
| Sonuç yazılmamış/yalnız yarım, boş/geçersiz `rowVersion`, yetersiz gerekçe, `Requested`'a yorum | `LabResults.Validation` | 400 |
| Kayıt iptal edilmiş | `LabResults.AlreadyCancelled` | 409 |
| Eşzamanlı güncelleme | `LabResults.ConcurrencyConflict` | 409 |
| Geliş yok / kiracıya ait değil / iptal | `Visits.NotFound` | 404 |
| Geliş hayvan/klinik ile uyuşmuyor | `LabResults.VisitPetMismatch`, `LabResults.VisitClinicMismatch` | 400 |
| Geliş ile muayene gelişi çelişiyor | `LabResults.ExaminationVisitMismatch` | 400 |
| Kesinleşmiş muayene kilidi | `Examinations.Finalized` | 409 |
| Diğer (tenant/klinik/hayvan/muayene) | mevcut kodlar, değişmedi | — |

## 6) Veri modeli ve migration

`LabResults` tablosu: `Status` (int, zorunlu; backfill `Resulted`), `RequestedAtUtc` (zorunlu; backfill = `ResultDateUtc`), `RequestedByUserId?`, `VisitId?`, `CancelledAtUtc?`, `CancelledByUserId?`, `CancelReason?` (500), `RowVersion` (rowversion). `ResultDateUtc` ve `ResultText` **nullable** olur. Yeni indeks `(TenantId, Status)`; `(TenantId, ResultDateUtc)` indeksi yerine `(TenantId, RequestedAtUtc)`. `VisitId` FK tanımlanmaz (mevcut desen: yalnız Guid).

## 7) Kullanım etkisi (frontend, bu görevde değiştirilmedi)

- PUT artık `rowVersion` ister ve 200 + `{id,rowVersion}` döner; mevcut düzenleme formu güncellenmeli (aksi halde 400).
- `resultDateUtc/resultText` nullable: liste/detay şablonları null güvenli olmalı.
- Rozetler: `status` ve `awaitingInterpretation`.
