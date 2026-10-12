# Aşama 5 kod keşfi — tedavi, reçete, klinik stok bağlantısı

Salt okunur keşif (kod yazılmadı, migration yok, test çalıştırılmadı, derleme yapılmadı). Taban: yerel `main` `ca254b5`. Kaynaklar: ana plan §10 (Aşama 5), `AJAN-KUYRUGU.md`, ADR-009/012, `FIN_DISCOVERY.md`, `ASAMA4_DISCOVERY.md`. Doğrulanmayan her şey "çıkarım" diye işaretli; son bölümde toplandı. Charge/Payment alanına (Dilim 5 düzeltme/iptal) dokunulmadı, yalnız okundu.

## 1. Mevcut durum

### 1.1 Varlıklar

| Varlık | Önemli alanlar | Bağlar | Notlar |
|---|---|---|---|
| `Treatment` | `Title`, `Description`, `Notes`, `TreatmentDateUtc`, `FollowUpDateUtc`, `CreatedAtUtc/UpdatedAtUtc` | `PetId`, `ClinicId`, `ExaminationId?` | Serbest metin. **Durum, hekim, ürün/miktar, `VisitId` yok.** Silme ucu yok, iptal kavramı yok. `RowVersion` yok. |
| `Prescription` | `Title`, `Content` (serbest metin), `Notes`, `PrescribedAtUtc`, `FollowUpDateUtc` | `PetId`, `ClinicId`, `ExaminationId?`, `TreatmentId?` | **Satır (ürün/doz/miktar) yok**, hekim yok, durum/iptal yok, `RowVersion` yok. |
| `Vaccination` | Aşı adı snapshot, `Status`, `AppliedAtUtc/DueAtUtc` | `PetId`, `ClinicId`, `ExaminationId?`, `VaccineDefinitionId` | Ürün/stok bağlantısı yok (yalnız katalog tanımı). Aşama 5 ana planında aşı "muayeneye bağlı başlatılır" listesinde, ama ürün tüketimi kapsamı belirtilmemiş (bkz. K7). |
| `Product` | `Name`, `Sku?` (tenant içinde benzersiz, filtreli), `Barcode?`, `Unit` (serbest metin ≤50), `UnitPrice`, `Currency`, `IsActive`, `ProductCategoryId?` | Tenant geneli katalog | **Ürün türü yok** (ilaç/sarf/hizmet/mama ayrımı yok), "stok takipli mi" bayrağı yok, birim dönüşümü yok, lot/son kullanma yok, ilaç etken madde/ruhsat alanı yok. |
| `ProductStock` | `QuantityOnHand`, `MinimumStockLevel`, `UpdatedAtUtc`, `RowVersion` | `TenantId+ClinicId+ProductId` **benzersiz** | Klinik bazlı snapshot. Optimistic concurrency (`rowversion`). Negatif olamaz (domain + `DecreaseQuantity` kontrolü). |
| `StockMovement` | `MovementType` (Initial/In/Out/Adjustment), `Quantity` (hep pozitif), `UnitCost?`, `Reason`, `ReferenceType?` (≤100), `ReferenceId?`, `OccurredAtUtc`, `CreatedByUserId`, `Notes` | `ProductId`, `ClinicId` | Append-only defter. `ReferenceType/ReferenceId` serbest metin/GUID: **koddan hiçbir yerde doldurulmuyor** (grep: yalnız `StockMovements` kendi komutu). Referans üzerinde **indeks ve benzersizlik yok**. Ters kayıt (reversal) tipi yok. |

`Treatment`/`Prescription` sınıflarında hekim (`VeterinarianId`) yok; "kim yazdı" bilgisi yalnız `Examination` tarafında (varsa). Yeni reçete satırı/ürün bağı hiç yok.

### 1.2 Uçlar ve izinler

