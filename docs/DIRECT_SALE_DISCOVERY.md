# Doğrudan satış keşfi (muayenesiz ürün satışı)

Salt okunur keşif; kod, migration veya sözleşme değişikliği yok. Taban: `main` @ `8664e66` (2026-10-14). Girdi: ürün deposu `roadmap/RAKIP-ANALIZI-2026-10.md` (satır 59), ADR-012 (ücret/ödeme/bakiye), ADR-013 (uygulama iptali), ADR-015 (parti stok ve defterler), `docs/FIN_DISCOVERY.md`, `docs/BATCH_STOCK_API_CONTRACT.md`.

**Doğrulama notu:** Aşağıdaki bulgular kod okunarak çıkarıldı. Hiçbir test, derleme veya uygulama çalıştırılmadı (bilgisayar kısıtlı). "Çıkarım" yazan yerler kod okumasından sonucudur; çalıştırılıp doğrulanmadı. Zorluk tahminleri ölçülmedi.

**Tanım:** Doğrudan satış = muayene/hasta kaydı olmadan ürün satışı (mama, ilaç, aksesuar). Stoktan düşer, ücret ve ödeme oluşur.

## Özet

- Parçaların çoğu var: `Charge` (ücret, müşteri zorunlu, muayene/ziyaret opsiyonel), `Payment` + `PaymentAllocation` (tahsis, iptal), `StockMovement` + `StockBatch` (FEFO, ters hareket), `Administration` (stok düşüren uygulama kaydı).
- **Eksik olan bağ:** `Charge` bir ürüne bağlı değil (ürün alanı yok), stok düşmez. `Administration` stok düşer ama **hayvan zorunlu** (`PetId`), ücret üretmez. Yani bugün "ürün sat → hem stok düş hem ücret yaz" yapan tek bir akış yok.
- **Defterlere düşme:** Aşı Bilgi ve narkotik sarf defterleri bugün yalnız `Administration` kayıtlarından türetiliyor (hayvan/sahip adı için). Hayvansız satış bu defterlere kendiliğinden düşmez; ayrı karar gerekir (bölüm 4).
- Önerilen yön (karar kullanıcıda): yeni bir `Sale` (satış) varlığı; ücret ve stok hareketini mevcut komutlarla üretmesi. Ayrıntı bölüm 2 ve 8.

## 1) Mevcut koddaki ilgili parçalar

### 1.1 Ücret: `Charge`

Dosya: `src/Backend.Veteriner.Domain/Charges/Charge.cs`

- Alanlar: `TenantId, ClinicId, ClientId` (zorunlu), `PetId?, VisitId?, ExaminationId?`, `Description` (≤500), `Quantity` (≤100000, 3 ondalık), `UnitPrice` (≤10 milyon, 2 ondalık), `Amount = round(Quantity × UnitPrice, 2, AwayFromZero)`, `Currency` (yalnız `TRY`), `AllocatedAmount`, `MutationSequence` (eşzamanlılık belirteci), `VoidedAtUtc/By/Reason`, `CreatedByUserId`, `RequestId?`.
- **Ürün alanı yok** (`ProductId` bulunamadı). Tek satır tek açıklama; çok satırlı sepet kavramı yok. Her satır ayrı `Charge`.
- Müşteri zorunlu (`Charge.Create`: "ClientId zorunludur"). Yani **müşterisiz perakende şu an modellenemiyor**.
- Düzeltme (`Correct`): açıklama/miktar/birim fiyat, gerekçe zorunlu, yeni tutar tahsis edilen tutarın altına inemez. İptal (`Void`): tahsisi olan ücret iptal edilemez (`Charges.VoidBlockedByAllocation`).
- `RequestId` tekrar koruması: `CreateChargeCommandHandler.cs` (aynı id ilk kaydı döner, başka istek için kullanılmışsa reddeder); veritabanı: filtreli benzersiz indeks `UX_Charges_TenantId_RequestId` (`ChargeConfiguration.cs`).
- Uçlar: `src/Backend.Veteriner.Api/Controllers/ChargesController.cs`; izinler `Charges.Read/Create/Correct`.

