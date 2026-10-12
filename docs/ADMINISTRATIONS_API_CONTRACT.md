# Uygulama (administration) kaydı API sözleşmesi — Aşama 5 Dilim 2

Backend tek doğruluk kaynağıdır. Kaynak: `docs/ASAMA5_DISCOVERY.md` (D1, D3, D6, D7, D8, D9 kabul edildi; K3, K4, R1, R2, R4), `docs/STOCK_MOVEMENT_SAFETY_API_CONTRACT.md` (requestId, kaynak benzersizliği, ters hareket). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme. Kapsam: klinikte hastaya fiilen uygulanan ürünün kaydı, stok düşümü ve iptali. **Kapsam dışı:** reçete satırları (Dilim 3; reçete hiçbir zaman stok değiştirmez), ücret önerisi (Dilim 5), aşıda stok tüketimi (D6), lot/son kullanma ve birim dönüşümü (D4), düşük stok bildirimi/e-posta.

---

## 1) Kavramlar

- **Uygulama:** hekimin (veya yetkili kullanıcının) bir hastaya klinikte fiilen uyguladığı **katalog ürünü + miktar**. Hastaya yazılan reçeteden (dış temin) ayrıdır; reçete stok düşmez.
- **Stok düşümü aynı transaction'dadır:** uygulama kaydı yazılırken ürün stoğu `Out` hareketiyle düşer (Dilim 1 uç kuralları aynen geçerli: klinik bazlı stok satırı, pasif ürün reddi, `rowversion` çakışması). Biri başarısız olursa ikisi de yazılmaz.
- **Kaynak izi:** `Out` hareketinin `referenceType = "Administration"`, `referenceId = uygulama kimliği`dir. Aynı uygulama için ikinci `Out` benzersiz kuralla engellenir.
- **İptal (void):** uygulama silinmez; gerekçeli iptal edilir ve stok, Dilim 1 ters hareketiyle (`Reversal`) geri verilir. Bir uygulama en fazla bir kez iptal edilir.
- **Düzenleme yoktur:** miktar/ürün yanlışsa iptal + yeni kayıt (D3). Notlar da değişmez.
- **Yetersiz stok engeller (D7):** uygulama kaydedilmez; stok negatife inmez.
- **Düşük stok yalnız bilgi (D9):** düşümden sonra eldeki miktar `minimumStockLevel`'in altındaysa yanıt bunu bildirir; kayıt engellenmez.
- **Miktar birimi = ürünün stok birimi (D4):** dönüşüm yoktur. Örnek: ürün birimi "şişe" ise `quantity: 0.5` yarım şişedir; ml girmek yanlış düşüm yapar. İstemci `unit` değerini miktar alanının yanında göstermelidir.

### `AdministrationDto`

| Alan | Tip | Not |
|---|---|---|
| `id` | guid | |
| `clinicId` | guid | |
| `petId` | guid | |
| `examinationId` | guid? | Verilmişse muayene; bkz. bölüm 3 kural 4 |
| `treatmentId` | guid? | Opsiyonel tedavi bağı |
| `productId` | guid | |
| `productName` | string | Ürünün **mevcut** adı |
| `productSku` | string? | |
| `unit` | string | Ürünün stok birimi (`Product.Unit`) |
| `quantity` | decimal | > 0, en çok 3 ondalık, en çok 100000 |
| `administeredAtUtc` | datetime | Uygulama zamanı |
| `notes` | string? | ≤ 2000 |
| `status` | string | `Active` \| `Voided` |
| `voidedAtUtc` | datetime? | Yalnız `Voided` |
| `voidedByUserId` | guid? | Yalnız `Voided` |
| `voidReason` | string? | Yalnız `Voided` |
| `createdByUserId` | guid | Uygulamayı kaydeden (klinik uygulayıcı) |
| `createdAtUtc` | datetime | |
| `requestId` | guid? | Verilmişse kaydedilen değer |

