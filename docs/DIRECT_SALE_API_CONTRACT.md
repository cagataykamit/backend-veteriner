# Doğrudan satış API sözleşmesi (muayenesiz ürün satışı)

Kaynak: ürün deposu `decisions/ADR-016-direct-sale.md` (10 karar, 2026-10-14) ve `docs/DIRECT_SALE_DISCOVERY.md`. Bu belge backend davranışının tek doğruluk kaynağıdır; frontend bundan türetilir. Stok kuralları `docs/BATCH_STOCK_API_CONTRACT.md`, ücret/ödeme kuralları `docs/CHARGES_API_CONTRACT.md`, `docs/PAYMENT_ALLOCATION_API_CONTRACT.md`, `docs/FINANCE_CORRECTION_API_CONTRACT.md` ile aynıdır; burada yinelenmez.

**Durum:** sözleşme + kod aynı dalda. Migration dosyası eklenir, paylaşılan/yerel dev veritabanına **uygulanmaz**.

## 1) Model

- **`Sale`** (satış başlığı) + **`SaleLine`** (satır). `Charge` ve `Administration` genişletilmedi.
- Satış komutu mevcut komutlarla **tek veritabanı işleminde** çalışır: satır başına stok `Out` (`CreateStockMovementCommand`), satış başına **tek** `Charge` ("Ürün satışı"), isteğe bağlı/zorunlu `Payment` (`CreatePaymentCommand`, tek ücrete tam tahsis). Herhangi bir adım başarısız olursa hiçbir şey kalmaz (`ITransactionalRollbackOnFailureRequest`).
- Satış tutarı = satır tutarlarının toplamı; ücret = `Quantity 1 × UnitPrice (toplam)`, açıklama `Ürün satışı`. Tutar kuralı tek yerde: `Sale` domain'i (satır tutarı `round(miktar × birimFiyat, 2, AwayFromZero)`).
- Para birimi yalnız `TRY`.
- Stok hareketi kaynağı: `ReferenceType = "SaleLine"`, `ReferenceId = saleLine.Id` (aynı ürün iki satırda gelebilir; kaynak benzersizliği satır kimliğiyle sağlanır).
- Bilgi amaçlı fiş verisi yanıttadır; **resmî belge/fatura/e-belge değildir** (ADR-016 karar 10, INT-007 ayrı iş).

### Durum

`status`: `Active` | `Voided`. Silme yoktur; iptal edilen satış kalır.

## 2) Peşin müşteri