### 1.2 Tahsilat: `Payment` ve `PaymentAllocation`

Dosyalar: `src/Backend.Veteriner.Domain/Payments/Payment.cs`, `PaymentAllocation.cs`

- `Payment`: `ClientId` zorunlu, `Amount`, `Currency`, `Method` (Cash/Card/Transfer), `PaidAtUtc`, `AllocatedAmount`, `RequestId` + `RequestHash` (içerik özeti, `PaymentRequestFingerprint.cs`), `MutationSequence`, iptal alanları.
- ADR-012 karar 6: her yeni ödeme en az bir ücrete **tam** tahsis edilir (`Payment.AllocateFully`: toplam ödeme tutarına eşit, ücret açık tutarını aşamaz, aynı klinik/müşteri/para birimi). `POST /payments` `requestId` ve `allocations[]` ister (`CreatePaymentCommand.cs`).
- **Çıkarım:** Satış akışı ödemeyi mevcut `CreatePaymentCommand` ile üretebilir (satışın ücretlerine tahsis). Ödeme müşteri zorunlu olduğu için müşterisiz satışta çalışmaz (bölüm 2, Soru 2).
- Ödeme iptali: `Payment.Void` — tahsisleri geri alır (`PaymentAllocation.ReversedAtUtc`), ücret sayaçlarını düşürür, `Payments.Correct` + gerekçe (5–500 karakter).

### 1.3 Stok: `StockMovement`, `StockBatch`, FEFO

Dosyalar: `Domain/Products/StockMovement.cs`, `StockBatch.cs`, `Application/StockBatches/StockBatchResolver.cs`, `Application/StockMovements/Commands/Create|Reverse`

- `StockMovement` eklenir kayıttır (append-only). Tipler: `Initial, In, Out, Adjustment, Reversal`. Alanlar: `ReferenceType/ReferenceId` (kaynak izi), `RequestId`, `ReversesMovementId`, `BatchId`, `BatchBalanceAfter`.
- **Kaynak benzersizliği:** `(TenantId, ClinicId, ProductId, ReferenceType, ReferenceId, MovementType)` üzerinde filtreli benzersiz indeks (`StockMovementConfiguration.cs`, satır 52–54). Sonuç: aynı kaynak için aynı üründen **tek** `Out` olur. Çok satırlı satışta kaynak kimliği **satır kimliği** olmalı (aynı ürün iki satırda gelirse çakışmasın). Bu bir tasarım kısıtıdır.
- Parti/FEFO: `docs/BATCH_STOCK_API_CONTRACT.md` bölüm 1 ve 4. Çıkışta `batchId` verilmezse SKT'si en yakın, **ilk yeterli tek parti**; olmazsa partisiz bakiye; yoksa `StockMovements.InsufficientStock`. Düşüm iki partiye bölünmez.
- `CreateStockMovementCommand` parametresi `NotExpiredOn`: uygulama yolunda süresi geçmiş partiyi engeller (`Administrations.BatchExpired`); manuel çıkış engellemez. **Satışın süresi geçmiş partiden yapılıp yapılmayacağı kararı** gerekir (Soru 7).
- Ters hareket: `ReverseStockMovementCommand`; `Out` geri alınınca aynı partiye iade edilir, `Initial/In` geri alınırken parti kalanı yetmezse hata.

### 1.4 Uygulama kaydı: `Administration`

Dosyalar: `Domain/Administrations/Administration.cs`, `Application/Administrations/Commands/Create|Void`

- Hastaya uygulanan ürünü kaydeder ve `CreateStockMovementCommand` (Out, `ReferenceType = "Administration"`, `ReferenceId = administration.Id`) ile aynı işlemde stok düşer. Atomiklik `ITransactionalRollbackOnFailureRequest` ile sağlanır (stok adımı başarısızsa uygulama geri alınır).
- **`PetId` zorunlu**; ücret üretmez. Muayene/tedavi opsiyonel.
- Tekrar koruması: `RequestId` + `UX_Administrations_TenantId_RequestId`, yarış kaybedeni kazananı döner (`DbUpdateException` yakalama deseni).
- İptal (`VoidAdministrationCommandHandler`): `StockReturned` (ters hareket, aynı partiye iade) veya `WrittenOff` (stok geri artmaz). ADR-013 kararı.
- Bu akış satış için **desen** olarak yeniden kullanılır (kayıt yaz → stok komutunu çağır → partiyi bağla → tek işlem); doğrudan kullanılamaz (hayvan zorunlu, ücret yok).

