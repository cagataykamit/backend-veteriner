# Stok hareketi güvenliği API sözleşmesi — Aşama 5 Dilim 1

Backend tek doğruluk kaynağıdır. Kaynak: `docs/ASAMA5_DISCOVERY.md` (R2 çift tüketim, R3 eşzamanlılık, R4 geri alma; D1–D9 kabul edildi). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme. Kapsam: mevcut `POST /stock-movements` için tekrar koruması, kaynak benzersizliği, eşzamanlı ilk giriş düzeltmesi ve yeni `POST /stock-movements/{id}/reverse`. **Kapsam dışı:** Uygulama (administration) kaydı, reçete satırı, tedavi/reçete stok bağı, ücret önerisi, yeni dar izin (Dilim 2+). **Parti (lot) bazlı stok (ADR-015)** bu sözleşmeyi genişletir: giriş parti açar, çıkış/ters hareket partiye bağlanır; bkz. `BATCH_STOCK_API_CONTRACT.md`.

---

## 1) Kavramlar

- **Stok defteri append-only** kalır: hareket değiştirilmez/silinmez. Hata düzeltmesi ya `Adjustment` (sayım) ya da yeni **`Reversal`** hareketidir.
- **`requestId`** (opsiyonel guid): aynı istemci isteğinin tekrarı (çift tıklama, ağ yeniden denemesi) stoğu ikinci kez değiştirmez. Kiracı içinde benzersizdir.
- **Kaynak (`referenceType` + `referenceId`)**: hareketin hangi işlemden doğduğunu bağlar (ör. ileride uygulama kaydı). Aynı kaynak, aynı klinik+ürün+hareket tipi için **en fazla bir** hareket üretir.
- **Ters hareket (`Reversal`)**: tek bir önceki hareketin etkisini geri alır. Her hareket en fazla bir kez geri alınır; ters hareket tekrar geri alınamaz.
- Yeni `StockMovementType.Reversal = 4` (sayısal enum; mevcut değerler değişmez).

### `StockMovementDto` değişikliği (geriye uyumlu, iki alan eklenir)

| Alan | Tip | Not |
|---|---|---|
| `requestId` | guid? | Verilmişse kaydedilen değer |
| `reversesMovementId` | guid? | Yalnızca `Reversal` hareketlerinde dolu: geri alınan hareket |

Diğer alanlar aynıdır. Bir hareketin geri alınıp alınmadığı bu dilimde DTO'da **yoktur** (liste `reversesMovementId` ile türetilebilir).

---

## 2) Yetkiler

Yeni izin **`StockMovements.Correct`** (kullanıcı kararı 2026-10-13, güvenlik raporu D2): geri alma artık bu izinle yapılır; migration yoktur, izin `DbMigrator seed` ile gelir. Özel roller elle almalıdır.

| İşlem | Policy | Roller |
|---|---|---|
| Hareket ekleme | `StockMovements.Create` | Admin, Owner, ClinicAdmin, Sekreter |
| Hareketi geri alma | `StockMovements.Correct` | **Yalnız yönetici düzeyi:** Admin, Owner, ClinicAdmin (ADR-012). Sekreter hareket ekler ama geri alamaz; Veteriner'de ikisi de yok. |
| Okuma | `StockMovements.Read` | Değişmedi |

**Uygulama iptali bu izne takılmaz.** `POST /administrations/{id}/void` (`Administrations.Correct`) stok iadesini iç komutla (`ReverseStockMovementCommand`) HTTP policy'si olmadan yazar; yetki ucun izninde aranır, ters hareket için ayrıca `StockMovements.Correct` istenmez. HTTP ucu `POST /stock-movements/{id}/reverse` yalnız `StockMovements.Correct` ile açılır.

D8 (dar izin: `Treatments.Administer` / `ProductUsage.Create` ve uygulama iptali için `...Correct`) **Uygulama kaydı (Dilim 2)** içindir; bu dilimde tanımlanmaz.

