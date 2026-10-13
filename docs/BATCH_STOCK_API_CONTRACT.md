# Parti (lot) bazlı stok API sözleşmesi — Aşama 5b

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-015-batch-based-stock-and-registers.md` (kabul edildi; Aşama 5 keşfinin D4 kararını değiştirir), `roadmap/KAYIT-DEFTERLERI-RAKIP-BICIMI.md`, `docs/STOCK_MOVEMENT_SAFETY_API_CONTRACT.md` (requestId, kaynak benzersizliği, ters hareket, eşzamanlılık), `docs/ADMINISTRATIONS_API_CONTRACT.md` (ADR-013). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme. Kapsam: parti varlığı, stok girişinin parti oluşturması, uygulama kaydının partiden düşmesi (FEFO), ürün alanları, parti okuma uçları. **Kapsam dışı (sonraki dilim):** kayıt defteri / rapor uçları (aşı, ilaç, narkotik-psikotropik defterleri, Aşı Bilgi raporu), PDF/CSV çıktısı, tedarikçi/cari modülü, alış faturası modülü, depo sayımı/transferi, partiler arası bölünmüş düşüm. **Resmî/yasal defter biçimine uygunluk doğrulanmadı** (ADR-015 karar 5); bu dilim yalnız defterleri türetecek veriyi tutar.

---

## 1) Kavramlar

- **Parti (`StockBatch`)**: bir klinikteki bir ürünün tek bir girişi (seri/lot no, SKT, miktar, firma, fatura, geliş...). Her yeni parti bir `In`/`Initial` hareketiyle doğar; `initialQuantity` girişteki miktardır, `remainingQuantity` o partinin kalanıdır. Parti silinmez/değiştirilmez; kalan yalnız stok hareketleriyle değişir.
- **Partisiz (devir) bakiye**: parti bilgisi olmayan bakiye. Ayrı bir kayıt **yoktur**, türetilir: `unbatchedQuantity = ProductStock.quantityOnHand − Σ parti.remainingQuantity`. Eski stok (bu dilimden önce girilen her şey) otomatik olarak partisiz bakiyedir; **migration stok verisine dokunmaz.** Partisiz bakiyeden düşen/ona eklenen hareketlerin `batchId` değeri `null`dur.
- **Değişmez kural:** `ProductStock.quantityOnHand` her zaman `Σ parti.remainingQuantity + unbatchedQuantity`'dir ve `unbatchedQuantity ≥ 0`'dır. Bütün stok değişimleri mevcut stok handler'larından (`POST /stock-movements`, ters hareket) geçtiği için kural tek yerde korunur.
- **FEFO (SKT'si en yakın önce):** partiden düşümde varsayılan seçim. Sıra: SKT artan; SKT'si olmayan partiler en sonda; eşitlikte giriş zamanı (eski önce). Partisiz bakiye en son sırada ve yalnız partilerin hiçbiri yetmediğinde kullanılır. **Düşüm tek partiden yapılır** (bir uygulama/çıkış iki partiye bölünmez): FEFO sırasındaki **ilk yeterli** parti seçilir. Hiçbiri tek başına yetmiyorsa yetersiz stok kuralı çalışır (`InsufficientStock`); kullanıcı miktarı iki kayıt olarak girer.
- **Zorunluluk ürün sınıfına bağlı:** `Product.kind = Vaccine` veya `Product.controlledClass ≠ None` ise ürün "lot ve SKT zorunlu"dur; bu ürünlerde stok girişinde `batch.lotNumber` ve `batch.expiryDate` zorunludur. Diğer ürünlerde parti bilgisi opsiyoneldir.
- **Süresi geçmiş parti:** `expiryDate` uygulama gününden (İstanbul takvim günü) **önceyse** süresi geçmiştir; SKT günü dahil kullanılabilir. **Uygulama kaydı süresi geçmiş partiden yapılamaz** (kullanıcı kararı 2026-10-13: engelle). Süresi geçmiş stok, manuel `Out`/`Adjustment` (imha/sayım) ile stoktan düşülebilir; bu engellenmez. Girişte geçmiş SKT reddedilmez (eski stok girişi için), yalnız listede `isExpired` ile işaretlenir.
- **Geriye uyum:** Yeni alanların hepsi opsiyoneldir; parti alanı göndermeyen mevcut istemci aynı şekilde çalışır (giriş partisiz bakiyeye eklenir, çıkış FEFO ile uygun partiye ya da partisiz bakiyeye düşer). Mevcut hata kodları ve anlamları değişmez; yetersiz parti bakiyesi mevcut `InsufficientStock` koduyla döner.

### `StockMovementDto` değişikliği (geriye uyumlu, iki alan eklenir)

| Alan | Tip | Not |
|---|---|---|
| `batchId` | guid? | Hareketin etkilediği parti; `null` = partisiz bakiye (eski hareketler dahil) |
| `batchBalanceAfter` | decimal? | Hareketten **sonra** o partinin kalanı. Parti hareketleri için dolu, partisiz/eski hareketlerde `null`. Defterin "kalan bakiye" sütunu ve `Adjustment` yönünün türetilmesi içindir |

`Adjustment` yönü hâlâ saklanmaz (`quantity = |fark|`); parti hareketlerinde yön, `batchBalanceAfter` farkından çıkar.

---

## 2) Ürün alanları (`/products`)

`ProductDto`, `POST /products` ve `PUT /products/{id}` gövdesine eklenen **opsiyonel** alanlar:

| Alan | Tip | Not |
|---|---|---|
| `kind` | string | `Other` (varsayılan) \| `Medicine` \| `Vaccine`. Defter türetmek ve zorunluluk içindir |
| `controlledClass` | string | `None` (varsayılan) \| `Narcotic` \| `Psychotropic` |
| `pharmacologicalForm` | string? | ≤ 100 (ör. enjeksiyonluk çözelti) |
| `presentationForm` | string? | ≤ 100 takdim şekli (ör. flakon) |
| `packaging` | string? | ≤ 100 ambalaj (ör. 10 ml flakon, 1 kutu = 10 flakon) |

`PUT` kısmi güncelleme kuralı (`Examination` ile aynı): alan **gönderilmezse (`null`) dokunulmaz**; metin alanlarında `""` temizler; `kind` ve `controlledClass` gönderilmezse değişmez. Eski istemci PUT'u bu alanları sıfırlamaz. Mevcut ürünlerin hepsi `kind: Other`, `controlledClass: None` olarak gelir; **sınıfların mevcut ürünlere elle atanması gerekir** (migration tahmin etmez).

Sınıf değiştirmek mevcut partileri etkilemez; yalnız sonraki girişlerin zorunluluğunu belirler.

---

## 3) Yetkiler

**Yeni izin yoktur** (seed gerekmez). Parti okuma `StockMovements.Read`; giriş/çıkış `StockMovements.Create`; geri alma `StockMovements.Correct`; uygulama `Administrations.Create`/`Correct`. Ürün alanları `Products.Create`/`Update`.

---

## 4) `POST /api/v1/stock-movements` — parti alanları

Gövdeye eklenen opsiyonel alanlar (diğerleri `STOCK_MOVEMENT_SAFETY_API_CONTRACT.md` ile aynı):

```json
{
  "batch": {
    "lotNumber": "string?",
    "expiryDate": "yyyy-MM-dd?",
    "supplierName": "string?",
    "invoiceNumber": "string?",
    "invoiceDate": "yyyy-MM-dd?",
    "receivedDate": "yyyy-MM-dd?",
    "dispatchedDate": "yyyy-MM-dd?",
    "transportMethod": "string?",
    "coldChainMonitor": "string?"
  },
  "batchId": "guid?"
}
```

| Alan | Sınır |
|---|---|
| `lotNumber` (seri/lot no) | ≤ 100 |
| `expiryDate` (SKT) | tarih |
| `supplierName` (satın alınan firma; serbest metin, cari modülü yok) | ≤ 200 |
| `invoiceNumber` | ≤ 100 |
| `invoiceDate` (fatura tarihi) | tarih |
| `receivedDate` (geliş/teslim tarihi) | tarih; verilmezse defter, hareketin `occurredAtUtc` gününü kullanır (parti alanı `null` kalır) |
| `dispatchedDate` (depo çıkış tarihi) | tarih; verilirse `receivedDate`'ten sonra olamaz |
| `transportMethod` (nakil şekli) | ≤ 200 |
| `coldChainMonitor` (nakilde kullanılan izleme cihazı / soğuk zincir indikatörü) | ≤ 200 |

Tarihler saat içermez (`yyyy-MM-dd`). Bütün `batch` alanları boşsa `batch` verilmemiş sayılır.

**`In` / `Initial`:**

1. `batch` dolu → **yeni parti** oluşur (`initialQuantity = remainingQuantity = quantity`), hareketin `batchId` değeri o partidir. Hareket, parti ve stok değişimi tek transaction'dadır.
2. `batch` yok (veya boş) → giriş **partisiz bakiyeye** eklenir (`batchId: null`); ürün "lot ve SKT zorunlu" ise bu durum reddedilir.
3. "Lot ve SKT zorunlu" üründe `batch.lotNumber` veya `batch.expiryDate` eksikse `StockBatches.LotAndExpiryRequired` (400); hiçbir şey yazılmaz.
4. `batchId` bu hareket tiplerinde gönderilemez (400): giriş her zaman yeni parti açar. Mevcut partiye ek miktar girişi desteklenmez (her giriş ayrı parti: defter satırı).
5. Aynı `lotNumber` aynı üründe tekrar girilebilir (ayrı teslimatlar); benzersizlik zorlanmaz.

**`Out`:**

1. `batchId` verilirse o partiden düşer: parti kiracıya/klinik/ürüne ait olmalı (`StockBatches.NotFound` 404; ürün veya klinik uyuşmazlığı da 404), `remainingQuantity ≥ quantity` olmalı (aksi `StockMovements.InsufficientStock`, 400).
2. `batchId` yoksa **FEFO** ile ilk yeterli parti seçilir; yoksa yeterli partisiz bakiye (`unbatchedQuantity ≥ quantity`); hiçbiri yetmezse `StockMovements.InsufficientStock` (400). Seçim hareketin `batchId` alanında görünür.
3. Mevcut ürün düzeyi kural (eldeki toplam < miktar → `InsufficientStock`) ve bütün diğer kurallar aynen sürer. Manuel çıkış süresi geçmiş partiden de yapılabilir (imha).

**`Adjustment` (sayım düzeltmesi):**

1. `batchId` verilirse `quantity` **o partinin hedef kalanıdır**; hareketin `quantity` değeri `|hedef − mevcut kalan|`, ürün toplamı aynı farkla değişir. Fark yoksa `StockMovements.AdjustmentUnchanged`.
2. `batchId` yoksa mevcut anlam korunur: `quantity` ürünün **hedef toplam** stoğudur; fark **partisiz bakiyeye** uygulanır. Fark negatifse ve partisiz bakiye yetmezse `StockMovements.BatchRequired` (400): düzeltilecek parti `batchId` ile seçilmelidir.

**Eşzamanlılık / tekrar:** `requestId`, kaynak benzersizliği ve ters hareket sözleşmesi aynen geçerlidir (`STOCK_MOVEMENT_SAFETY_API_CONTRACT.md` bölüm 3). Parti kalanı `RowVersion` ile korunur; eşzamanlı iki düşümden biri `StockMovements.ConcurrencyConflict` (409) alır ve aynı gövdeyle yeniden denemek güvenlidir (tekrar denenince güncel kalana göre karar verilir). Aynı `requestId` ile tekrar istek ilk hareketi (partisiyle birlikte) döndürür; **yeni parti ikinci kez oluşmaz**.

---

## 5) `POST /api/v1/stock-movements/{id}/reverse` — parti etkisi

Ters hareket orijinal hareketin partisine uygulanır ve aynı `batchId`'yi taşır:

| Geri alınan | Etki |
|---|---|
| `Out` (batchId = P) | P'nin kalanı miktar kadar **artar** (aynı partiye iade). `batchId = null` ise partisiz bakiyeye iade |
| `In`/`Initial` (batchId = P) | P'nin kalanı miktar kadar **azalır**; P'nin kalanı yetmiyorsa (parti kısmen kullanılmış) `StockMovements.InsufficientStock` (400). `batchId = null` ise partisiz bakiye yetmiyorsa aynı hata |

Giriş geri alınınca parti kalanı 0 olur ama parti kaydı kalır (silinmez). Diğer kurallar (yetki, `AlreadyReversed`, pasif ürün, `NotReversible`) değişmez.

---

## 6) Uygulama kaydı (`/administrations`) — partiden düşüm

`POST /administrations` gövdesine opsiyonel `batchId` eklenir. Uygulama, bir partiden düşer:

1. `batchId` verilirse o parti kullanılır; yoksa FEFO ile **süresi geçmemiş** ilk yeterli parti seçilir; yoksa yeterli partisiz bakiye; hiçbiri yetmezse `Administrations.InsufficientStock` (400).
2. Seçilen parti süresi geçmişse `Administrations.BatchExpired` (400); kayıt ve stok değişmez. (Varsayılan FEFO süresi geçmiş partiyi hiç seçmez; yalnız açıkça seçilen parti bu hatayı verir. Yalnız süresi geçmiş partilerde yeterli stok varsa varsayılan seçim `Administrations.BatchExpired` döner.)
3. Parti kiracıya/klinik/ürüne ait değilse `StockBatches.NotFound` (404).
4. Düşüm mevcut `Out` hareketi üzerinden yapılır; seçilen parti hareketin ve uygulamanın `batchId` alanında kalır. İptalde (`StockReturned`) ters hareket aynı partiye iade eder; `WrittenOff` partiye dokunmaz (parti kalanı düşmüş kalır).
5. Aynı `requestId` ile tekrarda ilk kayıt döner; farklı `batchId` istenmişse `Administrations.DuplicateRequestId` (409).

`AdministrationDto`'ya eklenen alanlar (hepsi opsiyonel/geriye uyumlu):

| Alan | Tip | Not |
|---|---|---|
| `batchId` | guid? | Düşülen parti; `null` = partisiz bakiye (eski uygulamalar dahil) |
| `lotNumber` | string? | Partinin seri/lot no'su |
| `expiryDate` | date? | Partinin SKT'si |

Liste/detay/oluşturma yanıtında bulunur. İptal edilmiş uygulamada da düşülen parti görünür.

---

## 7) Okuma uçları

Hepsi `StockMovements.Read`; klinik kapsamı stok hareketleriyle aynıdır (`clinicId` veya aktif klinik bağlamı zorunlu: `StockBatches.ClinicScopeRequired` 400; bağlamla uyuşmazlık `StockBatches.ClinicContextMismatch` 400; atanmamış klinik `Clinics.AccessDenied` 403).

### `StockBatchDto`

| Alan | Tip | Not |
|---|---|---|
| `id`, `clinicId`, `productId` | guid | |
| `productName`, `productSku`, `unit` | string | Ürünün mevcut adı/SKU'su/stok birimi |
| `productKind`, `controlledClass` | string | Defter sınıflaması için |
| `lotNumber`, `expiryDate` | string?, date? | |
| `initialQuantity`, `remainingQuantity` | decimal | |
| `supplierName`, `invoiceNumber`, `invoiceDate`, `receivedDate`, `dispatchedDate`, `transportMethod`, `coldChainMonitor` | | Giriş alanları |
| `isExpired` | bool | `expiryDate < bugün` (İstanbul günü) |
| `createdAtUtc` | datetime | Parti oluşma zamanı |
| `createdByUserId` | guid? | |

### `GET /api/v1/stock-batches`

Sayfalı. Süzgeçler: `clinicId`, `productId`, `lotNumber` (içerir), `availableOnly` (varsayılan `true`: kalan > 0), `expired` (`true` yalnız süresi geçmiş, `false` yalnız geçmemiş; verilmezse hepsi), `expiresBeforeDate` (SKT bu tarihten önce/eşit olanlar). Sıralama FEFO: SKT artan (SKT'siz sonda), sonra `createdAtUtc`.

### `GET /api/v1/stock-batches/{id}`

`StockBatchDto`; yoksa/kapsam dışıysa `StockBatches.NotFound` (404).

### `GET /api/v1/products/{id}/stock-batches?clinicId=`

Ürün bazlı parti bakiyesi (uygulama formunun parti seçimi için). Yanıt:

```json
{
  "productId": "guid",
  "clinicId": "guid",
  "unit": "şişe",
  "quantityOnHand": 14,
  "batchedQuantity": 12,
  "unbatchedQuantity": 2,
  "batches": [ "StockBatchDto", "... FEFO sıralı, remainingQuantity > 0" ]
}
```

`unbatchedQuantity` partisiz bakiyedir. Ürün yoksa `Products.NotFound` (404). `quantityOnHand` stok satırı yoksa 0'dır.

---

## 8) Hata kodları

| Kod | HTTP |
|---|---|
| `StockBatches.LotAndExpiryRequired`, `StockBatches.Validation` (depo çıkış tarihi geliş tarihinden sonra), `StockMovements.BatchRequired`, `Administrations.BatchExpired`, `StockBatches.ClinicScopeRequired`, `StockBatches.ClinicContextMismatch` | 400 |
| `StockMovements.InsufficientStock`, `Administrations.InsufficientStock` (yetersiz parti bakiyesi dahil) | 400 |
| `StockBatches.NotFound` | 404 |
| `StockMovements.ConcurrencyConflict`, `Administrations.DuplicateRequestId` | 409 |
| `Clinics.AccessDenied` | 403 |

Alan sınırı aşımı, `batchId` giriş hareketinde, `batch` çıkış/sayımda gibi biçim hataları standart doğrulama yanıtıyla 400 döner (`ValidationBehavior`).

Mevcut kodlar değişmedi.

---

## 9) Uygulama notları

- **Veri modeli (migration var):** yeni `StockBatches` tablosu (`TenantId`, `ClinicId`, `ProductId`, `LotNumber`, `ExpiryDate`, `InitialQuantity`, `RemainingQuantity` ≥ 0 CHECK, giriş alanları, `CreatedAtUtc`, `CreatedByUserId`, `RowVersion`; FK ürün ve klinik `Restrict`; indeksler `(TenantId, ClinicId, ProductId, ExpiryDate)` ve `(TenantId, ClinicId, LotNumber)`). `StockMovements`'a `BatchId` (nullable FK, `Restrict`) ve `BatchBalanceAfter`; `Administrations`'a `BatchId` (nullable FK); `Products`'a `Kind` (int, varsayılan 0), `ControlledClass` (int, varsayılan 0), `PharmacologicalForm`, `PresentationForm`, `Packaging`. **Mevcut satırlara veri yazılmaz**: eski hareket ve uygulamalar `BatchId = null` (partisiz) kalır, eski bakiye partisiz bakiyedir. Down yalnız eklenen kolon/tablo/indeksleri siler.
- **Tek yerde:** parti seçimi (FEFO, açık `batchId` doğrulaması, partisiz bakiye hesabı) uygulama katmanında tek bir çözücüde yaşar; hem `POST /stock-movements` hem `POST /administrations` onu kullanır. Süresi geçmiş partiyi engelleme kuralı yalnız uygulama yolunda açıktır. Ters hareket parti etkisini orijinal hareketin `batchId` değerinden alır.
- Yazma sırası: önce hareket (mevcut partinin ve stok satırının güncellemesiyle aynı `SaveChanges`; `requestId`/kaynak benzersizliği hiçbir şey yazılmadan eler), sonra yeni stok satırı, **en son yeni parti** ve hareketin `batchId` bağı. Yeni parti hareketten önce yazılırsa, `requestId` yarışını kaybeden istek (tekrar olarak 200 dönse de) sahipsiz bir parti bırakır; bu bir entegrasyon testiyle yakalanıp bu sıraya çevrildi.
- Uygulama komutu partiyi önce seçer, uygulamayı yazar, sonra seçilen `batchId` ile mevcut `CreateStockMovementCommand`'ı çağırır; stok kuralları (yetersiz bakiye, eşzamanlılık) tek yerde (stok handler'ı) yaşar.
- Defter türetimi (sonraki dilim) bu veriden yapılır: Aşı/İlaç giriş defteri = `In`/`Initial` hareketleri + parti; narkotik/psikotropik stok ve sarf defteri = parti hareketleri (`In`/`Out`/`Reversal`/`Adjustment`) + `batchBalanceAfter` + belge no (`invoiceNumber`, uygulamada `referenceId`); Aşı Bilgi raporu = uygulama + `lotNumber`/`expiryDate`. Partisiz eski bakiye devir satırı olarak türetilir.
- Açık / ertelenen: bölünmüş düşüm (iki partiden tek uygulama), açıkça "partisiz bakiyeyi seç" seçeneği, partiyi iade/devir (tedarikçiye iade) hareketi, geçmiş SKT girişini engelleme, aşı uygulamasının (`Vaccination`) partiye bağlanması (D6: aşı kaydı hâlâ stok düşmez), sınıfı değişen ürünün mevcut partileri için uyarı.