### 1.5 Defterler

Dosyalar: `Application/Reports/Registers/*`, `Infrastructure/Persistence/Repositories/Reports/*Reader.cs`

- **Narkotik/psikotropik stok ve sarf defteri** (`ControlledSubstanceRegisterReader.cs`): stok hareketlerinden türetilir (`In/Out/Reversal/Adjustment`). Çıkış satırında "Çıkış Yapılan Yer" yalnızca `ReferenceType == "Administration"` ise `Uygulama: Hasta (Sahip)` yazar (`ControlledSubstanceRegisterMapper.cs`); aksi hâlde hareketin `Reason` alanı (yoksa "Stok çıkışı") yazılır. **Çıkarım:** bir satış `Out` hareketi defterde görünür ama alıcı bilgisi (kime satıldı) olmaz, yalnız `Reason` metni olur.
- **Aşı Bilgi raporu** (`VaccineInfoRegisterReader.cs`): `Administrations` ⨝ `Products` (`Kind == Vaccine`) ⨝ `Pets` ⨝ `Clients` ⨝ `Species` ⨝ parti. Hayvana bağlı değilse **raporda görünmez** (çıkarım: join `Pets` üzerinden).
- Aşı Kayıt Defteri (`VaccineRegisterReader.cs`) ve İlaç Kayıt Defteri (`MedicationRegisterReader.cs`): yapıları ayrıca okunmadı; hangi kaynaktan türediği **doğrulanmadı**. Uygulamada kodlanacak dilimde ilk iş olarak okunmalı.
- `docs/BATCH_STOCK_API_CONTRACT.md` bölüm 9: aşı uygulaması (`Vaccination`) hâlâ stok düşmez (D6); kontrollü ürün stoğu `Administration` ve manuel çıkıştan düşer.

### 1.6 İzinler

`src/Backend.Veteriner.Application/Auth/PermissionCatalog.cs` ve `RolePermissionBindings.cs` (rol aralıkları satır başlıklarından okundu; matris testi çalıştırılmadı):

| Rol | Charges | Payments | StockMovements | Administrations |
|---|---|---|---|---|
| Admin, Owner, ClinicAdmin | Read, Create, Correct | Read, Create, Update, Correct | Read, Create, Correct | Read, Create, Correct |
| Veteriner | Read | Read | Read | Read, Create (Correct yalnız Veteriner için ek eşlemede, satır 504) |
| Sekreter | Read, Create | Read, Create, Update | Read, Create | Read |

Bulgu: **Sekreter ürün satabilir** gibi görünen bir izin kümesine sahip (ücret + ödeme + stok hareketi), Veteriner ise ödeme ve ücret ekleyemez. Doğrudan satış genelde resepsiyonda yapılır; bu uyum olumlu ama ayrı bir `Sales.*` izni mi, mevcutların bileşimi mi karar noktası (bölüm 5).

### 1.7 Ürün

`Domain/Products/Product.cs`: `UnitPrice`, `Currency`, `IsActive`, `Kind` (`Other/Medicine/Vaccine`), `ControlledClass` (`None/Narcotic/Psychotropic`). Satış fiyatı için hazır alan var (`UnitPrice`); **KDV, ayrı alış/satış fiyatı, barkod: bulunamadı** (barkodla okutma isteği varsa ayrı iş).

### 1.8 Mevcut desenler (iade/iptal)