---

## 3) `POST /api/v1/stock-movements`

İstek gövdesine `requestId` eklenir (diğer alanlar değişmez):

```json
{
  "clinicId": "guid",
  "productId": "guid",
  "movementType": 2,
  "quantity": 3,
  "unitCost": null,
  "reason": "string?",
  "referenceType": "string?",
  "referenceId": "guid?",
  "occurredAtUtc": "datetime?",
  "notes": "string?",
  "requestId": "guid?"
}
```

Yanıt: yeni hareket **201** + `StockMovementDto` (gövde değişmedi). Aynı `requestId` ile tekrar istek **200** ve ilk hareketi döndürür; stok değişmez. `requestId` verilmezse tekrar koruması yoktur.

Yeni kurallar:

1. **Tekrar (idempotency):** `requestId` kiracıda daha önce kullanılmışsa ilk hareket döner (200). Önceki hareket farklı klinik/ürün/hareket tipinde ise veya bir `Reversal` ise `StockMovements.DuplicateRequestId` (409). Karşılaştırma miktarı kapsamaz (`Adjustment` hedef miktarı hareket miktarından türemez).
2. **Kaynak bütünlüğü:** `referenceType` ve `referenceId` ya ikisi birlikte verilir ya da ikisi de boş olur; yarım kaynak → 400. Aynı (kiracı, klinik, ürün, `referenceType`, `referenceId`, `movementType`) için ikinci hareket `StockMovements.DuplicateReference` (409). Kaynaksız hareketler (manuel giriş/çıkış) etkilenmez. `requestId` tekrarı kaynak denetiminden önce değerlendirilir (aynı istek tekrarı 409 değil 200 döner).
3. **`movementType: 4 (Reversal)` bu uçla gönderilemez** (400); ters hareket yalnız bölüm 4'teki uçla oluşur.
4. **Stok satırı yokken eşzamanlı ilk giriş:** iki istek aynı anda `Initial`/`In` ile satırı oluşturmaya çalışırsa kaybeden istek 500 yerine `StockMovements.ConcurrencyConflict` (409) döner; **aynı gövdeyle (aynı `requestId` ile) yeniden denemek güvenlidir** (yeniden denenince satır vardır ve normal akış çalışır). Satır vardığında eşzamanlı güncelleme mevcut `rowversion` ile aynı koda düşer.

---

## 4) `POST /api/v1/stock-movements/{id}/reverse`

`StockMovements.Correct` (yetkisiz → 403). İstek:

```json
{
  "reason": "Yanlış ürün seçildi",
  "notes": "string?",
  "requestId": "guid?"
}
```

`reason` zorunludur (1–500 karakter); `notes` ≤ 4000. Yanıt: yeni `Reversal` hareketi **201** + `StockMovementDto`; aynı `requestId` ile tekrar **200** ve ilk ters hareket.