| Alan | Uçlar | İzin | Roller (`RolePermissionBindings`) |
|---|---|---|---|
| Tedavi | `POST/PUT/GET/GET{id}` `/treatments` | `Treatments.Read/Create/Update` | Admin, Owner, ClinicAdmin, Veteriner: R/C/U. Sekreter: yalnız Read (çıkarım, satır 437). |
| Reçete | `POST/PUT/GET/GET{id}` `/prescriptions` | `Prescriptions.Read/Create/Update` | Tedavi ile aynı dağılım. |
| Ürün | `/products` (liste, detay, oluştur, güncelle, activate/deactivate, `/{id}/stocks`, `/{id}/stock-movements`) | `Products.Read/Create/Update/Deactivate`; hareket listesi `StockMovements.Read` | Admin/Owner/ClinicAdmin tam. Veteriner: `Products.Read`, `StockMovements.Read` (satır 405–406); **`StockMovements.Create` yok**. Sekreter: Read + `StockMovements.Create` (satır 453–455) — **hekimde yok, sekreterde var** (bkz. R6). |
| Stok | `/product-stocks` (liste, min seviye PUT) | `Products.Read` / `Products.Update` | — |
| Hareket | `GET/POST /stock-movements` | `StockMovements.Read/Create` | — |
| Aşı | `/vaccinations` | `Vaccinations.*` | — |

Silme (`DELETE`) ucu hiçbirinde yok. Tedavi/reçete için **iptal/geçersiz kılma ucu ve izni yok**.

### 1.3 Durum modeli

- **Treatment / Prescription / Vaccination (Status hariç): durum yok.** Kayıtlar her zaman geçerli sayılır; "yanlış girildi" işaretlemek için tek yol PUT ile metni değiştirmek (geçmişi tutulmaz).
- `Vaccination.Status` var (Scheduled/Applied vb.), ama stokla ilgisi yok.
- `StockMovement`: durum yok, append-only; düzeltme `Adjustment` (hedef miktar) ile yapılır.
- **Muayene kilidi tedavi/reçeteye uygulanmıyor** (kanıt: `Finalized` yalnız `Examination.cs`, `FinalizeExaminationCommandHandler`, muayene DTO/Spec/Config dosyalarında; `Treatments/`, `Prescriptions/`, `Vaccinations/` altında geçmiyor). Yani kesinleştirilmiş muayeneye `POST /treatments` ile `ExaminationId` verilerek yeni tedavi/reçete eklenebilir ve mevcut kayıt PUT ile değiştirilebilir (çıkarım: handler kodundan; HTTP ile denenmedi). Aşama 4 D1 ("kesinleştirince kilit, düzeltme yalnız ek not") ile çelişir (R1).

### 1.4 Examination / Visit / Charge bağlantıları

| Bağ | Tedavi | Reçete | Aşı | StockMovement |
|---|---|---|---|---|
| `ExaminationId` | opsiyonel (var ise klinik+hayvan uyumu kontrol edilir) | opsiyonel | opsiyonel | yok (`ReferenceType/Id` ile bağlanabilir ama kullanılmıyor) |
| `VisitId` | **yok** (Visit yalnız muayene üzerinden dolaylı) | yok | yok | yok |
| `TreatmentId` | — | opsiyonel (var ise tenant kontrolü; aynı hayvan/klinik uyumu **doğrulanmadı**, handler satır 135+ okundu, yalnız bulundu/tenant bakıldı) | — | — |
| `Charge` | **yok**: `Charge` yalnız `ClientId`, `PetId?`, `VisitId?`, `ExaminationId?` taşır; tedavi/ürün/stok kaynağı alanı yok. Ücret serbest metin `Description` + `Quantity` × `UnitPrice`. | yok | yok | yok |
| `Product` ↔ `Charge` | `Product.UnitPrice` var ama Charge'a otomatik öneri yok; ürün adı/birimi Charge'ta snapshot olarak tutulmuyor | | | |

Timeline (`Pets/Timeline/PetTimelineSources.cs`) tedavi ve reçeteyi okuyor (Aşama 4); ürün/stok bilgisi göstermiyor.