- Silme yok; iptal alanları (`VoidedAtUtc/By/Reason`) + gerekçe zorunlu (`CorrectionReason`, 5–500 karakter) + audit (`IAuditableRequest`).
- Stok için silme yok; ters hareket.
- Eşzamanlılık: `MutationSequence` (Charge, Payment) / `RowVersion` (Administration) → 409 `*.ConcurrencyConflict`.
- Tekrar koruması: `requestId` + filtreli benzersiz indeks + yarış kaybedende kazananı döndürme.
- Ödeme iptali "hiç alınmamış sayılır"; ayrı iade/kasa çıkış kaydı yok (`FINANCE_CORRECTION_API_CONTRACT.md` bölüm 1; kasa kaydı CHECKOUT-001, ertelenmiş).

## 2) Tasarım seçenekleri

### Seçenek A — Yeni `Sale` + `SaleLine` varlığı (öneri)

Satış başlığı: klinik, müşteri (opsiyonel, Soru 2), hasta (opsiyonel), kullanıcı, tarih, durum (Tamamlandı/İptal), `RequestId`, `MutationSequence`. Satır: ürün, miktar, birim fiyat, parti (`BatchId`), satır tutarı, ilişkili `ChargeId` ve stok hareketi kimliği. Satışı oluşturan komut mevcut komutları aynı işlemde çağırır: satır başına `CreateStockMovementCommand` (`ReferenceType = "SaleLine"`, `ReferenceId = satır id`) ve `Charge` (veya satış başına tek `Charge`, Soru 5).

- Artı: Ürün-ücret-stok bağı tek yerde; iade/iptal tek satış üzerinden izlenir; sepet doğal; satış listesi/raporu temiz; `Charge` değişmez (finans yüzeyine dokunmaz); `Administration` deseni birebir yeniden kullanılır.
- Eksi: Yeni varlık + iki tablo; yeni uçlar, izinler, projeksiyon/rapor işi. `Charge` ile `Sale` arasında tutarlılık (satış tutarı = ücret tutarı) iki yerde durur; kural tek yerde (satış komutu) tutulmalı.
- Migration: **evet** — `Sales`, `SaleLines` tabloları (+ kiracı/klinik/tarih/müşteri indeksleri, `UX_Sales_TenantId_RequestId`). Mevcut tablo/veri değişikliği yok.

### Seçenek B — `Charge`'ı genişletme (ürün bağı ekle)

`Charge`'a `ProductId?`, `BatchId?`, `StockMovementId?` eklenir; ürünlü ücret yazılınca stok düşer.

- Artı: Yeni varlık yok; migration küçük (3 nullable kolon).
- Eksi: `Charge` bugün "para borcu" kavramı; stok yan etkisi eklemek tek sorumluluğu bozar (`Correct` miktar değişirse stok ne olur? `Void` stoğu geri verir mi?). Tek satır = tek ürün olduğu için sepet 5 ayrı ücret olur, "tek satış" gruplanamaz. Müşteri zorunlu kalır (müşterisiz perakende imkânsız). Ücret düzeltme/iptal kuralları (ADR-012) stok kurallarıyla iç içe geçer; risk yüksek.
- Migration: **evet** (kolonlar + FK).

### Seçenek C — `Administration`'ı genişletme (PetId opsiyonel, ücret ekle)

- Artı: stok ve parti kodu hazır.
- Eksi: "Uygulama" klinik kayıttır (hasta zorunlu, muayene bağı, defter dili); perakende satışı bu modele sokmak Aşı Bilgi ve klinik zaman çizelgesini kirletir. `PetId` zorunluluğu birçok sorgu/rapor varsayımı (join) taşıyor. **Önerilmez.**
- Migration: evet (kolon null yapma, ücret bağı).

### Karşılaştırma

| | A: `Sale` | B: `Charge` | C: `Administration` |
|---|---|---|---|
| Çok satırlı sepet | Evet | Hayır (gruplanamaz) | Hayır |
| Müşterisiz perakende | Mümkün (karar) | Hayır | Hayır |
| Mevcut finans davranışına riski | Düşük | Yüksek | Orta |
| Migration | 2 yeni tablo | 3 kolon | kolon değişimi |
| Yeni yüzey (uç/izin/rapor) | Çok | Az | Az |
| Tavsiye | **Evet** | Hayır | Hayır |

