# Mal kabul (çok satırlı stok girişi) API sözleşmesi — D5

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `ux/HEDEF-IS-AKISLARI-2026-10.md` bölüm 1.7, `decisions/ADR-015` (parti/lot), `docs/BATCH_STOCK_API_CONTRACT.md` (parti kuralları, `acknowledgeExpired`), `docs/STOCK_MOVEMENT_SAFETY_API_CONTRACT.md` (`requestId`, eşzamanlılık). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme. Kapsam: tek faturadan/irsaliyeden gelen N ürünün **tek işlemde (hepsi ya da hiçbiri)** stoğa girişi. **Kapsam dışı:** sipariş / alış faturası / iade / tedarikçi ödemesi, fatura belgesinden otomatik satır önerisi, belge (fatura ↔ girişler) varlığı ve raporu.

## 1) Kavramlar ve kararlar

- **Yeni tablo / migration yoktur.** Her satır, mevcut `POST /stock-movements` (`In`) komutu yeniden kullanılarak bir `StockMovement` + bir `StockBatch` olarak yazılır. Ortak başlık alanları (firma, fatura no/tarihi, geliş, depo çıkış, nakil, soğuk zincir) her satırın partisine kopyalanır (ADR-015 uyumlu; belge varlığı yok). Kural **tek yerde** yaşar: lot+SKT zorunluluğu (aşı/kontrollü), geçmiş SKT onayı, ürün/klinik/aktiflik/stok izleme kontrolleri, alan sınırları, stok ve parti yazımı mevcut giriş komutundadır; mal kabul bunları çoğaltmaz.
- **Atomik:** bir satır bile hata verirse **hiçbir satır yazılmaz** (tek transaction, hata sonucunda geri alınır). Hata hangi satır olduğunu söyler (bölüm 4).
- **Her satır ayrı parti:** aynı ürün birden çok satırda (farklı lot) gelebilir; aynı lot tekrar girilebilir. Her satır bir defter satırıdır.
- **Satır tipi her zaman `In`.**
- **Tek klinik:** bir mal kabul tek kliniğe aittir (`clinicId`).
- **Süresi geçmiş SKT:** **satır başına** açık onay: satırda `acknowledgeExpired: true` yoksa o satır `StockBatches.ExpiredAcknowledgementRequired` ile reddedilir ve hiçbir satır yazılmaz. Üst düzey toplu onay yoktur (bilerek: onay satırın kendisine ait).
- **Satır sınırı:** en fazla **100** satır (`GoodsReceipts.TooManyLines`). Gerekçe: tek fatura/irsaliye için fazlasıyla yeterli; tek transaction süresini ve kilit süresini sınırlı tutar. Tek sabittir (`CreateGoodsReceiptCommand.MaxLines`).
- **Rakip pratiği (iş kapsamı referansı, görünüm değil):** tek fatura/irsaliye başlığı + çok satır; lot/SKT satırda.

## 2) Yetki

**Yeni izin yoktur** (seed gerekmez): `StockMovements.Create` (mevcut stok girişi izni). Klinik kapsamı stok hareketleriyle aynıdır (`clinicId` zorunlu; aktif klinik bağlamıyla uyuşmazlık `StockMovements.ClinicContextMismatch` 400; atanmamış klinik `Clinics.AccessDenied` 403).

## 3) `POST /api/v1/goods-receipts`

İstek:

```json
{
  "clinicId": "guid",
  "requestId": "guid",
  "supplierName": "string?",
  "invoiceNumber": "string?",
  "invoiceDate": "yyyy-MM-dd?",
  "receivedDate": "yyyy-MM-dd?",
  "dispatchedDate": "yyyy-MM-dd?",
  "transportMethod": "string?",
  "coldChainMonitor": "string?",
  "notes": "string?",
  "lines": [
    {
      "productId": "guid",
      "quantity": 10,
      "lotNumber": "string?",
      "expiryDate": "yyyy-MM-dd?",
      "unitCost": 12.5,
      "acknowledgeExpired": false
    }
  ]
}
```

| Alan | Kural |
|---|---|
| `clinicId` | zorunlu |
| `requestId` | **zorunlu** (boş guid olamaz): `GoodsReceipts.RequestIdRequired` (400, doğrulama) |
| başlık alanları | sınırlar `BATCH_STOCK_API_CONTRACT.md` bölüm 4 ile aynıdır (`supplierName` ≤ 200, `invoiceNumber` ≤ 100, `transportMethod`/`coldChainMonitor` ≤ 200); `dispatchedDate`, `receivedDate`'ten sonra olamaz (`StockBatches.Validation`) |
| `notes` | ≤ 4000; her satırın hareketine `notes` olarak yazılır. Hareketin `reason` değeri `Mal kabul`'dür |
| `lines` | zorunlu, 1–100 satır |
| `lines[].productId` | zorunlu |
| `lines[].quantity` | > 0, ≤ `StockMovement.MaxQuantity` |
| `lines[].lotNumber`, `expiryDate` | aşı/kontrollü ürün için zorunlu (`StockBatches.LotAndExpiryRequired`); diğerlerinde opsiyonel. `lotNumber` ≤ 100 |
| `lines[].unitCost` | opsiyonel, ≥ 0 |
| `lines[].acknowledgeExpired` | geçmiş `expiryDate` için `true` gerekir (satır başına) |

Yanıt: **201** (yeni kayıt) veya **200** (aynı `requestId` ile tekrar):

```json
{
  "created": true,
  "clinicId": "guid",
  "lines": [ "StockMovementDto (istek sırasıyla; batchId dolu)" ]
}
```