Tedavi/reçete komutları `ITransactionalRequest` **değil** (`CreateTreatmentCommand` yalnız `IRequest`), tek `SaveChangesAsync` yapıyorlar; stok bağı eklenince aynı transaction'da tüketim yazmak için işaretlenmeleri gerekir (`TransactionBehavior` yalnız bu arayüzü bekliyor). `CreateStockMovementCommand` `ITransactionalRequest`.

### 1.5 Mevcut stok davranışı (`CreateStockMovementCommandHandler`)

- `Initial`: stok satırı varsa `StockAlreadyInitialized`; yoksa oluşturur.
- `In`: satır yoksa oluşturur, varsa `IncreaseQuantity`.
- `Out`: `stock is null || QuantityOnHand < Quantity` → `InsufficientStock`; yoksa `DecreaseQuantity`. **Negatife düşme önleniyor** (hem handler hem domain).
- `Adjustment`: hedef miktar verilir, delta kadar hareket yazılır; delta=0 reddedilir.
- Eşzamanlılık: `ProductStock.RowVersion` concurrency token; `DbUpdateConcurrencyException` → `StockMovements.ConcurrencyConflict` (409). **İdempotency anahtarı yok**: aynı POST iki kez gelirse iki hareket yazılır (Charge'taki `RequestId` deseni stok için yok). `ReferenceType/Id` benzersiz değil, kontrol edilmiyor.
- Stok satırı yokken iki eşzamanlı `In/Initial`: `ProductStocks (Tenant,Clinic,Product)` benzersiz indeksi ikinci kaydı `DbUpdateException` ile düşürür; handler yalnız `DbUpdateConcurrencyException` yakalıyor → **olası 500** (çıkarım, kodu okuyarak; denenmedi).
- Kiracı + klinik kapsamı, aktif klinik bağlamı eşleşmesi (`ClinicContextMismatch`), pasif ürün reddi (`Products.Inactive`) uygulanıyor.

## 2. Ana plan §10 kabul kriterleri — eksik / risk

Kabul: *"Uygulanan ürün bir kez tüketilir ve kaynak işlemde görünür. Dış reçete stok değiştirmez. İptal/düzeltme sonrası stok ve ücret ilişkileri tutarlı kalır."*

| # | Kriter / karar | Mevcut | Eksik |
|---|---|---|---|
| K1 | Tedavi/reçete/aşı muayene ve hastaya bağlı, kimlik tekrar seçtirilmez | `ExaminationId`/`PetId` var, ama opsiyonel; `VisitId` yok | Bağlam zorunluluğu/varsayılanı sözleşmede; kesinleşmiş muayene kilidi (R1) |
| K2 | Serbest metin reçete korunur + ilk satır kapsamı: ürün/ilaç adı, kullanım talimatı, miktar | Yalnız `Content` metni | **Reçete satırı varlığı yok** (yeni tablo). Katalog ürününe bağlı mı serbest ad mı? (D2) |
| K3 | Klinik uygulama ≠ hastaya yazılan reçete; dış reçete stok düşmez | Kod zaten reçeteden stok düşmüyor (bağ yok). | Yeni kodda bu ayrımın bozulmaması: reçete satırı **hiçbir zaman** stok hareketi üretmemeli; uygulama ayrı kayıt (yeni varlık). Test: reçete oluşturma/güncelleme stok değiştirmez. |
| K4 | Stok tüketimi açık, yetkili işlemle; tekrar kaydetme aynı stoğu azaltmaz; düzeltme/geri alma kaynakla izlenir | `StockMovements.Create` ile manuel `Out`; idempotency yok; referans bağı kullanılmıyor | **Uygulama kaydı (administration/usage) varlığı** + benzersiz kaynak bağı + `requestId`. Geri alma için ters hareket tipi veya `Reversal` bağı yok. |
| K5 | Birim dönüşümü, lot/SKT kapsamı pilotla netleşir; tamamlanmış varsayılmaz | `Unit` serbest metin; dönüşüm/lot/SKT yok | Kapsam kararı (D4). İlk dilimde lot/SKT yok, birim = stok birimi öneri. |
| K6 | Ücret satırı klinik işlemden **önerilir**, onay olmadan sessiz ücret/tüketim yok | Charge ile klinik kayıt arasında yapısal bağ yok | Öneri uç(ları): uygulama kaydından "ücret önerisi" (`Product.UnitPrice` × miktar), `Charge`'a kaynak alanı veya ayrı bağ tablosu (Charge alanına dokunma = Dilim 5 ile çakışma riski, bkz. §3 bağımlılıklar). |
| K7 | Aşı: muayeneye bağlı | var | Aşıda ürün/stok tüketimi bu aşamada kapsamda mı? (D6) |
| K8 | Tanı/doz hekime ait, doz hesaplayıcı sonra | — | Kapsam dışı (not). |

### Riskler

- **R1 — Kilit boşluğu (yüksek):** Kesinleşmiş muayeneye tedavi/reçete eklenip değiştirilebiliyor (§1.3). Aşama 4 kararını bozar; stok tüketimi bunlara bağlanırsa kilitli muayenede stok değişebilir. Çözüm: ekleme/güncelleme handler'larında `exam.IsFinalized` reddi; kesinleşmiş muayenede düzeltme yolu (addendum benzeri veya tedavi iptali) ürün kararı (D3).
- **R2 — Çift tüketim (yüksek):** `StockMovement` idempotent değil ve kaynağa benzersiz bağlı değil. "Aynı uygulama iki kez kaydedildi/tekrar kaydedildi" ile çift düşüm mümkün. Çözüm: uygulama kaydı başına en fazla bir `Out` hareketi (benzersiz `(TenantId, ReferenceType, ReferenceId, MovementType)` ya da uygulama satırında `StockMovementId` FK + benzersiz), `RequestId` ile tekrar koruması (Charge desenindeki gibi).
- **R3 — Eşzamanlılık (orta):** Mevcut `rowversion` iki eşzamanlı `Out` için doğru (biri 409). Ama (a) satır yokken eşzamanlı oluşturma `DbUpdateException` olarak sızabilir (çıkarım); (b) stok düşümü ile uygulama kaydının **aynı transaction** içinde yazılması gerekli (komut `ITransactionalRequest` olmalı, tedavi/reçete komutları şu an değil); (c) 409'da kullanıcıya tekrar deneme akışı sözleşmede olmalı.
- **R4 — Geri alma / iptal (yüksek):** Tedavi ve reçetede iptal kavramı yok; uygulama kaydı yok. İptal edilen uygulamanın stoğu geri vermesi için ters hareket (örn. `In` + kaynak=uygulama, ya da yeni `Reversal` tipi) ve "bir kez geri alınır" kuralı gerek. PUT ile miktar değiştirilirse fark hareketi mi, iptal+yeni mi? (D3).
- **R5 — Ücretle ilişki (orta, Dilim 5 ile çakışabilir):** Ücret iptali/düzeltmesi (Dilim 5) tamamlandığında uygulama iptali ile ücret iptali bağlanıyor mu? Kural önerisi: uygulama iptali ücreti **otomatik iptal etmez**, "ilişkili ücret var, ayrıca düzeltin" uyarısı; ücret iptali stok geri vermez. Karar gerek (D5). Charge'ta `PetId/VisitId/ExaminationId` var; tedavi/ürün kaynağı alanı yok; ekleme migration + Charge sözleşme değişikliği gerektirir ve Dilim 5 ajanıyla koordine edilmeli.
- **R6 — Yetki tutarsızlığı (orta):** `StockMovements.Create` Sekreter'de var, Veteriner'de yok (`RolePermissionBindings` satır 405–406 vs 453–455, çıkarım). Klinik uygulamayı hekim yapacak; Veteriner stok düşürebilmeli ama genel "stok giriş/çıkış/düzeltme" yetkisi vermek geniş. Yeni dar izin önerisi (örn. `Treatments.Administer` / `ProductUsage.Create`), manuel `Out`'u (`StockMovements.Create`) uygulamadan ayırır. Yeni izin = seeder + özel rollere elle ekleme + "yalnız birleşik main'den seed" uyarısı.
- **R7 — Manuel Out ile çift sayma (orta):** Aynı ilaç hem uygulama kaydıyla hem elle `Out` ile düşülürse çift sayılır. UI/kural: uygulama kaydı varken manuel `Out` ayrı kalır; raporda kaynak (uygulama vs manuel) görünür. Referanssız `Out` hareketleri engellenmeyecek (mevcut davranış korunur).
- **R8 — Negatif stok politikası (ürün kararı):** Şu an `Out` yetersiz stokta reddedilir. Klinikte stok sayımı gerisinde kalabilir; hekim ilacı uygularken "stok yetersiz" ile bloke olabilir (D7).
- **R9 — Çok klinikli stok:** Stok klinik bazlı, ürün tenant genelinde. Uygulama hangi klinik stoğundan düşer = kaydın `ClinicId`'si (çıkarım: tedavi kaydı klinik taşıyor). Başka klinikte uygulama ve stok farkı: transfer kapsam dışı (ana plan "depo transferi" sınır dışı).
- **R10 — Veri bütünlüğü:** `Prescription.TreatmentId` için tedavi-hayvan-klinik uyumu handler'da net doğrulanmadı (yalnız bulunma/tenant); yeni dilimde test ile kapatılmalı.
- **R11 — Birim:** `Unit` serbest metin; reçete/uygulama miktarı ile stok birimi farklı olabilir (örn. ml vs şişe). İlk dilimde "uygulama miktarı = stok birimi" kuralı sözleşmeye yazılmazsa kullanıcı hatası sessizce yanlış düşüm yapar.
- **R12 — Denetim:** Stok/tedavi komutları audit'e bağlı değil (`IAuditableRequest` yalnız Auth/Appointment komutlarında görüldü; çıkarım). Düzeltme/geri alma "kim, ne zaman, neden" için alan taşımalı (Charge düzeltme desenindeki gibi gerekçe zorunlu).

## 3. Önerilen dilimler

Genel ilke: ADR-009/012 desenleri (kural domain'de, idempotent `RequestId`, `RowVersion`, 409 eşlemesi, yeni izin = seeder). Okuma → yazma sırası; migration'lı dilimler yalnız yerel DB, paylaşılan/üretim ayrı onay.

| # | Dilim | Kapsam | Migration | Bağımlılık | Paralel? |
|---|---|---|---|---|---|
| 0 | Kilit boşluğunu kapat | Tedavi/reçete/aşı create+update'te kesinleşmiş muayene reddi (`Treatments.ExaminationFinalized` vb.), `Prescription.TreatmentId` hayvan/klinik uyum kontrolü, testler | Yok | D3 (kesinleşmiş muayenede düzeltme yolu kararı) | Evet, 1 ile paralel |
| 1 | Stok güvenliği | `StockMovement`: `RequestId`/idempotency, kaynak bağ benzersizliği (indeks), `DbUpdateException` (benzersiz ihlali) eşlemesi, ters hareket için tip/ilişki, reçete/tedavi stok değiştirmez testleri | **Var** | — | Evet, 0 ile; Dilim 2 buna bağlı |
| 2 | Uygulama (administration) kaydı | Yeni varlık: muayene/hasta/klinik + tedavi (opsiyonel) + ürün + miktar + hekim + durum (Aktif/İptal) + `RequestId`; oluşturma stok `Out` hareketini **aynı transaction'da** yazar; iptal ters hareket yazar; yeni dar izin | **Var** | 1, D1–D5, D7 | 1'den sonra |
| 3 | Reçete satırları | `PrescriptionItem` (ürün adı serbest veya katalog, talimat, miktar); stok bağı **yok** (K3); mevcut serbest metin `Content` korunur | **Var** | D2 | **Dilim 2 ile paralel** (ayrı dal; yalnız `Prescription` ve yeni tablo) |
| 4 | Okuma/zenginleştirme | Muayene ilişkili özet (`related-summary`), timeline satırlarına ürün/miktar, geçmiş özet ek alanları, stok hareket listesinde kaynak gösterimi | Yok | 2, 3 | 2/3 sözleşmesi hazır olunca |
| 5 | Ücret önerisi | Uygulama kaydından "önerilen ücret satırı" (`Product.UnitPrice × miktar`); operatör onayıyla `Charge` oluşturma (mevcut Charge create uçlarıyla); kaynak alanı ihtiyacı varsa Charge'a **ayrı onaylı** değişiklik | Charge'a alan eklenirse var | 2, D5; **Charge/Dilim 5 (düzeltme/iptal) ajanıyla koordinasyon** | Dilim 5 (FIN) bitmeden Charge şemasına dokunma |
| 6 | Frontend | Uygulama diyaloğu, reçete satır editörü, stok uyarıları, kilit durumu | — | Backend sözleşmeleri | Backend sözleşme sonrası |

Paralellik özeti: **0 ∥ 1**; sonra **2 ∥ 3** (farklı varlıklar, farklı dallar; seed uyarısı: izin gerektiren dalda seed yalnız birleşik main'de); 4 ve 5 sonra; FE sözleşme sonrası. Migration gerektirenler: 1, 2, 3 (+ 5 olasılıkla). Migration'sız: 0, 4.

Dalları ayrı ayrı çalıştırırken `AppDbContextModelSnapshot` çakışması olacağı için migration'lı dilimler (1, 2, 3) sırayla birleştirilmeli ya da snapshot elle çözülmeli (Aşama 3/4'te de yaşandı; çıkarım).

## 4. Kullanıcıya sorulacak ürün kararları

**Karar durumu: D1–D9 önerildiği gibi Kabul edildi (kullanıcı, 2026-10-12).** Aşağıdaki "Öneri" sütunu kabul edilen karardır.

| # | Soru | Seçenekler | Öneri ve gerekçe |
|---|---|---|---|
| D1 | Klinik uygulama kaydının şekli | **a)** Yeni "Uygulama" varlığı (tedavi opsiyonel bağ; ürün+miktar). **b)** Tedaviye ürün satırı (Treatment satırları). **c)** Mevcut `StockMovement` + `ReferenceType` ile yetinmek | **a** — ana plan #3/#4 (reçeteyle uygulama ayrı), geri alma/tekrar koruması için kaynak varlığı gerekir; b tedaviyi şişirir, c idempotency/geri alma/ücret önerisi için yetersiz. |
| D2 | Reçete satırı | **a)** Katalog ürününe bağlı zorunlu. **b)** Serbest ad + opsiyonel katalog bağı. **c)** Yalnız serbest metin (satır yok) | **b** — dışarıdan temin edilen ilaç katalogda olmayabilir; K3 gereği stok hareketi hiç üretilmez. |
| D3 | Kesinleşmiş muayenede tedavi/reçete/uygulama ve iptal | **a)** Kesinleşmiş muayeneye ekleme/değişiklik yok; düzeltme **iptal+yeni kayıt** gerekçeli (yetkili). **b)** Yalnız addendum benzeri ek kayıt serbest, değişiklik yok. **c)** Serbest bırak | **a**, iptal yalnız Veteriner+yönetim, gerekçe zorunlu; iptal edilen uygulama stoğu geri verir. Kesinleşmiş muayeneye **yeni uygulama** için ek not benzeri akış gerekirse b'ye eklenir (soru: hekim kesinleştirdikten sonra unuttuğu ilacı nasıl girer?). |
| D4 | Birim, lot, son kullanma | **a)** İlk sürüm: uygulama miktarı = stok birimi; lot/SKT yok. **b)** Birim dönüşümü (ör. şişe→ml) ilk dilimde. **c)** Lot/SKT ilk dilimde | **a** — plan "pilot ürünleriyle netleşir"; hata riski R11 sözleşmede açıkça yazılır. Pilot ürün listesi gelirse b/c yeniden. |
| D5 | Ücret ilişkisi | **a)** Uygulama → ücret **önerisi**, operatör onaylar; uygulama iptali ücreti değiştirmez (uyarı gösterir), ücret iptali stok vermez. **b)** İptal zincirleme (uygulama iptali ilişkili ücreti de iptal). **c)** İlk sürümde ücret önerisi yok | **a** — plan #6 "sessiz ücret yok"; zincirleme iptal ADR-012 düzeltme yetki kurallarını (yönetici düzeyi) atlatır. Charge şeması Dilim 5 ile çakışmasın diye öneri uçları sonraya. |
| D6 | Aşıda stok tüketimi | **a)** İlk sürüm aşı ürün tüketmez (yalnız katalog). **b)** Aşı uygulaması da Uygulama varlığı üzerinden stok düşer | **a** önce, **b** sonra — aşı katalogu (`VaccineDefinition`) ile ürün (`Product`) ayrı; birleştirmek kapsam büyütür. |
| D7 | Stok yetersizken uygulama | **a)** Engelle (mevcut `Out` davranışı). **b)** Uyarıyla izin ver, stok negatife gitmesin diye 0'a sabitle + işaret. **c)** Negatif stoka izin | **a** başlangıç (veri güvenilirliği, mevcut kodla uyumlu). Sahadan "ilaç var ama sistemde yok" şikâyeti gelirse hekimin gerekçeyle geçiş izni (b benzeri) ayrı karar. |
| D8 | Yeni izin kümesi | **a)** Dar izin (`ProductUsage.Create`/`Treatments.Administer`) Veteriner+yönetim; `StockMovements.Create` manuel için kalır. **b)** Mevcut `StockMovements.Create` Veteriner'e de ver | **a** — R6; iptal için ayrı `...Correct` izni. Özel roller elle alır (deploy notu). |
| D9 | Stok eşiği (düşük stok) uyarısı | Mevcut `MinimumStockLevel` ile listede `isBelowMinimum` var. Uygulama anında uyarı gösterilsin mi? | Gösterilsin (bilgi), engellemesin. Düşük stok bildirimi/e-posta sonraki aşama. |