### Müşterili ve müşterisiz perakende

- Müşterili: `Charge.ClientId`/`Payment.ClientId` zorunlu olduğu için mevcut finans hattı değişmeden çalışır. Bakiye (`GET /clients/{id}/balance`) satıştaki ücreti doğal olarak gösterir; veresiye (kısmi/sonra ödeme) mümkün.
- Müşterisiz ("peşin satış", isimsiz): `Charge` ve `Payment` müşteri ister. Seçenekler: (1) her kliniğe sistem tarafından "Peşin müşteri" adlı bir müşteri kaydı (mevcut model değişmez; tek kayıt altında tüm perakende birikir, bakiye anlamsız olmasın diye hep tam ödenir), (2) `Charge/Payment.ClientId` null yapılabilir hâle getirmek (finans değişmezlerini, bakiye ve beş okuma yüzeyini etkiler; yüksek risk). Öneri 1. Karar kullanıcıda (Soru 2).

### Çok satırlı sepet, parti seçimi, FEFO

- Sepet = `Sale` + N `SaleLine`. Her satır kendi stok `Out`'unu üretir (referans = satır id). Aynı ürün iki satırda gelirse FEFO her seferinde yeniden çözer.
- Parti: satırda opsiyonel `batchId`; verilmezse FEFO (mevcut çözücü, `StockBatchResolver`). Kontrollü/aşı ürününde partisiz bakiye kuralı mevcut sözleşmeyle aynı kalır. Miktar tek partiye sığmıyorsa mevcut kural (`InsufficientStock`, bölünmez); kasada bu can sıkıcı olabilir: satırı kullanıcı iki satır girer. Bölünmüş düşüm ertelenmiş iş (`BATCH_STOCK_API_CONTRACT.md` bölüm 9). Perakendede sık olursa öncelik yükselir (Soru 7).
- Fiyat: `Product.UnitPrice` varsayılan, satırda değiştirilebilir mi (indirim) kullanıcı kararı (Soru 6).

### Ödeme akışı

- Satış tamamlanınca ücret yazılır; ödeme ayrı bir adımdır ve mevcut `POST /payments` + `allocations[]` ile yapılır ya da satış ucu "ödemeyi birlikte al" seçeneğiyle iki komutu aynı işlemde çağırır. Öneri: satış ucu tek çağrıda `payment` bloğunu opsiyonel kabul etsin (peşin satışta zorunlu, müşterili satışta isteğe bağlı = veresiye). Bu karar kullanıcıya sorulur (Soru 4). Ödeme yöntemi, `requestId` ve fazla ödeme reddi mevcut kurallarla aynı.

## 3) İade, iptal ve düzeltme kuralı

Mevcut desenlerle tutarlı öneri (kullanıcı onayına tabi):

- **Silme yok.** Satış iptal edilir (gerekçe zorunlu, 5–500 karakter, audit); kayıt kalır.
- **Tam iptal:** satış iptalinde (a) ödeme varsa önce `Payments.Void` (mevcut kural: ödenmiş ücret iptal edilemez), (b) ücret `Void`, (c) her satırın `Out` hareketi **ters hareket** ile aynı partiye iade edilir. Hepsi tek işlemde; biri başarısız olursa hiçbiri kalmaz. ADR-013 ilkesiyle aynı çizgi: iade edilen mal kullanılabilirse stoğa döner; bozuk/imha ise **zarar** olarak işaretlenir (stok artmaz). Yani `StockReturned` / `WrittenOff` seçimi satış iptalinde de sorulur (Soru 8).
- **Kısmi iade** (sepetten bir kalemin geri alınması): Seçenek A'da doğal biçimi satış satırı bazlı iade (`SaleLine` iptali: o satırın stok ters hareketi + ücret düzeltmesi). Bu mevcut `Charge.Correct`'u (tutar düşürme, gerekçe) ve satır başı ters hareketi birleştirir. Karmaşık; ilk dilimde yalnız tam iptal önerilir, kısmi iade sonraki dilime (Soru 8).
- **Düzeltme:** Yanlış fiyat/miktar için "düzenleme" yerine iptal + yeni satış önerilir (kayıt değişmezliği, defterlerde sürpriz yok). Alternatif: `Charge.Correct` ile tutar düzeltme; ama stok miktarını değiştirmez, satırla ücret ayrışır. Bu yüzden önerilmez.
- Yetki: ADR-012 karar 2 ile uyumlu olarak iptal/iade yönetici düzeyi (`*.Correct`); resepsiyon satış **ekler**, düzeltmez.
- Eşzamanlılık: `Sale.MutationSequence`; iki kullanıcı aynı satışı iptal etmeye çalışırsa biri 409 alır.
- Tarih sınırı: satış sonrası iptalde süre sınırı (ör. kapanmış gün) ürün kararıdır; mevcut finans düzeltmelerinde böyle bir sınır **bulunamadı**.

