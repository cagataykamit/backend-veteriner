# Reçete satırları API sözleşmesi — Aşama 5 Dilim 3

Backend tek doğruluk kaynağıdır. Kaynak: `docs/ASAMA5_DISCOVERY.md` (D2, D3, D4, K2, K3; D1–D9 kabul edildi). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme. Kapsam: mevcut reçeteye **yapılandırılmış satırlar** (`items`). **Kapsam dışı:** Uygulama (administration) kaydı ve stok düşümü (Dilim 2), ücret önerisi, reçete iptali, lot/son kullanma/birim dönüşümü (D4), timeline/özet zenginleştirme (Dilim 4).

---

## 1) Kavramlar

- **Reçete satırı (`PrescriptionItem`)**: reçetenin bir kalemi. **Serbest ad** + **opsiyonel katalog ürün bağı** (D2): dışarıdan temin edilen veya katalogda olmayan ilaç yalnız adla yazılabilir.
- **Reçete stok DÜŞMEZ** (K3). Satır oluşturma, güncelleme ve ürün bağı hiçbir stok hareketi üretmez ve stok okumaz. Stok tüketimi yalnız Uygulama kaydındadır (Dilim 2).
- **Güncelleme (ADR-036 Dilim 2):** satıra isteğe bağlı `dose`, `unit`, `lotNumber` eklendi; ayrıntı `HBS_ASSISTANT_API_CONTRACT.md` bölüm 2. Aşağıdaki D4 notu tarihseldir.
- **Birim yok (D4)**: satırda birim, lot, son kullanma alanı yoktur. `quantity` birimsizdir; katalog ürüne bağlıysa anlamı ürünün stok birimidir, serbest satırda birim `instructions` metninde yazılır (ör. "2 kutu").
- **Geriye uyum**: `title` ve `content` (serbest metin) **olduğu gibi** kalır ve `content` hâlâ zorunludur. Satırı olmayan (eski) reçeteler değişmeden açılır; `items` boş dizi döner. Eski istemciler `items` göndermeden yazmaya devam edebilir.

### Satır alanları

| Alan | Tip | Kural |
|---|---|---|
| `id` | guid | Sunucu üretir; istekte gönderilmez. |
| `sortOrder` | int | Sunucu atar (dizideki sıra, 1'den başlar); istekte gönderilmez. |
| `name` | string | En çok 200. `productId` yoksa zorunlu. `productId` varsa boş bırakılırsa ürün adı kopyalanır. Değer **anlık görüntüdür**: ürün sonradan yeniden adlandırılırsa satır değişmez. |
| `productId` | guid? | Opsiyonel katalog ürünü. Kiracıya ait ve aktif olmalı. |
| `instructions` | string? | Kullanım talimatı, en çok 1000. |
| `quantity` | decimal? | Verilmişse > 0 ve en çok 100000, 3 ondalık. |

Bir reçetede en çok **30** satır. Aynı ürün birden çok satırda geçebilir.

---

## 2) Yetkiler

Yeni izin **yoktur** (seed/özel rol değişikliği gerekmez). Satırlar reçetenin parçasıdır:

| İşlem | Policy |
|---|---|
| Satırlı reçete oluşturma | `Prescriptions.Create` |
| Satırları değiştirme (PUT) | `Prescriptions.Update` |
| Okuma | `Prescriptions.Read` |

---

## 3) `POST /api/v1/prescriptions`

Gövdeye opsiyonel `items` eklenir; diğer alanlar değişmez.

```json
{
  "clinicId": "guid", "petId": "guid", "examinationId": "guid?", "treatmentId": "guid?",
  "prescribedAtUtc": "datetime", "title": "string", "content": "string", "notes": "string?",
  "followUpDateUtc": "datetime?",
  "items": [
    { "name": "Amoksisilin 250 mg", "productId": "guid?", "instructions": "Günde 2 kez, 7 gün", "quantity": 14 }
  ]
}
```

`items` yok / `null` / `[]` → satırsız reçete. Yanıt: 201 + reçete `id` (değişmedi).

## 4) `PUT /api/v1/prescriptions/{id}`

`UpdatePrescriptionBody`'ye opsiyonel `items` eklenir. Yorum (kısmi güncelleme deseni, muayene PUT'u ile aynı):