## 5. Doğrulanmayanlar

- Hiçbir test çalıştırılmadı, derleme yapılmadı, DB'ye bağlanılmadı; HTTP ile uçlar denenmedi.
- Kesinleşmiş muayeneye tedavi/reçete eklenebildiği (R1) yalnız handler kodunun okunmasından çıkarım; Finalized kontrolünün başka bir katmanda (validator/behavior/spec) yapılmadığı grep ile `Treatments/`, `Prescriptions/` altında "Finalized" geçmemesine dayanıyor.
- Eşzamanlı `In` ile benzersiz indeks ihlalinin 500'e dönüştüğü (R3a) kodu okuyarak çıkarıldı; genel bir `DbUpdateException` eşlemesinin başka katmanda olup olmadığı bakılmadı.
- Rol/izin dağılımı `RolePermissionBindings.cs` grep satırlarından okundu; rol başlık-satır eşleşmesi (özellikle Veteriner/Sekreter bloğu) satır aralıklarından çıkarıldı, ilgili test (`RolePermissionBindingsProductsStockTests`) çalıştırılmadı.
- `Prescription.TreatmentId` hayvan/klinik uyum kontrolü handler'ın ilk bulunan kısmında görülmedi; tamamı satır satır okunmadı.
- `IAuditableRequest` kapsamı (tedavi/stok denetimi yok) yalnız grep çıktısının ilk 5 satırına dayanıyor; tam liste alınmadı.
- Query DB / read model etkisi: ürün/stok/tedavi/reçete için read model olmadığı varsayıldı (Aşama 4 keşfine dayanarak), bu keşifte yeniden doğrulanmadı.
- Frontend durumu bu keşifte incelenmedi.
- Pilot kliniğin gerçek ürün listesi, birim ve lot/SKT ihtiyacı bilinmiyor (D4 bu yüzden açık).
- Provet/DaySmart/E-Vet rakip davranışları ana plandan alındı; yeniden doğrulanmadı.