Etki (stok tek transaction'da değişir, defter hareketi aynı işlemde yazılır):

| Geri alınan hareket | Ters hareketin stok etkisi |
|---|---|
| `Out` | Miktar kadar **artar** |
| `In`, `Initial` | Miktar kadar **azalır**; eldeki miktar yetersizse `StockMovements.InsufficientStock` |
| `Adjustment`, `Reversal` | **Geri alınamaz** (`StockMovements.NotReversible`, 400). Adjustment yönü saklanmaz ve sayım yeniden yapılarak düzeltilir (yeni `Adjustment`). |

Ters hareket: aynı klinik/ürün/miktar/`unitCost`, `movementType: 4`, `reversesMovementId = {id}`, geri alınan hareketin `referenceType/referenceId` değerlerini taşır (kaynak üzerinden izlenebilir), `reason` istekten gelir, `createdByUserId` isteği yapan kullanıcıdır. Kaynak kopyalanması benzersizlik kuralını bozmaz (tip `Reversal` ayrıdır).

Kurallar:

- Hareket kiracıya ait olmalı; yoksa `StockMovements.NotFound` (404). Klinik kapsamı mevcut kuralla: aktif klinik bağlamı varsa hareketin kliniğiyle uyuşmalı (`StockMovements.ClinicContextMismatch`), atanmamış klinik `Clinics.AccessDenied` (403).
- Geri alma pasif ürüne de yazılır (ADR-013): `Products.Inactive` kuralı yalnız yeni hareket oluşturmada geçerlidir; ters hareket stok iadesini engellemez.
- Zaten geri alınmışsa `StockMovements.AlreadyReversed` (409). Eşzamanlı iki geri alma isteğinde DB benzersiz kuralı kaybedene aynı kodu (409) döner.
- Stok satırı eşzamanlı değişirse `StockMovements.ConcurrencyConflict` (409); yeniden deneme güvenlidir.
- Kimin/neden: `createdByUserId`, `reason`, `reversesMovementId`. (Stok komutları audit pipeline'ına bağlı değildir; bu dilimde eklenmez.)

---

## 5) Hata kodları

| Kod | HTTP |
|---|---|
| `StockMovements.DuplicateRequestId`, `StockMovements.DuplicateReference`, `StockMovements.AlreadyReversed`, `StockMovements.ConcurrencyConflict` | 409 |
| `StockMovements.NotReversible`, `StockMovements.InsufficientStock`, doğrulama hataları (yarım kaynak, `Reversal` tipi, boş `reason`) | 400 |
| `StockMovements.NotFound`, `StockMovements.StockNotFound` (geri alınan `Out` hareketinin stok satırı yok; veri bozukluğu) | 404 |
| `StockMovements.ClinicContextMismatch` | 400 |
| `Clinics.AccessDenied` | 403 |

Mevcut kodlar (`StockMovements.StockAlreadyInitialized`, `AdjustmentUnchanged`, `Products.Inactive`, vb.) değişmedi.

---

## 6) Uygulama notları

- Veri modeli: `StockMovements` tablosuna `RequestId` (nullable guid) ve `ReversesMovementId` (nullable guid) eklenir. Üç filtreli benzersiz indeks: `(TenantId, RequestId)` (`RequestId IS NOT NULL`), `(TenantId, ClinicId, ProductId, ReferenceType, ReferenceId, MovementType)` (`ReferenceType IS NOT NULL AND ReferenceId IS NOT NULL`), `(TenantId, ReversesMovementId)` (`ReversesMovementId IS NOT NULL`). Mevcut satırlar bu alanları doldurmaz; keşifte `ReferenceType/ReferenceId` hiçbir yerde doldurulmuyordu (uygulamadan önce hedef DB'de çift kaynak olmadığı kontrol edilir).
- Yazma sırası: önce hareket (mevcut stok satırı değişimiyle tek `SaveChanges`), yeni stok satırı **hareketten sonra**. Böylece `requestId`/kaynak/geri-alma benzersiz kuralları hiçbir şey yazılmadan eler. Komutlar `ITransactionalRollbackOnFailureRequest`tir: başarısız sonuçta (ör. satır yarışı kaybı) işlem geri alınır, hareket kalmaz. Repository `AddAsync` kaydı hemen kalıcılaştırır; bu yüzden tek `SaveChanges` varsayılmaz.
- Benzersiz ihlali (`DbUpdateException`) SQL hata numarasına bağlı kalmadan, yeniden sorgulayarak sınıflandırılır: hareket yazımında `requestId` kazananı (tekrar, 200) ve kaynak çakışması (409); stok satırı yazımında satırın artık var olması (`ConcurrencyConflict`, 409). Hiçbiri değilse hata olduğu gibi yükselir.
- Reçete/tedavi kayıtları stok hareketi üretmez; bu dilimde bu davranış değişmez (K3, Dilim 2'de testle korunur).
- Okuma modeli (Query DB) yoktur; stok Command DB'dedir.