### `AdministrationStockDto` (yalnız oluşturma yanıtında)

| Alan | Tip | Not |
|---|---|---|
| `quantityOnHand` | decimal | Düşümden **sonraki** eldeki miktar |
| `minimumStockLevel` | decimal | |
| `isBelowMinimum` | bool | `quantityOnHand < minimumStockLevel` (stok listesiyle aynı tanım) |

---

## 2) Yetkiler (D8)

Yeni izinler eklenir: seeder ile gelir; **mevcut özel roller elle almalı** ve izin seed'i yalnız birleşik `main`'den çalıştırılır.

| İzin | Açıklama | Roller |
|---|---|---|
| `Administrations.Read` | Listeleme/detay | Admin, Owner, ClinicAdmin, Veteriner, Sekreter |
| `Administrations.Create` | Uygulama kaydı (stok düşer) | Admin, Owner, ClinicAdmin, Veteriner |
| `Administrations.Correct` | Uygulamayı gerekçeyle iptal (stok geri verilir) | Admin, Owner, ClinicAdmin, Veteriner |

Manuel stok girişi/çıkışı/düzeltmesi (`StockMovements.Create`) değişmez ve uygulamadan bağımsızdır. Uygulama ucu, `StockMovements.Create` izni olmayan Veteriner için de stok düşer (yetki uygulama iznindedir). Sekreter yazamaz.

---

## 3) `POST /api/v1/administrations`

`Administrations.Create`. İstek:

```json
{
  "clinicId": "guid?",
  "petId": "guid",
  "examinationId": "guid?",
  "treatmentId": "guid?",
  "productId": "guid",
  "quantity": 1.5,
  "administeredAtUtc": "datetime?",
  "notes": "string?",
  "requestId": "guid?"
}
```

Yanıt: yeni kayıt **201** + `{ "created": true, "administration": AdministrationDto, "stock": AdministrationStockDto }`. Aynı `requestId` ile tekrar istek **200** ve `{ "created": false, "administration": <ilk kayıt>, "stock": <güncel stok> }`; stok değişmez.

Kurallar:

1. **Klinik:** `clinicId` yoksa aktif klinik bağlamı kullanılır; ikisi de yoksa `Administrations.Validation` (400). Bağlamla uyuşmazlık `Administrations.ClinicContextMismatch` (400); atanmamış klinik `Clinics.AccessDenied` (403). Pasif kiracı `Tenants.TenantInactive`.
2. **Tekrar (idempotency):** `requestId` kiracıda daha önce kullanılmışsa ilk kayıt döner (200). Önceki kayıt farklı klinik/hasta/ürün/miktar için ise `Administrations.DuplicateRequestId` (409). İptal edilmiş uygulamanın `requestId`'si ile tekrar istek `Administrations.RequestIdVoided` (409; yeni `requestId` kullanılmalı).
3. **Hasta/ürün/klinik:** hasta kiracıya ait olmalı (`Pets.NotFound`); ürün kiracıya ait ve aktif olmalı (`Products.NotFound`, `Products.Inactive`).
4. **Muayene/tedavi bağı:**
   - `treatmentId` verilirse tedavi kiracıya ait, aynı klinik ve aynı hasta olmalı (`Treatments.NotFound`, `Administrations.TreatmentClinicMismatch`, `Administrations.TreatmentPetMismatch`).
   - `examinationId` verilirse muayene aynı klinik ve aynı hasta olmalı (`Examinations.NotFound`, `Administrations.ExaminationClinicMismatch`, `Administrations.ExaminationPetMismatch`).
   - Tedavi bir muayeneye bağlıysa ve istekte `examinationId` yoksa uygulama o muayeneye bağlanır; ikisi de varsa aynı olmalı (`Administrations.TreatmentExaminationMismatch`).