## 4) Kontrollü ürün ve aşı satışının defterlere düşmesi

Bugünkü durum (bölüm 1.5): defterler `Administration` odaklı. Satışta hayvan yok.

| Ürün sınıfı | Satışta stok | Narkotik/psikotropik sarf defteri | Aşı Bilgi raporu |
|---|---|---|---|
| Sıradan ürün | Düşer | Yok | Yok |
| Kontrollü (`Narcotic/Psychotropic`) | Düşer, lot+SKT zorunlu kural geçerli | `Out` hareketi defterde görünür; **alıcı bilgisi eksik** (mapper `Reason` yazar) | Yok |
| Aşı (`Kind=Vaccine`) | Düşer | Yok | **Görünmez** (hayvan ve `Administration` yok) |

Seçenekler:

1. **Kontrollü ürün/aşı doğrudan satılamaz** (yalnız hekim uygulamasıyla kullanılır). Basit ve güvenli; en az risk. Rakipte bu davranış doğrulanmadı.
2. **Kontrollü ürün satılabilir, ama müşteri + hayvan zorunlu** ve satış `Sale` yanında bir `Administration`-benzeri kaydı gerekir: defter için alıcı (müşteri/hayvan) bilgisi. Mapper'a `ReferenceType == "SaleLine"` dalı eklenir: `Satış: Müşteri (Hayvan)`. Mümkün, küçük bir mapper değişikliği.
3. **Aşı satışı** (müşteriye aşı flakonu satmak) yasal/etik olarak ayrı soru: aşı hayvana klinikte uygulanır; satış yapılırsa Aşı Bilgi raporunda yer almaz. Öneri: aşı ve kontrollü ürün satışı ilk sürümde **kapalı** (satış satırında reddedilir), sonra açılır.

Resmî defter biçimine uygunluk doğrulanmadı (ADR-015 karar 5); bu keşif bu konuyu değiştirmez. Karar ürün/hukuk alanıdır (Soru 3).

## 5) Yetki, audit, requestId, RowVersion

- **İzin önerisi:** yeni `Sales.Read`, `Sales.Create`, `Sales.Correct` (iptal/iade). Gerekçe: satışın bileşimi bugün `Charges.Create` + `Payments.Create` + `StockMovements.Create` gerektirir; bu üç izni tek tek kontrol etmek hata kaynağı. Alternatif (yeni izin yok): komut bu üçünü ister. Yeni izin eklendiğinde seed/backfill gerekir (`RolePermissionBindingSeeder` idempotent çalışır; çalıştırılmadı). Öneri rolleri: Sekreter + yönetici düzeyi `Create`; `Correct` yalnız Admin/Owner/ClinicAdmin. Veteriner satış yapabilsin mi kullanıcıya sorulur (Soru 9).
- **Audit:** `IAuditableRequest` (`Sale.Create`, `Sale.Void`) ve gerekçe saklama; mevcut komutlardaki desen (örn. `CreatePaymentCommand.cs`).
- **requestId:** zorunlu (`Guid`). `UX_Sales_TenantId_RequestId` filtreli benzersiz indeks; yarış kaybedeni kazananı döner; farklı içerikle aynı id ise `Sales.DuplicateRequestId` (409) — `PaymentRequestFingerprint` deseni (içerik özeti). Satır stok hareketleri için ayrıca kaynak benzersizliği (satır id) ikinci savunma.
- **Eşzamanlılık:** `Sale.MutationSequence` (Charge/Payment ile aynı) → 409 `Sales.ConcurrencyConflict`. Stok tarafı mevcut `StockMovements.ConcurrencyConflict` (parti `RowVersion`). Çıkarım: iki kasada aynı son ürün satılırsa biri 409 veya `InsufficientStock` alır; tekrar denemek güvenli.
- **Atomiklik:** komut `ITransactionalRollbackOnFailureRequest` işaretli olmalı (Administration/Payment deseni).
- **Klinik/kiracı kapsamı:** `IClinicReadScopeResolver` (mevcut yazma komutlarında kullanılan çözücü).

