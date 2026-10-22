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
| dolu dizi | Tüm satırları bu liste ile **değiştir**. Satır `id`'leri her değiştirmede yeniden üretilir; istemci satır kimliğine güvenmemelidir. |

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
Kesinleşmiş muayenede reçete değişmez. D3'ün "gerekçeli iptal + yeni kayıt" düzeltmesi reçete için **bu dilimde yoktur** (reçetede durum/iptal kavramı bulunmuyor); ayrı karar/iş olarak açıktır.

---

## 7) Veri modeli ve migration

Yeni tablo `PrescriptionItems`: `Id` (istemci üretimli guid, `ValueGeneratedNever`), `PrescriptionId` (FK → `Prescriptions`, cascade), `SortOrder`, `Name` (200), `ProductId?` (FK → `Products`, restrict), `Instructions?` (1000), `Quantity?` (decimal 18,3). `Prescriptions` tablosu **değişmez**; mevcut kayıtlar için backfill yok.