5. **Kesinleşmiş muayene kilidi (Dilim 0):** bağlanan (etkin) muayene kesinleşmişse `Examinations.Finalized` (409); kayıt ve stok değişmez.
6. **Zaman:** `administeredAtUtc` verilmezse şimdi. Tedavi tarihi penceresiyle aynı kural: en çok 7 gün geriye, en çok 2 yıl ileriye (tedavi ile ortak kural: `Treatments.DateTooFarInPast`, `Treatments.DateTooFarInFuture`, 400). Stok hareketinin `occurredAtUtc` değeri budur.
7. **Miktar:** `quantity` 0'dan büyük, en çok 3 ondalık, en çok 100000 (`Administrations.Validation`, 400).
8. **Stok (D7):** klinikte yeterli eldeki miktar yoksa (stok satırı olmaması dâhil) `Administrations.InsufficientStock` (400); hiçbir şey yazılmaz.
9. Stok satırı eşzamanlı değişirse `StockMovements.ConcurrencyConflict` (409); **aynı gövde ve aynı `requestId` ile yeniden denemek güvenlidir.**

Etki: `Out` hareketi (`quantity`, `reason: "Uygulama"`, `referenceType: "Administration"`, `referenceId: id`) ve uygulama kaydı tek transaction'da yazılır.

---

## 4) `POST /api/v1/administrations/{id}/void`

`Administrations.Correct`. İstek: `{ "reason": "string" }` — gerekçe zorunlu, 5–500 karakter (`CorrectionReason`). Yanıt **200** + güncel `AdministrationDto` (`status: "Voided"`).

Etki (tek transaction): uygulama iptal edilir; `Out` hareketi Dilim 1 ters hareketiyle geri alınır (`Reversal`, `reversesMovementId` = uygulamanın `Out` hareketi (kaynak izinden bulunur), aynı kaynak izi, stok miktar kadar **artar**).

Kurallar:

- Uygulama kiracıya ait olmalı; yoksa `Administrations.NotFound` (404). Klinik kapsamı: aktif klinik bağlamı varsa uygulamanın kliniğiyle uyuşmalı (yoksa `Administrations.NotFound`), atanmamış klinik `Clinics.AccessDenied` (403).
- Zaten iptal edilmişse `Administrations.AlreadyVoided` (409); eşzamanlı iki iptal isteğinde kaybedene aynı kod döner.
- **Kesinleşmiş muayene iptali engellemez (D3):** kesinleşmiş muayeneye bağlı uygulama iptal edilebilir (yetkili, gerekçeli); kilit yalnız yeni kayıt eklemeyi engeller. Kesinleşmiş muayeneye *yeni* uygulama eklenemediği için "iptal + yeni kayıt" düzeltmesinde yeni kaydın bu muayeneye bağlanamayacağı **açık ürün sorusudur** (bölüm 8).
- Ürün sonradan pasif yapılmışsa ters hareket Dilim 1 kuralıyla `Products.Inactive` (400) ile reddedilir (bölüm 8).
- Stok satırı eşzamanlı değişirse `StockMovements.ConcurrencyConflict` (409); yeniden deneme güvenlidir.
- **Ücret ilişkisi (D5):** iptal ücreti değiştirmez ve ücret iptali stok vermez. Ücret önerisi Dilim 5'tedir; bu dilimde ücret bağı yoktur.

---

## 5) Okuma

| Uç | İzin | Not |
|---|---|---|
| `GET /administrations/{id}` | `Administrations.Read` | `AdministrationDto`; yoksa/kapsam dışıysa `Administrations.NotFound` |
| `GET /administrations` | `Administrations.Read` | Sayfalı. Klinik kapsamı zorunlu: `clinicId` veya aktif klinik bağlamı (`Administrations.ClinicScopeRequired`, 400). Süzgeçler: `petId`, `examinationId`, `treatmentId`, `productId`, `includeVoided` (varsayılan `false`: iptaller gizli). Sıralama: `administeredAtUtc` azalan |