## 6) Fatura / e-belge sınırı

- INT-007 (e-belge/fatura) **kapsam dışı**. ADR-012: "Klinik ücret/tahsilat özeti ≠ resmi e-belge."
- Satış fişi (yazdırılabilir özet, bilgi amaçlı): kapsamda olabilir, ama "resmî belge değildir" notu taşır.
- Bugünkü modelde **KDV yok** (fiyat tek tutar). Ürün satışında KDV oranı belge/fatura için gerekir; modellenirse ayrı karar ve ayrı iş (bu keşifte önerilmiyor).
- Yazar kasa / mali cihaz / ÖKC entegrasyonu: kapsam dışı. Hukuki olarak perakende satışın mali belgesiz yapılabilirliği bu belgede **doğrulanmadı**; pilot klinik muhasebecisine sorulmalı (Soru 10).

## 7) Kullanıcıya sorulacak kararlar

Ürün kararı verilmedi; her biri için sade soru, önerilen cevap ve neden.

| # | Soru | Önerilen cevap | Neden |
|---|---|---|---|
| 1 | Doğrudan satış, yeni bir "Satış" kaydı olarak mı yapılsın (ücreti ve stoğu o üretsin), yoksa mevcut Ücret kaydına ürün bağı mı eklensin? | Yeni "Satış" kaydı | Sepet, iade ve stok bağı tek yerde; mevcut ücret/ödeme kuralları bozulmaz. |
| 2 | Müşterisi olmayan (isimsiz, peşin) satış gerekli mi? Gerekliyse nasıl? | İlk sürümde her kliniğe tek bir "Peşin müşteri" kaydı; satış hep tam ödenir | Ücret ve ödeme bugün müşteri ister; boş müşteriye izin vermek bakiye ve beş okuma yüzeyini riske atar. |
| 3 | Aşı ve narkotik/psikotropik ürünler satılabilsin mi? | İlk sürümde hayır (yalnız hekim uygulamasıyla) | Defterler alıcı/hayvan bilgisi ister; satışta bu bilgi yok. Sonra, müşteri+hayvan zorunlu olarak açılabilir. |
| 4 | Ödeme satış anında mı alınsın, yoksa sonra da ödenebilsin (veresiye)? | Müşterili satışta veresiye olabilsin; peşin müşteride ödeme zorunlu | Mevcut bakiye/tahsis kurallarıyla uyumlu. |
| 5 | Sepetteki her ürün ayrı ücret satırı mı olsun, yoksa tüm sepet tek ücret mi? | Satış başına tek ücret, açıklaması "Ürün satışı" (satır dökümü satışta) | Müşteri bakiyesinde tek satır görünür; iptalde tek işlem. (Her ürün ayrı ücret isterseniz de yapılır.) |
| 6 | Satışta fiyat veya indirim değiştirilebilsin mi? Kim? | Ürün fiyatı varsayılan; satırda fiyat düzenlenebilir, düzenleme gerekçesiz; ayrı indirim alanı ilk sürümde yok | Basit başlar; indirim yetkisi/oranı ürün kararı (ayrı soru). |
| 7 | Süresi geçmiş partiden satış engellensin mi? Miktar tek partiye sığmazsa ne olsun? | Engelle (uygulama kuralıyla aynı); sığmazsa kullanıcı iki satır girer | Mevcut parti sözleşmesiyle tutarlı; bölünmüş düşüm sık ihtiyaç olursa sonra. |
| 8 | Satış iptal/iade edilirse mal stoğa mı dönsün, zarar mı yazılsın? İlk sürümde kısmi iade (bir kalem) gerekli mi? | Seçim sorulsun (stoğa dön / zarar); ilk sürümde yalnız tam iptal | ADR-013 ile aynı çizgi; kısmi iade ücret düzeltmesiyle iç içe, sonra eklenir. |
| 9 | Kim satış yapabilsin, kim iptal edebilsin? Veteriner satış yapsın mı? | Satış: Sekreter + yönetici düzeyi; iptal: yalnız Admin/Owner/ClinicAdmin; Veteriner şimdilik yok | ADR-012 ile uyumlu (resepsiyon ekler, yönetici düzeltir). |
| 10 | Satış için resmî bir belge (fatura, yazar kasa fişi) gerekiyor mu, yoksa bilgi amaçlı fiş yeter mi? | Bilgi amaçlı fiş; fatura/e-belge INT-007'de | Mevzuat doğrulanmadı; pilot klinik muhasebecisinin cevabı gerekir. |