- Her **kiracıya** tek bir sistem müşterisi: `fullName = "Peşin müşteri"`, `isWalkIn = true`. (Müşteri kaydı kiracı kapsamlıdır, klinik alanı yoktur; ADR'deki "her kliniğe" ifadesi bu yüzden kiracı başına tek kayıt olarak uygulandı. Ücret ve ödeme yine klinik kapsamlıdır.)
- Veritabanı: `Clients` üzerinde `IsWalkIn` + filtreli benzersiz indeks `UX_Clients_TenantId_WalkIn` (kiracıda en fazla bir).
- Satışta `clientId` verilmezse (veya peşin müşterinin kimliği verilirse) satış peşin müşteriye yazılır; kayıt yoksa aynı işlemde oluşturulur. Oluşturma yarışını kaybeden istek `Sales.ConcurrencyConflict` (409) alır, tekrar denemek güvenlidir.
- Peşin müşteri güncellenemez (`PUT /clients/{id}` → `Clients.WalkInReadOnly`, 400). Müşteri silme ucu yoktur.
- Peşin müşteri satışında **ödeme zorunlu ve tam tutar**: `Sales.WalkInPaymentRequired` / `Sales.WalkInPaymentMustBeFull`.
- `POST /api/v1/sales/walk-in-client`: peşin müşteriyi getirir, yoksa oluşturur (idempotent). `Sales.Create` gerekir. Yanıt `{ clientId, fullName, created }`.
- Frontend notu: müşteri listesinde bu kayıt görünür; `isWalkIn` alanı satış yanıtlarında bulunur. Müşteri listesinden gizleme bu sürümde yapılmadı (açık soru).

## 3) Uçlar

Hepsi `/api/v1/sales` altında. Kiracı JWT'den; klinik, aktif klinik bağlamından veya gövde/sorgudaki `clinicId`'den (bağlam varsa uyuşmak zorunda: `Sales.ClinicContextMismatch`). Klinik atama kuralı diğer yazma uçlarıyla aynıdır.

### 3.1 `POST /api/v1/sales` — satış oluştur (`Sales.Create`)

Gövde:

```json
{
  "clinicId": "guid?",
  "clientId": "guid?",          // null => Peşin müşteri
  "requestId": "guid",          // zorunlu (tekrar koruması)
  "notes": "string? (<=500)",
  "lines": [                     // 1..50 satır
    { "productId": "guid", "quantity": 2.0, "unitPrice": 45.50, "batchId": "guid?" }
  ],
  "payment": {                   // peşin müşteride zorunlu; müşterilide opsiyonel (yoksa veresiye)
    "method": "Cash|Card|Transfer",
    "amount": 91.00,             // opsiyonel; yoksa satış toplamı
    "paidAtUtc": "datetime?",    // yoksa şimdi
    "notes": "string?"
  }
}
```

- `unitPrice` opsiyonel: yoksa `Product.UnitPrice`. Verilirse gerekçesiz düzenlenebilir (ADR karar 6); ayrı indirim alanı yok. `0 < unitPrice ≤ 10.000.000`, en çok 2 ondalık. `quantity`: `0 < q ≤ 100.000`, en çok 3 ondalık (stok birimi cinsinden).
- `batchId` opsiyonel: yoksa FEFO (süresi geçmemiş, SKT'si en yakın, ilk yeterli tek parti; yoksa yeterli partisiz bakiye). Süresi geçmiş partiden **satış yapılamaz**. Miktar tek partiye sığmıyorsa `Sales.InsufficientStock` (mesaj kullanılabilir/süresi geçmiş miktarı söyler); kullanıcı iki satır girer.
- Aşı (`Kind = Vaccine`) ve narkotik/psikotropik (`ControlledClass != None`) ürün satılamaz.
- `payment.amount` verilirse `0 < amount ≤ satış toplamı`. Müşterili satışta toplamdan azsa kalan veresiye olarak müşteri bakiyesinde açık kalır; fazlası `Sales.PaymentExceedsTotal`.
- Yanıt `201` `{ created: true, sale: SaleDto, stocks: [StockDto] }`. Aynı `requestId` + aynı içerik ile tekrar: `200` ve `created: false` (ilk satış; stok, ücret, ödeme tekrar yazılmaz). `stocks`, satıştaki her ürün için güncel stok bilgisidir (`productId, quantityOnHand, minimumStockLevel, isBelowMinimum, availableQuantity, expiredQuantity`); düşük stok yalnız bildirilir, satışı engellemez.

### 3.2 `POST /api/v1/sales/{id}/void` — iptal (`Sales.Correct`)

```json
{ "reason": "5-500 karakter, zorunlu", "stockOutcome": "StockReturned|WrittenOff" }
```

- `stockOutcome` varsayılan `StockReturned` (her satırın `Out` hareketi **ters hareketle aynı partiye iade** edilir). `WrittenOff`: zarar/imha, stok artmaz (ADR-013 çizgisi).
- Yalnız **tam iptal**; kısmi iade ertelendi.
- Sıra ve atomiklik: satış iptali → satışın kendi ödemesi iptal (tahsisleri geri alınır; "hiç alınmamış sayılır") → ücret iptali → stok iadesi. Tek işlem; biri başarısız olursa hiçbiri uygulanmaz.
- Satış ücretine sonradan başka bir ödeme tahsis edilmişse (veresiye satış sonradan ödendiyse) ücret iptal edilemez: `Charges.VoidBlockedByAllocation` (409); önce o ödemeler iptal edilir.
- Yanıt `200` `SaleDto`. Zaten iptalse `Sales.AlreadyVoided` (409). Eşzamanlı iki iptalde kaybeden `Sales.ConcurrencyConflict` veya `Sales.AlreadyVoided` (409).

### 3.3 `GET /api/v1/sales/{id}` — detay (`Sales.Read`)

`SaleDto` (satırlarla). Klinik kapsamı dışındaki satır `404 Sales.NotFound`.

### 3.4 `GET /api/v1/sales` — liste (`Sales.Read`)

Sorgu: `page` (1), `pageSize` (20, en çok 100), `clinicId` (veya aktif klinik bağlamı; kapsamsız liste yok: `Sales.ClinicScopeRequired`), `clientId`, `productId`, `fromUtc`, `toUtc` (satış zamanı, `from ≤ x < to`), `includeVoided` (varsayılan `false`). Sıralama: satış zamanı azalan, sonra `id`. Yanıt `PagedResult<SaleDto>`.

### 3.5 `POST /api/v1/sales/walk-in-client` — peşin müşteri (`Sales.Create`)

Bkz. bölüm 2.

## 4) DTO

`SaleDto`: `id, clinicId, clientId, clientName, isWalkIn, status, soldAtUtc, totalAmount, currency, notes, chargeId, paymentId, paidAmount, outstandingAmount, createdByUserId, createdAtUtc, requestId, voidedAtUtc, voidedByUserId, voidReason, voidStockOutcome, lines[]`.

`SaleLineDto`: `id, productId, productName, unit, quantity, unitPrice, lineAmount, batchId, lotNumber, expiryDate`. Ürün adı ve birim satış anında satıra kopyalanır (fiş için); lot/SKT parti kaydından okunur.

`paidAmount`/`outstandingAmount` ücretin güncel tahsis durumundan gelir (iptalde `0`/`0`).

## 5) İzinler

| İzin | Anlam | Roller (varsayılan) |
|---|---|---|
| `Sales.Read` | Satış listesi/detayı | Admin, Owner, ClinicAdmin, Sekreter |
| `Sales.Create` | Satış oluşturma, peşin müşteri ucu | Admin, Owner, ClinicAdmin, Sekreter |
| `Sales.Correct` | Satış iptali | Admin, Owner, ClinicAdmin |

Veteriner şimdilik satış yapmaz (ADR karar 9). Satış komutu içindeki ücret/ödeme/stok adımları controller izni gerektirmez: sekreter `Sales.Create` ile satış yapar, ayrıca `Charges.Create`/`Payments.Create`/`StockMovements.Create` gerekmez. Yeni izinler **seed** gerektirir: `DbMigrator seed` kullanıcı tarafından çalıştırılır (farklı dallardan seed edilirse izinler birbirini siler; bkz. ajan kuyruğu Aşama 3 uyarısı).

## 6) Hata kodları

| Kod | HTTP | Ne zaman |
|---|---|---|
| `Sales.Validation` | 400 | Alan/kural hatası (satır yok, miktar/fiyat sınırları, not uzunluğu, gerekçe 5–500) |
| `Sales.RequestIdRequired` | 400 | `requestId` boş |
| `Sales.ClinicContextMismatch` / `Sales.ClinicScopeRequired` | 400 | Klinik bağlamı uyuşmuyor / yok |
| `Sales.ProductInactive` | 400 | Pasif ürün satılamaz |
| `Sales.VaccineNotSellable` | 400 | Aşı ürünü satılamaz (yalnız hekim uygulamasıyla) |
| `Sales.ControlledSubstanceNotSellable` | 400 | Narkotik/psikotropik ürün satılamaz |
| `Sales.InsufficientStock` | 400 | Yetersiz kullanılabilir bakiye veya tek partiye sığmıyor |
| `Sales.BatchExpired` | 400 | Seçilen/uygun parti SKT geçmiş |
| `Sales.WalkInPaymentRequired` | 400 | Peşin müşteri satışında `payment` yok |
| `Sales.WalkInPaymentMustBeFull` | 400 | Peşin müşteri ödemesi toplamdan farklı |
| `Sales.PaymentExceedsTotal` | 400 | Ödeme satış toplamını aşıyor |
| `Sales.NotFound` | 404 | Satış yok / kapsam dışı |
| `Products.NotFound`, `Clients.NotFound`, `StockBatches.NotFound` | 404 | Ürün/müşteri/parti yok veya kiracıya ait değil |
| `Sales.DuplicateRequestId` | 409 | Aynı `requestId` farklı içerikle |
| `Sales.RequestIdVoided` | 409 | `requestId`'li satış iptal edilmiş; yeni `requestId` gerekir |
| `Sales.AlreadyVoided` | 409 | Satış zaten iptal |
| `Sales.ConcurrencyConflict` | 409 | Eşzamanlı güncelleme; yeniden oku ve tekrarla |
| `StockMovements.ConcurrencyConflict` | 409 | İki kasada aynı stok; tekrar denemek güvenlidir |
| `Charges.VoidBlockedByAllocation` | 409 | İptalde ücrete başka ödeme tahsisli |
| `Clients.WalkInReadOnly` | 400 | Peşin müşteri güncellenemez |
| `Auth.*`, `Tenants.*` | 401/403/404 | Mevcut kurallar |

İç adımların hataları (ödeme, ücret) kodlarını korur (`Payments.*`, `Charges.*`).

## 7) Tekrar koruması, eşzamanlılık, audit

- `requestId` zorunlu; kiracıda benzersiz (`UX_Sales_TenantId_RequestId`). Yarışı kaybeden istek hiçbir şey yazmadan kazananı döner. İçerik özeti (`requestHash`: klinik, müşteri, satırlar [ürün, miktar, fiyat, parti isteği], ödeme yöntemi/tutarı) farklıysa `Sales.DuplicateRequestId`.
- Ücret ve ödeme isteklerinin `requestId`'si satışın `id`'sidir (türetilmiş, ayrı anahtar gerekmez).
- `Sale.RowVersion` eşzamanlılık belirtecidir; iptal yarışında tek kazanan olur. Stok yarışı mevcut parti/satır belirteçleriyle yakalanır (iki kasada son ürün: biri `409` veya `Sales.InsufficientStock` alır, hiçbir kayıt yarım kalmaz).
- Audit: `Sale.Create`, `Sale.Void` (`IAuditableRequest`) + iç komutların denetimleri.

## 8) Migration planı

Migration adı: `AddDirectSale`. Değişenler: yeni `Sales`, `SaleLines` tabloları (+ kiracı/klinik/tarih ve müşteri indeksleri, `UX_Sales_TenantId_RequestId`); `Clients.IsWalkIn` (varsayılan `0`) + `UX_Clients_TenantId_WalkIn`. Mevcut veri değişmez. Yerel dev/paylaşılan veritabanına uygulama kullanıcı onayıyla.

## 9) Frontend için değişenler

- Yeni uçlar bölüm 3; yeni izinler `Sales.Read/Create/Correct` (menü/CTA görünürlüğü buna bağlı).
- Satış ekranı: sepet satırı = ürün + miktar + birim fiyat (varsayılan ürün fiyatı) + opsiyonel parti; parti seçilmezse FEFO uygulanır (öneri göstermek için mevcut parti uçları kullanılır). Miktar tek partiye sığmıyorsa kullanıcı iki satır girer.
- Müşteri seçilmezse peşin müşteri; ödeme bloğu zorunlu ve tutar toplama eşit. Müşterili satışta ödeme opsiyonel (veresiye), kısmi ödeme mümkün.
- Aşı ve kontrollü ürünler satış ürün aramasında seçilebilse bile `Sales.VaccineNotSellable` / `Sales.ControlledSubstanceNotSellable` döner; ürün listesinde `kind`/`controlledClass` ile önceden süzülmesi önerilir.
- `requestId` her sepet gönderiminde bir kez üretilir; ağ hatasında aynı `requestId` ile yeniden gönderilir (`200 created:false` başarı sayılır).
- İptal diyaloğu: gerekçe (5–500) + "Stoğa dön / Zarar yaz" seçimi; `409 Charges.VoidBlockedByAllocation` mesajı gösterilir.
- Fiş: `SaleDto` bilgi amaçlıdır, "resmî belge değildir" notu taşır.
- Müşteri listesinde "Peşin müşteri" (`isWalkIn`) görünür; düzenleme ucu `Clients.WalkInReadOnly` döner.
- Defterler: aşı ve narkotik satılamadığı için Aşı Bilgi/narkotik sarf defterleri etkilenmez.

## 10) Kapsam dışı / ertelenen

Kısmi iade, indirim alanı/yetkisi, aşı ve narkotik satışı, fatura/e-belge/yazar kasa, KDV, bölünmüş parti düşümü, veresiye raporunda satış ayrımı, satış raporu/projeksiyonu (Query DB), kasa kaydı.