`lines[i]`, istek satırı `i` ile aynı sıradadır (`StockMovementDto`: ürün adı/sku, klinik adı, `batchId`, `quantity`, `unitCost`, `batchBalanceAfter`). Parti ayrıntısı (lot, SKT, fatura) `GET /stock-batches/{batchId}` ile okunur.

### Idempotency (`requestId`)

- Her satırın hareketi, `requestId` ve satır sırasından **türetilen sabit bir requestId** taşır; böylece mevcut hareket idempotency'si (benzersiz indeks, `STOCK_MOVEMENT_SAFETY_API_CONTRACT.md` bölüm 3) satır başına çalışır.
- Aynı `requestId` ile aynı gövde tekrarlanırsa **200** ve ilk sonuç döner, `created: false`; **çift giriş / çift parti olmaz**. İki eşzamanlı aynı istekte biri yazar, diğeri kazananın sonucunu 200 ile alır.
- Aynı `requestId` ile **farklı içerik** (farklı satır sayısı, ürün veya miktar) → `GoodsReceipts.DuplicateRequestId` (409); hiçbir şey yazılmaz.
- Hata ile sonuçlanan istek hiçbir kayıt bırakmaz; **aynı `requestId` ile düzeltilmiş gövde gönderilebilir**.
- Frontend, formu açtığında bir `requestId` üretmeli ve Kaydet'in tekrarlarında (çift tıklama, ağ yeniden denemesi) aynı değeri göndermelidir. Başarılı kayıttan sonra yeni bir mal kabul için yeni `requestId` üretilir.

## 4) Hata kodları ve satır belirtimi

Doğrulama hataları (`lines[i]` alanları: `quantity ≤ 0`, `unitCost < 0`, boş `productId`, lot uzunluğu; 100'den fazla satır; boş `lines`) standart `ValidationProblemDetails` (400, `code: Validation.FluentValidation`) döner. Satır alanı hataları `errors` anahtarında **satır yolunu** taşır: `lines[2].quantity` (indeks **0 tabanlı**).

İş kuralı hataları (lot+SKT zorunlu, geçmiş SKT onayı, ürün bulunamadı/pasif/stok izlenmiyor, depo çıkış tarihi, eşzamanlılık) mevcut giriş komutunun **aynı kodlarıyla** döner (`BATCH_STOCK_API_CONTRACT.md` bölüm 8) ve `detail` metni `Satır {n}: ` ile başlar (**1 tabanlı**, ör. `Satır 3: Aşı ve kontrollü ürünlerde seri/lot no ve son kullanma tarihi zorunludur.`). Kod değişmediği için mevcut HTTP eşlemesi aynen geçerlidir.

| Kod | HTTP | Not |
|---|---|---|
| `StockBatches.LotAndExpiryRequired` | 400 | satır |
| `StockBatches.ExpiredAcknowledgementRequired` | 400 | satır; o satırda `acknowledgeExpired: true` gerekir |
| `StockBatches.Validation` | 400 | `dispatchedDate` > `receivedDate` |
| `Products.NotFound` | 404 | satır |
| `Products.Inactive`, `Products.NotStockTracked` | 400 | satır |
| `StockMovements.ConcurrencyConflict` | 409 | aynı gövde/`requestId` ile yeniden denenebilir |
| `GoodsReceipts.DuplicateRequestId` | 409 | aynı `requestId`, farklı içerik |
| `StockMovements.ClinicContextMismatch` | 400 | |
| `Clinics.NotFound` / `Clinics.AccessDenied` | 404 / 403 | |

## 5) Düşük stoktan "Giriş" kısayolu

Yeni uç gerekmez. `GET /product-stocks?isBelowMinimum=true` (`ProductStockDto`) şunları taşır: `productId`, `productName`, `clinicId`, `availableQuantity`, `minimumStockLevel`. **Önerilen miktar** istemcide hesaplanır: `max(minimumStockLevel − availableQuantity, 0)` (yaklaşık; kullanıcı düzenler). Kısayol, mal kabul formunu bu ürünle ve bu miktarla ön dolu tek satırla açar.

## 6) Frontend için değişenler

| Ekran | Değişiklik |
|---|---|
| Stok > Mal kabul (yeni sekme/yüzey) | Başlık: firma, fatura no/tarihi, geliş tarihi, (depo çıkış tarihi, nakil, soğuk zincir izleme: katlanır bölüm), klinik. Satırlar: ürün, miktar, seri/lot no, SKT, birim maliyet. Kaydet = `POST /goods-receipts` tek çağrı |
| Form açılışı | `requestId` bir kez üret; Kaydet tekrarlarında aynı değeri gönder |
| Satır doğrulama | Ürün sınıfı aşı/kontrollü ise lot+SKT alanlarını zorunlu göster (`kind`, `controlledClass`; `GET /products`). Geçmiş SKT'de o satıra "Süresi geçmiş partiyi kabul ediyorum" onayı ekle ve `acknowledgeExpired: true` gönder |
| Hata gösterimi | `lines[i].…` anahtarlı doğrulama hatalarını satıra yansıt (0 tabanlı); iş kuralı hatalarında `detail` başındaki `Satır n:` (1 tabanlı) ile satırı vurgula. Hiçbir satır yazılmadığı için formu aynen koru ve düzeltilmiş gövdeyi aynı `requestId` ile yeniden gönder |
| Başarı | `lines[]` sırasıyla özet; `created: false` ise "bu kayıt zaten alınmış" (sessiz başarı) |
| Düşük stok listesi | Satır eylemi **Giriş** → Mal kabul formu, ürün ve önerilen miktar (bölüm 5) ön dolu |
| Satır sınırı | Satır eklemeyi 100'de durdur |

Yeni izin yoktur; `DbMigrator seed` ve migration gerekmez.