## 8) Dilim önerisi ve tahmini zorluk (ölçülmedi)

Önce backend sözleşmesi, sonra kod, sonra frontend. Zorluk tahmini kod okumasından, **ölçülmedi**.

| # | Dilim | İçerik | Bağımlılık | Zorluk (ölçülmedi) |
|---|---|---|---|---|
| 0 | Karar toplantısı | Bölüm 7'deki 10 sorunun cevabı; gerekirse kısa ADR (ürün deposu) | — | Düşük |
| 1 | Backend sözleşmesi | `docs/DIRECT_SALE_API_CONTRACT.md`: varlıklar, uçlar, hata kodları, iptal kuralı, izinler, migration planı (kod yok) | 0 | Düşük–orta |
| 2 | Backend çekirdek | `Sale`/`SaleLine` varlığı + migration (uygulama için kullanıcı onayı ayrı), `CreateSaleCommand` (stok + ücret [+ ödeme] tek işlem), requestId, izinler/seed, listeleme/detay uçları | 1; `Charges`, `Payments`, `StockBatchResolver` hazır | Orta–yüksek (çok adımlı atomik komut, yarış testleri) |
| 3 | İptal/iade | `VoidSaleCommand` (ödeme iptali + ücret iptali + ters hareket; stoğa dön/zarar), 409 durumları | 2 | Orta–yüksek |
| 4 | Defter ve rapor | Kontrollü ürün alıcı bilgisi için mapper dalı (karar 3'e bağlı); satış listesi/rapor; Query DB gerekirse | 2, karar 3 | Orta |
| 5 | Frontend | Satış ekranı (sepet, ürün arama, parti seçimi, ödeme), satış listesi/detay, iptal diyaloğu, fiş yazdırma | 2–3 sözleşmesi | Orta–yüksek (ekran akışı ve barkod/arama hızı) |

Test odağı (çalıştırılmadı, planlanan): 1000/400/600 benzeri ödeme senaryosu satış üzerinden; çift istek; iki kullanıcı aynı son ürün; parti yetersizliği (hiçbir şey yazılmamalı); iptalde tam geri dönüş (stok, ücret, ödeme sayaçları); kontrollü ürün reddi; klinik kapsamı ve kiracı izolasyonu.

## Açık / doğrulanmadı

- Aşı Kayıt Defteri ve İlaç Kayıt Defteri okuyucularının kaynağı okunmadı (bölüm 1.5).
- Rol-izin eşlemesi satır aralıklarından okundu; `RolePermissionBindingsMatrixTests` çalıştırılmadı.
- Hiçbir test/derleme çalıştırılmadı; zorluk tahminleri ölçülmedi.
- Rakip ekranı (E-vet) incelenmedi; yalnız ürün deposundaki rakip analizi satırı kullanıldı.
- Perakende satışın mali belge zorunluluğu mevzuat açısından doğrulanmadı.