| `items` | Anlam |
|---|---|
| alan yok / `null` | **Dokunma**: mevcut satırlar korunur. (Eski istemci PUT'u satırları silmez.) |
| `[]` | Tüm satırları sil. |
| dolu dizi | Satır listesi bu liste olur. **RECORD-008 (2026-10-30):** satırda `id` (GET ile dönen) verilirse o satır **yerinde güncellenir, kimliği korunur**; `id` verilmeyen satır yenidir; listede olmayan satır silinir. Reçeteye ait olmayan veya iki kez geçen `id` → `Prescriptions.Validation` (400). |

## 5) `GET /api/v1/prescriptions/{id}`

`PrescriptionDetailDto`'ya eklenir:

```json
"items": [
  { "id": "guid", "sortOrder": 1, "name": "string", "productId": "guid?", "instructions": "string?", "quantity": 14 }
]
```

Liste (`GET /prescriptions`), muayene ilişkili özeti, hayvan geçmiş özeti ve timeline **değişmedi**; satır içermezler (Dilim 4).

---

## 6) Kurallar ve hata kodları

Satır yazan her yol (POST, PUT) mevcut reçete kurallarının tamamından geçer; yeni kilit yolu açılmaz.

| Kural | Kod | HTTP |
|---|---|---|
| Kesinleşmiş muayeneye bağlı reçetede satır ekleme/değiştirme/silme (D3, Dilim 0 kilidi) | `Examinations.Finalized` (mevcut kilit kodu) | 409 |
| Satır `productId` kiracıda yok | `Products.NotFound` | 404 |
| Satır `productId` pasif | `Products.Inactive` | 400 |
| Satır sayısı > 30, `name` boş (ürünsüz), `quantity` ≤ 0 veya sınır dışı, uzunluk aşımı | doğrulama (`Validation`) | 400 |

### Düzeltme yolu (D3)
Kesinleşmiş muayenede mevcut reçete değişmez. Düzeltme yolu **gerekçeli iptal + yeni reçete**dir (RECORD-008, bölüm 8).

---

## 7) Veri modeli ve migration

Yeni tablo `PrescriptionItems`: `Id` (istemci üretimli guid, `ValueGeneratedNever`), `PrescriptionId` (FK → `Prescriptions`, cascade), `SortOrder`, `Name` (200), `ProductId?` (FK → `Products`, restrict), `Instructions?` (1000), `Quantity?` (decimal 18,3). `Prescriptions` tablosu **değişmez**; mevcut kayıtlar için backfill yok.

---

## 8) İptal ve sonradan ekleme (RECORD-008, 2026-10-30)

ADR-013 ilkelerinin reçeteye uyarlanması. Yeni izin yoktur.

### `POST /api/v1/prescriptions/{id}/cancel` — `Prescriptions.Update`

Gövde `{ "reason": "string" }` (kırpılır, 5–500). Yanıt **204**. Reçete **silinmez**; `cancelledAtUtc`, iptal eden ve gerekçe saklanır. Kesinleşmiş muayeneye bağlı reçetede de yapılır (muayene kilidine takılmaz).

| Durum | Kod | HTTP |
|---|---|---|
| Gerekçe yok / 5–500 dışı | `Prescriptions.Validation` | 400 |
| Zaten iptal | `Prescriptions.AlreadyCancelled` | 409 |
| İptal edilmiş reçeteye PUT (alan veya satır) | `Prescriptions.AlreadyCancelled` | 409 |

HBS geri kaydı (`PATCH .../hbs`) iptalden etkilenmez; iptal edilen reçete HBS takip listesine ve "HBS'e girilmedi" sayacına girmez (`hbs.isTracked = false`).

### Sonradan ekleme (kesinleşmiş muayene)

`POST /prescriptions` gövdesine opsiyonel `lateEntryReason` eklendi. Bağlı muayene (doğrudan `examinationId` veya `treatmentId`'nin muayenesi) **kesinleşmişse** reçete artık reddedilmez: `lateEntryReason` zorunludur (yoksa/5–500 dışıysa `Prescriptions.LateEntryReasonRequired`, 400; kayıt yazılmaz) ve reçete `addedAfterFinalization: true` ile yazılır. Muayene taslaksa alan yok sayılır. **Mevcut** reçetenin PUT'u kilitli kalır (`Examinations.Finalized`, 409).

### Okuma alanları

| Uç | Yeni alanlar |
|---|---|
| `GET /prescriptions/{id}` | `status` (`Active` \| `Cancelled`), `cancelledAtUtc`, `cancelledByName`, `cancelReason`, `addedAfterFinalization`, `lateEntryReason` |
| `GET /prescriptions` | Satırda `status`, `addedAfterFinalization`. İptal edilen reçete listede kalır |
| `GET /examinations/{id}/related-summary` | Reçete satırında `isCancelled`, `addedAfterFinalization` |
| Hasta timeline | İptal edilen reçete varsayılan gizli; `includeVoided=true` ile `status = Cancelled`, `isVoided = true` döner (lab iptali ile aynı) |

Kayıt defterleri (muayene/reçete defteri) bu sürümde değişmedi: iptal edilen reçete defterde işaretsiz görünür (açık konu).

### Veri modeli

Migration `AddPrescriptionCancellationAndLateEntry`: `Prescriptions` tablosuna `CancelledAtUtc`, `CancelledByUserId`, `CancelReason` (500), `AddedAfterFinalization` (bit, varsayılan 0), `LateEntryReason` (500); tutarlılık CHECK kısıtları `CK_Prescriptions_Cancel_Consistent`, `CK_Prescriptions_LateEntry_Consistent`. Mevcut satırlar etkin ve normal kalır, backfill yok.