Liste/detay stok bilgisi taşımaz (`stock` yalnız oluşturma yanıtındadır).

---

## 6) Hata kodları

| Kod | HTTP |
|---|---|
| `Administrations.DuplicateRequestId`, `Administrations.RequestIdVoided`, `Administrations.AlreadyVoided`, `Examinations.Finalized`, `StockMovements.ConcurrencyConflict` | 409 |
| `Administrations.Validation`, `Administrations.InsufficientStock`, `Administrations.ClinicContextMismatch`, `Administrations.ClinicScopeRequired`, `Administrations.TreatmentClinicMismatch`, `Administrations.TreatmentPetMismatch`, `Administrations.ExaminationClinicMismatch`, `Administrations.ExaminationPetMismatch`, `Administrations.TreatmentExaminationMismatch`, `Treatments.DateTooFarInPast`, `Treatments.DateTooFarInFuture`, `Products.Inactive` | 400 |
| `Administrations.NotFound`, `Administrations.StockMovementNotFound`, `Pets.NotFound`, `Products.NotFound`, `Treatments.NotFound`, `Examinations.NotFound` | 404 |
| `Clinics.AccessDenied`, `Tenants.TenantInactive` | 403 |

---

## 7) Uygulama notları

- Veri modeli: yeni `Administrations` tablosu. Benzersiz kural: `(TenantId, RequestId)` filtreli benzersiz indeks (`RequestId IS NOT NULL`); düşümün hareket kimliği saklanmaz, `Out` hareketi kaynak izinden bulunur (`Administrations.StockMovementNotFound`, 404: veri bozukluğu). İptal alanları tutarlılık CHECK'i (`VoidedAtUtc`, `VoidedByUserId`, `VoidReason` ya hep dolu ya hep boş). Okuma yardımı: `(TenantId, PetId)`, `(TenantId, ExaminationId)`, `(TenantId, TreatmentId)`, `(TenantId, ClinicId, AdministeredAtUtc)` indeksleri. Silme ucu yoktur.
- Komutlar `ITransactionalRollbackOnFailureRequest`tir. Yazma sırası: önce uygulama kaydı (`requestId` benzersizliği hiçbir şey yazılmadan eler), sonra aynı transaction'da mevcut `CreateStockMovementCommand` / `ReverseStockMovementCommand` gönderilir; stok tarafı başarısız sonuç dönerse işlem geri alınır ve uygulama kaydı kalmaz. Stok kuralları tek yerde (Dilim 1 handler'ları) yaşar, burada kopyalanmaz.
- Reçete ve tedavi komutları stok değiştirmez (K3); bu dilimde de değişmez. Uygulama yalnız bu ucun yan etkisidir.
- Tedavi/muayene/aşı yazma uçlarına dokunulmaz; Dilim 3 (`PrescriptionItem`) `Prescription` alanındadır ve bu dilimle kesişmez. Ortak dosyalar (izin kataloğu, `AppDbContext`, model snapshot) iki dilimde de değişir; migration birleştirmeden önce güncel `main` üzerinde yeniden üretilir.

---

## 8) Açık noktalar (ürün kararı bekler, bu dilimde varsayım yapılmadı)

1. **Kesinleşmiş muayeneye unutulan ilaç:** D3'e göre kesinleşmiş muayeneye yeni kayıt eklenemez; ek not (addendum) akışı uygulama için tanımlı değildir. Bu dilimde reddedilir (`Examinations.Finalized`).
2. **Pasif ürünün uygulamasını iptal:** ürün sonradan pasif yapılırsa uygulama iptali (stok iadesi) `Products.Inactive` ile engellenir; gerçek ihtiyaç doğarsa iptal için pasif ürün istisnası ayrı karar olmalıdır.
3. **Aşı (D6):** aşı uygulaması stok düşmez; `VaccineDefinition` ile `Product` bağı sonraki karar.
