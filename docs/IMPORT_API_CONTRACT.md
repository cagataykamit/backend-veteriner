# Veri içe aktarma (müşteri + hasta + aşı geçmişi) API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: ADR-014 K8 (ürün deposu), `ASAMA9_READINESS.md` Dilim 7. Tasarım gerekçesi, akış, riskler ve kullanıcı kararları: `ASAMA9_IMPORT_DESIGN.md`. Tüm uçlar `api/v1` altında, JSON (dosya yükleme ve indirme hariç), standart `ProblemDetails` + `extensions.code` hata zarfı, sayfalama mevcut liste uçlarındaki biçimde.

> **Durum:** Kararlar kesinleşti: ADR-019 (ürün deposu, K1–K12 önerilen seçeneklerle kabul). `[K#]` işaretleri o karara bağlıdır. **Uygulanan dilimler: B1 (temel)** — parti kaydı, durum makinesi, izinler, migration `AddDataImportBatches`; **B2 (okuma ve eşleme)** — XLSX/CSV okuyucu, dosya yükleme, sütun/değer eşleme, şablon (yeni migration yok); **B3 (doğrulama / kuru çalıştırma)** — satır kuralları, mükerrer ve çip denetimi, hatırlatma etkisi, preview/rows/errors uçları (yeni migration yok). B4–B5 uçları aşağıda **taslak**tır; dilim başında kesinleşir (bölüm 0).

## 0) Dilim durumu ve frontend için değişenler

| Dilim | Uç / yüzey | Durum |
|-------|------------|-------|
| B1 | `POST /imports/batches` (dosyasız iskelet), `GET /imports/batches`, `GET /imports/batches/{id}`, `DELETE /imports/batches/{id}`; izinler; durum makinesi; `Vaccination.ImportBatchId` | **Uygulandı** |
| B2 | `POST /imports/batches` dosya alanları (`file`, `customersFile`…), `GET /imports/template`, `PUT .../mapping`, yanıtta `tables[]` | **Uygulandı** |
| B3 | `POST .../validate`, `GET .../preview`, `GET .../rows`, `GET .../errors` | **Uygulandı** |
| B4 | `POST .../commit`, `POST .../release-reminders`, `progress`, hatırlatma dışlaması | Taslak |
| B5 | `GET .../rollback-preview`, `POST .../rollback`, 30 gün temizlik, `Expired` işi | Taslak |

**Frontend için değişenler (B1):**
- Yeni izinler `Imports.Read`, `Imports.Manage`, `Imports.Commit` yalnız yönetici düzeyine (Admin, Owner, ClinicAdmin) bağlanır. Seed çalışıp kullanıcı çıkış-giriş yapana kadar bu uçlar `403` döner; menü öğesi izinle gizlenmelidir.
- `POST /imports/batches` B1'de **dosyasız** çalışır (yalnız `requestId`, `name`, `clinicId`); parti `Uploaded` durumunda, satırsız oluşur. Gövde zaten `multipart/form-data`'dır; B2 aynı uca dosya alanlarını ekler (alan adları değişmez). Sihirbaz (F1) B2–B3 sözleşmesi sabitlenene kadar bağlanmamalıdır.
- Parti yanıtındaki tarih alanları UTC, `status` metin (`"Uploaded"` …), sayaçlar `summary.<tablo>` altında (B1'de hepsi 0).
- Mevcut müşteri/hasta/aşı uçlarının sözleşmesi **değişmez** (`Vaccination.ImportBatchId` yalnız veritabanında; okuma DTO'larına eklenmedi).

**Frontend için değişenler (B2):**
- `POST /imports/batches` artık **dosya ister** (`file` XLSX veya `customersFile`/`petsFile`/`vaccinationsFile` CSV); dosyasız iskelet çağrısı `400 Imports.EmptyFile` döner. `requestId`, `name`, `clinicId` alanları değişmedi. Yeni isteğe bağlı alanlar `encoding`, `delimiter`. Yanıt (201/200) parti nesnesine `tables[]` (algılanan sütunlar + önerilen eşleme) ve yalnız yükleme yanıtında `warnings[]` ekler (bölüm 7.2).
- `GET /imports/batches/{id}` ve `PUT .../mapping` yanıtı da `tables[]` taşır (`columns[].mapped` = kayıtlı eşleme, yoksa `null`); parti listesinde `tables` ve `warnings` `null`'dır.
- `PUT /imports/batches/{id}/mapping` kullanılabilir (bölüm 7.3). `GET /imports/template` kullanılabilir (bölüm 7.1; `Imports.Read`).
- Aşı tablosu içeren partide `clinicId` zorunludur (`400 Imports.ClinicRequired`).
- Aynı dosya ikinci kez yüklenirse `409 Imports.DuplicateFile` + `extensions.batchId` (parti çağıranın görebildiği bir partiyse).
- Tür/ırk/renk/aşı **değer eşleme çözücüsü** B2'de iç servis olarak vardır; kullanıcıya dönük "eşlenemeyen değerler" listesi B3 `preview` ile gelir. `PUT mapping` yalnız hedefin varlığını/aktifliğini doğrular.
- Taslak silinince (`DELETE`) hazırlık satırları da silinir; parti `payloadPurgedAtUtc` ile görünür ve `tables[].columns` boş döner.
- Aktarma kapsamı yalnız veri okumadır: bu dilimde müşteri/hasta/aşı kaydı **oluşmaz**; doğrulama (B3) ve uygulama (B4) sonraki dilimlerdir.

**Frontend için değişenler (B3):**
- `POST /imports/batches/{id}/validate` **senkron** çalışır ve `202` yerine `200` + güncel parti nesnesi döner (`status` = `Validated` | `ValidationFailed`); istek bitince sonuç hazırdır, yoklama gerekmez (bölüm 7.4). Bu yüzden `progress` alanı B3'te yoktur.
- Doğrulama bitince `summary.<tablo>` `valid/warning/error/willCreate/willMatch/willSkip` alanları dolar. Değişmezler: `valid + warning + error = total`; `willCreate + willMatch + willSkip = valid + warning`.
- `GET .../preview`, `GET .../rows` ve `GET .../errors` doğrulaması bitmiş partide çalışır (`ValidationFailed` dahil; dosya düzeyi nedenler `blockers` içindedir). Eşleme değişince parti `Mapped`'a düşer ve bu üç uç `409 Imports.InvalidState` döner (sonuç güncel değil).
- Satır sorunları `{ severity: "Info|Warning|Error", code, field, message, value? }` biçimindedir; kural kodlarının tam listesi ve davranışı bölüm 6.4'te, hata kodları bölüm 8'dedir.
- `reminderImpact` yalnız bilgidir: içe aktarılan aşılar varsayılan olarak hatırlatma dışıdır (bölüm 6.3); sayılar çağrı anındaki saate göre hesaplanır.
- `GET .../errors` hata raporunu `xlsx`/`csv` dosya olarak indirir. Satır verisi temizlenmiş partide `rows`/`errors` `410 Imports.PayloadPurged` döner; `preview` yalnız sayıları verir.
- B3 ürün tablolarına (müşteri/hasta/aşı) **hiçbir şey yazmaz**; uygulama (B4), geri alma ve temizlik işi (B5) sonraki dilimlerdir. B3'te yeni migration yoktur.

## 1) Kapsam ve sınırlar

- **Var:** Müşteri (`Client`), hasta (`Pet`) ve **uygulanmış** aşı geçmişi (`Vaccination`, `Status = Applied`).
- **Yok (ilk sürüm):** muayene, tedavi, reçete, lab, yatış, randevu, geliş, ücret, ödeme, stok, uygulama kaydı (`Administration`; stok düşer, bu yüzden bilinçli olarak dışarıda), planlı (`Scheduled`) aşı.
- Kiracı yalnızca JWT/bağlamdan çözülür; istekte `tenantId` yoktur. Müşteri ve hasta **kiracı geneli**dir (klinik filtresi yok); aşı kaydı için tek bir `clinicId` partide seçilir (bölüm 4.3).
- İçe aktarma, mevcut oluşturma yollarının kurallarını **yeniden yazmaz**: telefon normalizasyonu (`TurkishMobilePhone`), e-posta/ad kuralları, müşteri ve hayvan mükerrer kontrolü, tür/ırk doğrulaması, abonelik yazma koruması, outbox olayları aynen uygulanır. Yalnız aşı **tarih penceresi** içe aktarmaya özgüdür (bölüm 6.3).

## 2) Yetki (yeni izinler) `[K8]`

Seed ile gelir (`PermissionCatalog` + `RolePermissionBindingSeeder`); varsayılan olarak yalnız **yönetici düzeyi** rollere (`Admin`, `Owner`, `ClinicAdmin`; `*.Correct` izinleriyle aynı hiza) bağlanır. `Veteriner` ve `Sekreter` almaz. Mevcut özel roller elle almalıdır. Seed yalnız birleşik `main`'den çalıştırılır; sonrasında çıkış-giriş gerekir.

| İzin | Uçlar |
|------|-------|
| `Imports.Read` | Parti listesi/ayrıntısı, önizleme, satırlar, hata raporu, şablon indirme |
| `Imports.Manage` | Parti oluşturma (yükleme), eşleme, doğrulama, taslağı silme |
| `Imports.Commit` | Onaylayıp uygulama, hatırlatmaları açma, geri alma |

Ek zorunlu izinler (yetki yükseltmeyi önlemek için): `commit` çağıranın `Clients.Create` (müşteri içeren parti), `Pets.Create` (hasta içeren parti), `Vaccinations.Create` (aşı içeren parti) izinlerine de sahip olmasını gerektirir; biri eksikse `403`. Başka kiracının partisi `404` döner.

## 3) Yaşam döngüsü

Parti durumları (`status`): `Uploaded` → `Mapped` → `Validating` → `Validated` | `ValidationFailed` → `Committing` → `Committed` | `CommitFailed`; `Committed` → `RollingBack` → `RolledBack` | `PartiallyRolledBack` | `RollbackFailed`; taslak aşamalarından `Discarded`; 7 gün hareketsiz taslak `Expired`.

**Geçiş tablosu (B1, domain'de tek yer: `ImportBatch`).** Tabloda olmayan her geçiş `409 Imports.InvalidState` döner; iki istek aynı partiyi aynı anda değiştirirse ikincisi `409 Imports.ConcurrencyConflict` (rowversion).

| İşlem | Kaynak durum(lar) | Hedef |
|-------|-------------------|-------|
| Oluştur | — | `Uploaded` |
| Eşleme kaydet | `Uploaded`, `Mapped`, `Validated`, `ValidationFailed` | `Mapped` (önceki `validationId` geçersizleşir) |
| Doğrulama başlat | `Mapped`, `Validated`, `ValidationFailed`; kirası dolmuş `Validating` (çökme sonrası devralma) | `Validating` |
| Doğrulama bitti | `Validating` | `Validated` (yeni `validationId`) \| `ValidationFailed` |
| Uygula | `Validated` + güncel `validationId` (aksi `Imports.ValidationStale`) | `Committing` |
| Uygulama bitti | `Committing` | `Committed` \| `CommitFailed` |
| Geri al | `Committed`, `PartiallyRolledBack`, `CommitFailed`, `RollbackFailed` (yeniden deneme) | `RollingBack` |
| Geri alma bitti | `RollingBack` | `RolledBack` \| `PartiallyRolledBack` \| `RollbackFailed` |
| Hatırlatmaları serbest bırak | `Committed`, `PartiallyRolledBack`; yalnız bir kez | durum değişmez, `remindersReleasedAtUtc` dolar |
| Taslağı sil | `Uploaded`, `Mapped`, `Validated`, `ValidationFailed` | `Discarded` |
| Süre doldu | taslak durumları + son hareketten ≥ 7 gün | `Expired` |

Taslak = `Uploaded`, `Mapped`, `Validating`, `Validated`, `ValidationFailed` (açık taslak sınırı için sayılır). Aktif iş = `Validating`, `Committing`, `RollingBack`. Son durumlar: `Committed` (geri alınabilir), `RolledBack`, `Discarded`, `Expired`. Kiracı başına en fazla **5 açık taslak** (`409 Imports.TooManyOpenDrafts`; uygulama düzeyi sınır, eşzamanlı iki istekte 6. oluşabilir).

- Doğrulama B3'te **senkrondur** (`200`, bölüm 7.4); uygulama ve geri alma **arka plan işidir** (`202 Accepted`); istemci `GET /imports/batches/{id}` ile durumu ve ilerlemeyi yoklar (önerilen aralık 2 sn).
- Aynı kiracıda aynı anda **tek** aktif iş (`Validating`, `Committing`, `RollingBack`) olabilir; ikincisi `409 Imports.AnotherJobRunning`.
- Doğrulama tekrarlanabilir (eşleme değişince); her doğrulama yeni bir `validationId` üretir. `commit` yalnız en son `validationId` ile kabul edilir.

## 4) Girdi biçimi

### 4.1 Dosya biçimi

| Konu | Kural |
|------|-------|
| Biçim | `.xlsx` (tercih) ve `.csv`. `.xls`, `.xlsm` (makro), parolalı dosya reddedilir (`Imports.UnsupportedFile`). İçerik uzantıya değil **imzaya** göre doğrulanır (xlsx = zip `PK`). |
| Tablolar | Üç mantıksal tablo: **müşteri**, **hasta**, **aşı**. XLSX'te tek dosyada üç sayfa (`Musteriler`, `Hayvanlar`, `Asilar`); CSV'de her tablo ayrı dosya. Parti en az bir tablo içerir; hasta tablosu müşteri tablosuna veya kiracıdaki mevcut müşterilere, aşı tablosu hasta tablosuna veya mevcut hastalara bağlanır. |
| CSV kodlama | UTF-8 (BOM'lu/BOM'suz). BOM yoksa ve UTF-8 geçersizse Windows-1254 denenir; istek `encoding` alanıyla zorlanabilir. Karakter bozulması (`�`) varsa `Imports.EncodingSuspect` uyarısı. |
| CSV ayırıcı | Otomatik: `;` `,` `\t` (Türkçe Excel `;` üretir); `delimiter` ile zorlanabilir. Tırnaklı alanlar RFC 4180. |
| Başlık | İlk dolu satır başlıktır. Başlık eşleştirme: büyük/küçük harf ve Türkçe harf farkı yok sayılır (`Telefon`, `telefon`, `TELEFON`, `Mikrocip` = `Mikroçip`). |
| Boyut | Dosya ≤ **10 MB**; tablo başına ≤ **10.000** veri satırı, parti toplamı ≤ **25.000** (`Imports.FileTooLarge`, `Imports.TooManyRows`). Sayılar pilot hacmine göredir (5–10 bin hasta); değerler yapılandırılabilir. |
| Boş satırlar | Tamamen boş satırlar sayılmaz ve yok sayılır. |
| Formül/Biçim | XLSX'te hücrenin **görüntülenen/önbellek değeri** okunur; formül çalıştırılmaz. Dış bağlantılar yok sayılır. |

**B2 okuyucu ayrıntıları (uygulandı):**

| Konu | Kural |
|------|-------|
| Alan–biçim eşleşmesi | `file` yalnız `.xlsx`; `customersFile`/`petsFile`/`vaccinationsFile` yalnız `.csv`. `file` ile CSV alanları birlikte verilemez. Hiç dosya yoksa `Imports.EmptyFile`, boş (0 bayt) dosya `Imports.EmptyFile`. Başka uzantı (`.xls`, `.xlsm`, `.txt`…) `Imports.UnsupportedFile`. |
| İmza / MIME | İçerik uzantıya değil imzaya bakılır: xlsx = zip (`PK\x03\x04`); OLE2 imzası (`D0 CF 11 E0`: eski `.xls` veya parolalı xlsx) reddedilir; CSV'de zip/OLE/PDF/EXE imzası veya NUL bayt (ilk 8 KB) reddedilir. `Content-Type` güvenilmez ama verilmişse izinli kümede olmalıdır (xlsx: `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/octet-stream`, `application/zip`, `application/x-zip-compressed`; csv: `text/csv`, `application/csv`, `text/plain`, `text/comma-separated-values`, `application/vnd.ms-excel`, `application/octet-stream`); aksi `Imports.UnsupportedFile`. |
| XLSX güvenliği | Dosya belleğe alınmadan önce zip denetlenir: giriş sayısı ≤ 1.000, **gerçek** açılmış boyut ≤ 100 MB (başlıktaki boyuta güvenilmez; zip bombası), `vbaProject.bin` (makro) varsa veya XML parçalarında `<!DOCTYPE`/`<!ENTITY` varsa (XXE) `Imports.UnsupportedFile`. Dış bağlantı parçaları okunmaz. Çalışma kitabında en çok 20 sayfa. |
| XLSX sayfaları | Sayfa adları katlanarak (bölüm 5 katlaması) tanınır: `Musteriler`/`Musteri`/`Customers` = müşteri; `Hayvanlar`/`Hayvan`/`Hastalar`/`Hasta`/`Pets` = hasta; `Asilar`/`Asi`/`Vaccinations` = aşı. Tanınmayan sayfalar (örn. `Aciklama`) yok sayılır; hiçbiri tanınmazsa `Imports.UnsupportedFile` (tek sayfalı düz liste ilk sürümde yok, K1). Yalnız başlığı olan, veri satırı olmayan sayfa atlanır ve `Imports.TableEmpty` uyarısı verilir; hiçbir tabloda veri yoksa `Imports.EmptyFile`. |
| Tavanlar | Başlık sütunu ≤ 60; tek hücre ≤ 10.000 karakter; tablo başına ≤ 10.000, toplam ≤ 25.000 veri satırı (boş satırlar sayılmaz). Aşımda dosya reddedilir: boyut/hücre `Imports.FileTooLarge`, satır `Imports.TooManyRows`, sütun `Imports.UnsupportedFile`. Satır sayısı okunurken sayılır, aşıldığı anda okuma durur. Tavanlar `Imports:Limits` yapılandırmasıyla değişir; istek gövdesi üst sınırı (3 dosya için 33 MB) aşılırsa taşıma katmanı `413` döner. |
| Formül / enjeksiyon | Formül hücresinde yalnız dosyadaki önbellek değeri okunur (yoksa boş); formül **hiçbir zaman** değerlendirilmez. `=`, `+`, `-`, `@` ile başlayan metin hücresi (örn. `+90 532…`, `-5`) düz **metin** olarak, olduğu gibi saklanır; yorumlanmaz. Dışarı yazılan dosyalarda (şablon, hata raporu) bu karakterlerle başlayan metin hücrelerinin başına `'` eklenir. |
| Hücre değerleri | Satır kaynağı `başlık → metin` sözlüğü olarak saklanır (`SourceJson`). XLSX: metin olduğu gibi; sayı değişmez kültürle (bilimsel gösterim yok, `5321234567`); tarih hücresi `yyyy-MM-dd` (saat varsa `yyyy-MM-ddTHH:mm:ss`); mantıksal `true`/`false`; hata hücresi (`#N/A`) boş. Yalnız boşluk içeren hücre boştur (`null`). |
| Başlık sütunları | İlk dolu satır başlıktır. Başlığı boş sütun: verisi yoksa yok sayılır, varsa `Sütun N` adı verilir. Yinelenen başlık `Ad (2)`, `Ad (3)` olur. Başlık 200 karaktere kırpılır. |
| Satır numarası | Kullanıcıya gösterilen `rowNumber`: XLSX'te Excel satır numarası; CSV'de kayıt sırası (başlık dahil, boş kayıtlar sayılır). |
| CSV kodlama | BOM varsa (UTF-8, UTF-16) o kullanılır. BOM yoksa katı UTF-8 denenir, geçersizse Windows-1254. `encoding` alanıyla zorlanırsa (`utf-8`, `windows-1254`) çözümleme bozulmuşsa (`�`) `Imports.EncodingSuspect` uyarısı verilir (hata değil). |
| CSV ayırıcı | `delimiter` alanı yoksa ilk 20 kayıtta `;` `,` sekme adayları denenir; başlıkla aynı alan sayısına sahip kayıt sayısı en yüksek olan seçilir (eşitlikte `;`, `,`, sekme sırası). `delimiter` değerleri: `;` `,` `\t` (`semicolon`, `comma`, `tab` de kabul). Tırnaklı alanlar RFC 4180 (tırnak içinde ayırıcı, satır sonu ve `""`). |
| İçerik özeti | Tek `file`: dosyanın SHA-256'sı (küçük harf hex). CSV alanları: `customers=<h>;pets=<h>;vaccinations=<h>` (verilmeyen alan atlanır) metninin SHA-256'sı. `encoding`/`delimiter` özete girmez. |

### 4.2 Şablon (yönetici indirir)

`GET /imports/template?format=xlsx` (üç sayfa + `Aciklama` sayfası: kolon açıklamaları, örnek satır, kabul edilen değerler) ve `?format=csv&table=customers|pets|vaccinations`. Şablon başlıkları aşağıdaki **kanonik başlıklardır**; ayrıca kanonik başlıklar için kabul edilen takma adlar (`aliases`) sütun eşleme önerisinde kullanılır.

**B2 şablon içeriği (uygulandı):** XLSX'te `Musteriler`, `Hayvanlar`, `Asilar` sayfaları yalnız **kanonik başlık satırını** taşır (sabit satır, filtre yok); örnek veri **veri sayfalarında yoktur** (şablon doldurulmadan yüklenirse sahte kayıt oluşmasın). `Aciklama` sayfası her kanonik başlık için tablo, zorunluluk, kural açıklaması ve örnek değer verir. Telefon, mikroçip, numara ve tarih sütunları metin biçimlidir (baştaki 0 ve 15 haneli çip kaybolmasın; tarih yerel biçime çevrilmesin). `format=csv` `table` ister; dosya UTF-8 BOM'lu, `;` ayırıcılı, yalnız başlık satırı. Dosya adları `vetinity-iceaktar-sablon.xlsx` ve `vetinity-iceaktar-<musteriler|hayvanlar|asilar>.csv`. Eksik/geçersiz `format` veya `table` `400 Validation.FluentValidation`.

**Sütun eşleme (mapping) `[K1]`:** Pilot kliniğin gerçek dosya biçimi bilinmiyor. Bu yüzden iki yol birlikte desteklenir: (a) şablona uyan dosya yüklenir, eşleme otomatik; (b) kliniğin kendi dosyası yüklenir, başlıklar otomatik önerilir (kanonik ad + takma ad, katlanmış karşılaştırma) ve kullanıcı eşlemeyi düzeltir. Kaynak sütun birden fazla kanonik alana eşlenemez; zorunlu kanonik alan eşlenmemişse `validate` reddedilir (`Imports.MappingIncomplete`). Eşleme parti kaydında saklanır (`PUT .../mapping`).

### 4.3 Kanonik alanlar

**Müşteri tablosu (`customers`)**

| Kanonik başlık | Zorunlu | Kural |
|----------------|---------|-------|
| `MusteriNo` | Hayır* | Kliniğin eski sistemindeki müşteri referansı. Hasta tablosundan bağlantı için tercih edilen anahtar. Dosya içinde benzersiz olmalı (aksi `Imports.DuplicateReference`). *Hasta tablosu müşteriye `MusteriNo` ile bağlanacaksa zorunlu. |
| `AdSoyad` | **Evet** | 2–300 karakter (mevcut müşteri kuralı); baştaki/sondaki boşluk kırpılır, art arda boşluk teke iner. |
| `Telefon` | Hayır (önerilir) | Türkiye cep standardı: `05XXXXXXXXX`, `5XXXXXXXXX`, `+90 5XX XXX XX XX`, `905XXXXXXXXX` → `905XXXXXXXXX`. Sabit hat/yabancı/geçersiz → **uyarı**, telefon boş aktarılır, özgün değer raporda görünür `[K5]`. Excel sayı hücresindeki `5321234567` (baştaki 0 kaybı) kabul edilir. |
| `Eposta` | Hayır | Mevcut müşteri e-posta kuralı (≤ 320, geçerli adres, küçük harfe çevrilir). Geçersiz → **uyarı**, e-posta boş aktarılır. |
| `Adres` | Hayır | ≤ 500 karakter; fazlası hata. |

**Hasta tablosu (`pets`)**

| Kanonik başlık | Zorunlu | Kural |
|----------------|---------|-------|
| `MusteriNo` **veya** (`SahipAdSoyad` + `SahipTelefon`) | **Evet** (biri) | Sahibe bağlantı. Önce `MusteriNo` (dosya içi), yoksa sahip adı + telefon ile dosyadaki müşteri tablosu, sonra kiracıdaki mevcut müşteri aranır. Bulunamayan → hata `Imports.OwnerNotFound`. |
| `HayvanNo` | Hayır* | Eski sistemdeki hasta referansı; aşı tablosundan bağlantı için tercih edilen anahtar. *Aşı tablosu `HayvanNo` ile bağlanacaksa zorunlu. |
| `HayvanAdi` | **Evet** | 1–200 karakter. |
| `Tur` | **Evet** | Katalogdaki aktif `Species` ile eşleşir (bölüm 5). Eşleşmeyen → değer eşleme gerekir. |
| `Irk` | Hayır | Türe ait `Breed` ile eşleşirse `BreedId`; eşleşmezse serbest metin `Breed` (≤ 150), **hata değil**. |
| `Cinsiyet` | Hayır | `Erkek/E/Male/M` → `Male`; `Disi/Dişi/D/Female/F` → `Female`; boş → null. Tanınmayan → uyarı, null. |
| `DogumTarihi` | Hayır | Tarih biçimleri bölüm 4.4. Gelecekte olamaz (mevcut kural) → hata. |
| `Kisir` | Hayır | `Evet/E/Kısır/Kısırlaştırılmış/1/True` → true; `Hayir/H/0/False`/boş → false. |
| `Mikrocip` | Hayır | Boşluk, `-`, `.` temizlenir; yalnız rakam kalmalı (≤ 50). 15 hane değilse uyarı (eski çipler), rakam dışı karakter varsa hata `Imports.MicrochipInvalid`. Excel'in bilimsel gösterimi (`9,85121E+14`) hata `Imports.MicrochipScientificNotation` (ipucu: hücreyi metin yapın). |
| `Renk` | Hayır | `PetColor` ile eşleşirse `ColorId`; eşleşmezse **veri kaybolmasın diye** `Notes` başına `Renk: <değer>` eklenir (uyarı yok). |
| `Agirlik` | Hayır | Ondalık virgül/nokta, `kg` eki atılır; 0 ≤ değer < 10.000, 2 ondalık (mevcut `decimal(6,2)`). |
| `KarneNo` | Hayır | `PassportOrTagNumber` (≤ 50). |
| `Not` | Hayır | `Notes` (≤ 2000, `Renk` satırıyla birlikte). Aşımda hata. |

**Aşı tablosu (`vaccinations`)**

| Kanonik başlık | Zorunlu | Kural |
|----------------|---------|-------|
| `HayvanNo` **veya** (`SahipAdSoyad` + `SahipTelefon` + `HayvanAdi` + `Tur`) | **Evet** (biri) | Hastaya bağlantı; sırayla dosyadaki hasta, sonra kiracıdaki mevcut hasta. Bulunamayan → hata `Imports.PetNotFound`. |
| `AsiAdi` | **Evet** | `VaccineDefinition` ile eşleşir (bölüm 5). Eşleşmeyen → değer eşleme gerekir. |
| `UygulamaTarihi` | **Evet** | Tarih biçimleri bölüm 4.4. Gelecekte olamaz; 1990-01-01'den eski olamaz (hata `Imports.DateOutOfRange`); hastanın doğum tarihinden önceyse **uyarı**. |
| `SonrakiAsiTarihi` | Hayır | `UygulamaTarihi`'nden **sonra** olmalı (mevcut `DueAtMustBeAfterAppliedAt` kuralı); yoksa/önceyse hata. Gelecek ≤ 5 yıl (mevcut `VaccinationDueAtWindow`). Geçmişte kalan sonraki tarih **geçerlidir** (geçmiş geçmiş kalır; hatırlatma üretmez). |
| `Seri`, `Uygulayan`, `Not` | Hayır | Aşı varlığında ayrı alan olmadığı için `Notes`'a `Seri: …; Uygulayan: …; <Not>` olarak birleştirilir (≤ 4000). |

Aşı için klinik: parti oluştururken `clinicId` verilir (aşı tablosu varsa zorunlu); çağıranın o kliniğe yazma erişimi olmalı (`VaccinationClinicWriteScope`) ve aktif klinik bağlamıyla çelişmemeli. Tüm aşı satırları o kliniğe yazılır. `ExaminationId` her zaman null'dır.

### 4.4 Tarih ve saat

Kabul edilen yalnız-tarih biçimleri (gün öncelikli, belirsizlik yok): `gg.aa.yyyy`, `gg/aa/yyyy`, `gg-aa-yyyy`, `yyyy-aa-gg` (ISO), Excel tarih hücresi ve Excel seri sayısı. Tek haneli gün/ay kabul edilir (`3.5.2024`). **Reddedilen:** iki haneli yıl (`3.5.24`), yalnız ay/yıl (`05.2024`), ay adı, aa/gg/yyyy (ABD) sırası. Saat bilgisi varsa yok sayılır (uyarı yok).

Saklama: tarih-saat alanları (`Vaccination.AppliedAtUtc`, `DueAtUtc`) için yalnız-tarih değeri **Europe/Istanbul 12:00** olarak yorumlanıp UTC'ye çevrilir (UTC kayması günü değiştirmesin diye öğlen seçilir; `Turkey Standard Time` yedek kimliği mevcut randevu kodundaki gibi). `Pet.BirthDate` zaten `DateOnly`'dir.

## 5) Tür / ırk / aşı / renk eşleme

Eşleme anahtarı katlanmış metindir: küçük harf, Türkçe harf farkı yok (`ş=s ğ=g ü=u ö=o ç=c ı=i İ=i`), baştaki/sondaki/art arda boşluk yok. Aynı katlama `DailySearchTerm.Fold` kuralından türetilir (kural tek yerde yaşar).

1. **Otomatik:** katlanmış değer, katalogdaki `Name` veya `Code` ile (türler: `Species`, ırklar: seçilen türe ait `Breed.Name`, renk: `PetColor`, aşı: kiracıya görünür aktif `VaccineDefinition` — global + kiracı özel — ve hastanın türüyle uyumlu) birebir eşleşirse eşlenir. Yerleşik takma adlar küçük ve sabittir (`kopek/köpek`, `kedi`, `kus`, `tavsan`, `kuduz`, `karma`, …) ve katalog adlarının dışında kalan yaygın yazımları kapsar; koda gömülü kısa liste, yönetilen bir tablo değildir.
2. **Benzersiz kaynak değer listesi:** doğrulamadan sonra `GET .../preview` yanıtında, eşlenemeyen her kaynak değer (kaç satırda geçtiğiyle) listelenir.
3. **Değer eşleme:** `PUT .../mapping` ile `valueMappings` içinde `{ kind: "species|breed|vaccine|color", source, targetId | null }` verilir; `targetId = null` bu değerli satırların **atlanması** demektir (ırk/renk için: serbest metin/nota düş). Değer eşlemeleri parti kaydında saklanır ve doğrulama tekrarında yeniden uygulanır.
4. **Yeni katalog kaydı oluşturma ilk sürümde yok** `[K6]`: eşlenemeyen tür/aşı satırları `Imports.SpeciesUnmapped` / `Imports.VaccineUnmapped` hatası alır; klinik gerekli aşı tanımını Ayarlar'dan ekleyip doğrulamayı yeniden çalıştırır.

**B2 çözücü (uygulandı, iç servis `ImportValueResolver`):** Katlama tek yerdedir (`ImportTextFolding.Fold`): `DailySearchTerm.Fold` (ı→i, İ→I) + küçük harf + `ç→c ğ→g ö→o ş→s ü→u â→a î→i û→u` + harf/rakam dışı her karakterin atılması (`Kuduz-2023`, `kuduz 2023`, `KUDUZ_2023` aynı anahtar). Sıra: (1) kullanıcının `valueMappings` kaydı (aynı `kind` + katlanmış `source`; `targetId = null` → `Skipped`), (2) katalog `Name`/`Code` birebir (katlanmış), (3) yerleşik takma ad tablosu (aşağıda; hedef, katalogda katlanmış adıyla aranır, yoksa eşleşmez), (4) `Unmapped`. Boş/boşluk değer `Empty` döner (zorunlu alan denetimi B3'tür). Katalog kapsamı: tür = aktif `Species`; ırk = seçilen türün aktif `Breed` kayıtları; renk = aktif `PetColor`; aşı = kiracıya görünür (global + kiracı) aktif `VaccineDefinition`, tanımın türü doluysa hastanın türüyle aynı olmalı (farklıysa eşleşmez). Yerleşik takma adlar: tür `dog→kopek`, `cat→kedi`, `bird→kus`, `rabbit→tavsan`; aşı `rabies→kuduz`, `kuduz asisi→kuduz`, `mixed→karma`, `karma asisi→karma`, `bronchine→bronsin`, `leukemia→losemi`. Birden fazla katalog kaydı aynı anahtara düşerse (belirsiz) eşleşme **yapılmaz** (`Unmapped`).

## 6) Kurallar

### 6.1 Yinelenen kayıt kuralları

Karşılaştırma **mevcut oluşturma kurallarıyla aynı** anahtarlarla yapılır (kural tek yerde): müşteride `Client.NormalizeFullNameForDuplicateCheck` + normalize telefon/e-posta, hayvanda müşteri + küçük harf ad + tür.

| Durum | Sonuç |
|-------|-------|
| Dosya içinde aynı müşteri (ad + telefon, veya ad + e-posta) iki kez | İkinci satır birincisine **birleştirilir**; satır durumu `DuplicateInFile` (atlandı), uyarı. |
| Kiracıda zaten aynı ad + telefon (veya ad + e-posta) müşteri var | **Mevcutla eşleşir** (`Matched`): yeni müşteri oluşmaz; dosyadaki hastalar bu müşteriye bağlanır `[K4]`. Müşteri alanları **güncellenmez** (mevcut veri ezilmez). |
| Aynı telefon, farklı ad | Yeni müşteri oluşur (aile bireyleri telefon paylaşır, mevcut hızlı kayıt kuralıyla aynı); satıra bilgi uyarısı `Imports.SharedPhone`. |
| Telefon ve e-posta yok, aynı ad | Birleştirilmez (ayırt edilemez); yeni müşteri, uyarı `Imports.PossibleDuplicate` (aynı katlanmış ad dosyada/kiracıda var). |
| Aynı müşteri + aynı hayvan adı + aynı tür dosyada iki kez | İkinci satır birincisine birleştirilir (`DuplicateInFile`). |
| Kiracıda aynı müşteri + ad + tür hayvan var | Mevcutla eşleşir (`Matched`), aşılar buna bağlanır; hayvan alanları güncellenmez. |
| Aynı mikroçip dosyada/kiracıda **başka** hastada | **Hata** `Imports.MicrochipConflict` `[K10]` (mevcut sistemde benzersizlik kuralı yoktur; içe aktarma kirli kopya yaymaz). |
| Aynı hasta + aynı aşı tanımı + aynı uygulama günü (dosyada veya kiracıda) | Atlandı, `DuplicateVaccination` (uyarı değil, bilgi). |

Mevcutla eşleşen kayıtlar geri almada **silinmez** (bölüm 9).

### 6.2 Satır sonuçları

Doğrulama sonrası her satır: `Valid`, `Warning` (aktarılır, not düşülür), `Error` (aktarılmaz). Uygulama sonrası: `Created`, `Matched`, `Skipped` (`DuplicateInFile`/`DuplicateVaccination`/bağımlılık hatası), `Failed`, ve geri almada `RolledBack`/`Kept`. Her satırın sorunları `{ severity, code, field, message }` listesidir; mesajlar Türkçe ve kullanıcıya gösterilebilir.

### 6.3 Aşı geçmişi: tarih penceresi ve hatırlatma etkisi (açık kural)

- İçe aktarılan aşılar yalnız `Status = Applied`, `ExaminationId = null`. **`Scheduled` aşı içe aktarmayla oluşmaz.**
- Günlük aşı girişindeki "uygulama en fazla 7 gün öncesine" penceresi (`VaccinationAppliedAtWindow`) içe aktarmada **uygulanmaz** (geçmiş geçmiştir); onun yerine 4.3'teki kurallar geçerlidir (gelecek yok, ≥ 1990-01-01). Günlük giriş penceresi **gevşetilmez**; içe aktarma kuralı yalnız içe aktarma kodunda yaşar. Durum/tarih tutarlılığı mevcut `VaccinationStatusDateRules` (Applied dalı) ile doğrulanır.
- **Hatırlatma e-postaları içe aktarılan kayıtlar için otomatik GİTMEZ.** Her içe aktarılan `Vaccination` kaydı `ImportBatchId` taşır; `ReminderProcessorService` aşı hatırlatma adaylarından, partisinin `remindersReleasedAtUtc` değeri **boş** olan kayıtları dışlar. Hatırlatma ancak yönetici `POST .../release-reminders` ile açıkça serbest bırakınca değerlendirilir; o zaman da yalnız `DueAtUtc` gelecekte kalanlar (mevcut kural) ve yalnız müşterisi e-postalı olanlar için gönderim hesaplanır. Önizleme bunun etkisini önceden verir (`reminderImpact`).
- Hastanın bir sonraki aşı tarihi `DueAtUtc` olarak saklanır; bu, uygulama ekranlarındaki "sonraki aşı" gösterimlerini besler, hatırlatma serbest bırakılmasa da görünür.
- Serbest bırakma sonrası gönderilmiş e-postalar geri alınamaz; geri alma onayında bu uyarı gösterilir.

### 6.4 Doğrulama kuralları (B3, uygulandı)

Kural kodları, önem dereceleri ve varsayılan mesajlar kodda **tek yerde** tanımlıdır (`ImportRules`); aşağıdaki liste onun belgesidir. Her satır sorunu `{ severity, code, field, message, value? }`: `field` kanonik alan adı, `value` yalnız kullanıcının düzeltmesi için gereken kaynak değer (geçersiz telefon/e-posta, eşlenmeyen tür/aşı/ırk/renk adı). Satır durumu: herhangi bir `Error` → `Error`; yoksa herhangi bir `Warning` → `Warning`; yoksa `Valid` (`Info` durumu değiştirmez). Satırın planlanan eylemi (`action`): `Create` | `Match` (mevcut kayıtla eşleşir) | `Skip`; hatalı satırın eylemi yoktur. İşlem sırası: müşteri → hasta → aşı; her tablo dosya sırasıyla (ilk görülen kazanır).

| Kod | Önem | Kural |
|-----|------|-------|
| `Imports.RequiredMissing` | Error | Zorunlu alan boş (`AdSoyad`, `HayvanAdi`, `Tur`, `AsiAdi`, `UygulamaTarihi`). |
| `Imports.NameTooShort` | Error | Müşteri adı < 2 karakter (kırpılmış, art arda boşluk teke inmiş). |
| `Imports.FieldTooLong` | Error | Ad > 300 (müşteri) / 200 (hayvan), e-posta > 320 hariç (bkz. EmailInvalid), adres > 500, ırk serbest metni > 150, karne > 50, hasta notu > 2000 (Renk satırıyla birlikte), aşı notu > 4000. |
| `Imports.PhoneInvalid` | Warning | Telefon cep standardına uymuyor (sabit hat, yabancı, eksik hane); telefon boş aktarılır `[K5]`, `value` özgün numara. Sahip telefonu (hasta/aşı tablosu) geçersizse sahip yalnız ad ile aranır (aşağıda). |
| `Imports.EmailInvalid` | Warning | E-posta mevcut müşteri kuralıyla (FluentValidation `EmailAddress`, ≤ 320) geçersiz; boş aktarılır, `value` özgün. |
| `Imports.DateInvalid` | Error | Tarih ayrıştırılamadı veya takvimde yok (`31.02.2024`). |
| `Imports.DateFormatAmbiguous` | Error | Biçim belirsiz ve reddedilen sınıfta: iki haneli yıl, yalnız ay/yıl, ay adı (bölüm 4.4). |
| `Imports.DateOutOfRange` | Error | Uygulama tarihi 1990-01-01'den eski. |
| `Pets.BirthDateInFuture` | Error | Doğum tarihi bugünden (İstanbul takvim günü) sonra. |
| `Imports.BirthDateImplausible` | Warning | Doğum tarihi 40 yıldan eski (aktarılır). |
| `Vaccinations.AppliedAtMustNotBeFuture` | Error | Uygulama tarihi bugünden sonra. |
| `Vaccinations.DueAtMustBeAfterAppliedAt` | Error | Sonraki aşı tarihi uygulama tarihinden sonra değil. |
| `Vaccinations.DueTooFarInFuture` | Error | Sonraki aşı tarihi bugünden 5 yıl ötede (geçmiş sınırı yok). |
| `Imports.AppliedBeforeBirth` | Warning | Uygulama tarihi hastanın doğum tarihinden önce (dosyadaki veya mevcut hasta). |
| `Imports.MicrochipInvalid` | Error | Boşluk/`-`/`.` temizlendikten sonra rakam dışı karakter var veya > 50 hane. |
| `Imports.MicrochipScientificNotation` | Error | Değer bilimsel gösterimde (`9,85121E+14`); hücreyi metin yapın. |
| `Imports.MicrochipLength` | Warning | Çip 15 hane değil (eski çip olabilir). |
| `Imports.MicrochipConflict` | Error | Aynı çip dosyada önceki başka hayvanda **veya** kiracıda başka bir hayvanda `[K10]`; mesaj çakışan satırı/kaydı belirtir. Aynı hayvanın tekrarı (hasta mükerrer kuralı) çakışma sayılmaz. |
| `Imports.SpeciesUnmapped` / `Imports.VaccineUnmapped` | Error | Tür/aşı adı katalogda yok ve kullanıcı eşlememiş `[K6]`; `value` kaynak değer. Yeni tanım oluşmaz. |
| `Imports.ValueSkipped` | Warning | Tür/aşı değeri kullanıcı tarafından bilinçli atlanmış (`targetId = null`); satır eylemi `Skip`. |
| `Imports.ParentSkipped` | Warning | Bağlı üst satır (hasta) bilinçli atlandığı için satır da atlanır. |
| `Imports.BreedFreeText` | Info | Irk katalogda yok / atlandı → serbest metin; `value` kaynak. |
| `Imports.ColorFreeText` | Info | Renk katalogda yok / atlandı → `Notes`'a `Renk: <değer>` eklenir; `value` kaynak. |
| `Pets.BreedSpeciesMismatch` | Error | Kullanıcının eşlediği ırk, satırın türüne ait değil. |
| `VaccineDefinitions.InvalidSpecies` | Error | Kullanıcının eşlediği aşı tanımı hastanın türüyle uyumsuz. |
| `Imports.GenderUnrecognized` | Warning | Cinsiyet tanınmadı; boş aktarılır. |
| `Imports.NeuteredUnrecognized` | Warning | Kısır değeri tanınmadı; `false` aktarılır. |
| `Imports.WeightInvalid` | Warning | Ağırlık sayı değil veya 0–9999,99 dışında; boş aktarılır. |
| `Imports.DuplicateReference` | Error | `MusteriNo`/`HayvanNo` dosyada daha önce kullanılmış (kırpılmış, büyük/küçük harf duyarsız). |
| `Imports.DuplicateInFile` | Warning | Satır, aynı dosyada önceki bir satırla aynı kayıt (müşteri: aynı ad+telefon veya ad+e-posta; hasta: aynı sahip+ad+tür); eylem `Skip`, bağlı satırlar ilk satıra bağlanır. |
| `Imports.DuplicateClient` | Error | Mevcut müşteriyle eşleşti ve `options.onDuplicateClient = Error`. |
| `Imports.MatchedExisting` | Info | Mevcut müşteri/hasta ile eşleşti (`Match`: eylem `Match`; `Skip` modu: eylem `Skip`). Mevcut alanlar güncellenmez `[K4]`. |
| `Imports.SharedPhone` | Info | Aynı telefon başka adlı müşteride var (dosyada veya kiracıda); yeni müşteri oluşur. |
| `Imports.PossibleDuplicate` | Warning | Telefon ve e-posta yok ve aynı (katlanmış) ad dosyada/kiracıda zaten var; yeni müşteri oluşur. |
| `Imports.OwnerNotFound` | Error | Hasta satırının sahibi dosyada veya kiracıda bulunamadı / belirsiz. |
| `Imports.OwnerRowInvalid` | Error | Sahip müşteri satırı hatalı olduğundan hasta aktarılamaz. |
| `Imports.PetNotFound` | Error | Aşı satırının hastası dosyada veya kiracıda bulunamadı / belirsiz. |
| `Imports.PetRowInvalid` | Error | Hasta satırı hatalı olduğundan aşı aktarılamaz. |
| `Imports.DuplicateVaccination` | Info | Aynı hasta + aynı aşı tanımı + aynı uygulama günü dosyada önceden var veya kiracıda kayıtlı; eylem `Skip`. |

Bağlantı çözümü: **Hasta→müşteri:** `MusteriNo` doluysa yalnız dosyadaki müşteri satırı aranır (bulunamazsa `OwnerNotFound`, `MusteriNo` kiracıdaki kayıtlara bağlanmaz). `MusteriNo` yoksa/eşlenmemişse `SahipAdSoyad + SahipTelefon`: telefon geçerliyse önce dosyadaki müşteri (ad+telefon), sonra kiracıdaki müşteri (ad+telefon); telefon boş/geçersizse (`PhoneInvalid` uyarısı) dosyadaki müşteri tablosunda **aynı katlanmış adlı tek** müşteri aranır (birden çok ya da yoksa `OwnerNotFound`; kiracı tarafında telefonsuz ad eşleşmesi yapılmaz). Müşteri satırı mevcut müşteriyle eşleştiyse (`Match`/`Skip` modu) hasta o kiracı müşterisine bağlanır. **Aşı→hasta:** `HayvanNo` doluysa dosyadaki hasta satırı; yoksa sahip (yukarıdaki kural) + `HayvanAdi` + `Tur` ile dosyadaki hasta, sonra sahibin kiracıdaki hastaları. Hasta mevcut hastayla eşleştiyse (aynı müşteri + küçük harf ad + tür) aşı o hastaya bağlanır.

Katalog değerleri satır bağlamında `ImportValueResolver` ile çözülür (bölüm 5): tür → ırk (türün ırkları) → renk; aşı → hastanın türü. Sonuç `Empty` zorunlu alanda `RequiredMissing`; `Unmapped` tür/aşı `Error`, ırk/renk `Info` (serbest metin).

Normalize değerler: telefon `905XXXXXXXXX`; e-posta küçük harf; tarih `yyyy-MM-dd`; cinsiyet `Male|Female`; mikroçip yalnız rakam; aşı tarihleri bölüm 4.4'teki gibi İstanbul 12:00 → UTC. Normalize satır `NormalizedJson` içinde saklanır (kişisel veri: bölüm 10). Planlanan eylem ve bağlı kayıt kimliği de buradadır; B4 eşleşmeleri uygulama anında yeniden çözer.

Toplu sorgular (N+1 yok): doğrulama kiracının mevcut verisini sabit sayıda sorguyla okur — katalog (tür, ırk, renk, aşı tanımı: 4 sorgu), partinin hazırlık satırları (1), dosyadaki tüm telefon/e-posta/ad kümeleriyle **tek** müşteri sorgusu, bulunan müşterilerin ve dosyadaki çiplerin hastaları için **tek** hasta sorgusu, bulunan hastaların uygulanmış aşıları için **tek** aşı sorgusu (kümeler tek JSON parametresiyle gider); satır sonuçları EF toplu yazmasıyla kaydedilir (komut başına ~42 satır, satır başına komut yok). SELECT sayısı satır sayısına bağlı değildir. Ölçüm (yerel LocalDB, doğrulama isteği): 9 satırlı dosya ve 10.200 satırlı dosyada **16 SELECT** (kimlik/abonelik/yetki/parti okumaları dahil), 24.000 satırda da 16; 10.200 satır ≈ 1,3 sn, 243 satır-güncelleme komutu (24.000 satır: 572).

**Bilinen sınır (tasarım §14):** ad karşılaştırması mevcut müşteri kuralındaki `ToLowerInvariant` ile yapıldığından `AYŞE YILMAZ` ile `Ayşe Yılmaz` aynı kabul edilmez (`I` → `i`, `ı` değil). Bu fark mükerrer tespitini kaçırır; kural değişirse tek yerde (`Client.NormalizeFullNameForDuplicateCheck`) değiştirilmeli, içe aktarma onu aynen izler.

## 7) Uçlar

Hata zarfı için bölüm 8. `{id}` = parti kimliği.

### 7.1 `GET /imports/template`
`?format=xlsx` (tek dosya) veya `?format=csv&table=customers|pets|vaccinations`. `Imports.Read`. `200` dosya (`Content-Disposition`), `Cache-Control: no-store`. İçerik bölüm 4.2 ("B2 şablon içeriği").

### 7.2 `POST /imports/batches` (parti oluştur)
`multipart/form-data`, `Imports.Manage`. Alanlar:

| Alan | Zorunlu | Not |
|------|---------|-----|
| `requestId` | **Evet** (Guid) | Aynı kiracı + aynı `requestId` ikinci çağrıda **aynı partiyi** döndürür (`200`); farklı içerikle gelirse `409 Imports.RequestIdConflict`. |
| `file` | Evet (XLSX için) | XLSX: üç sayfa içerebilir. Ya `file` ya da CSV alanlarından en az biri zorunludur. |
| `customersFile`, `petsFile`, `vaccinationsFile` | CSV için | Her biri ayrı CSV. `file` ile birlikte verilemez. |
| `clinicId` | Aşı varsa evet | Bölüm 4.3. |
| `name` | Hayır | Listede görünen etiket (≤ 100); kişisel veri içermemeli. |
| `encoding`, `delimiter` | Hayır | CSV zorlaması. |

İşlem: dosya boyut/biçim denetimi, **içerik özeti (SHA-256)** hesabı, başlıkların okunması, kanonik başlıklarla otomatik eşleme önerisi. Satırlar bu aşamada saklanır (`Uploaded`) ama doğrulanmamıştır.

**B2 davranışı (uygulandı):** Dosya alanları işlenir (bölüm 4.1 "B2 okuyucu ayrıntıları"). Sıra: kiracı/klinik denetimi → dosya imza/boyut denetimi → içerik özeti → aynı `requestId` tekrarı (`200` aynı parti; `name`, `clinicId` **ve içerik özeti** aynı olmalı, aksi `409 Imports.RequestIdConflict`) → aynı özetli aktif parti (`409 Imports.DuplicateFile`; `extensions.batchId` yalnız çağıranın görebildiği partiyse) → açık taslak sınırı → okuma (bölüm 4.1 hataları) → aşı tablosu var ve `clinicId` yok (`400 Imports.ClinicRequired`) → kayıt. Parti ve hazırlık satırları **tek veritabanı işleminde** yazılır; okuma hatasında hiçbir şey kalmaz. Satırlar `ImportBatchRow` olarak saklanır (`SourceJson` = başlık → metin sözlüğü, `NormalizedJson` boş, doğrulama durumu `Pending`); `summary.<tablo>.total` satır sayısı olur. Dosya yoksa `400 Imports.EmptyFile`. Aşı için `clinicId` erişimi B1'deki gibi okuma kapsamıyla denetlenir; aşı yazma erişimi ve `Vaccinations.Create` denetimi uygulama (B4) adımındadır. `encoding` ve `delimiter` değerleri izinli kümede değilse `400 Validation.FluentValidation`. Audit payload'ı dosya içeriği ve satır verisi taşımaz (yalnız `requestId`, `name`, `clinicId`, `encoding`, `delimiter`).

Yanıt gövdesi (B1 alanlarına ek): `tables[]` ve yalnız yükleme yanıtında `warnings[]` (`{ code, table?, message }`: `Imports.EncodingSuspect`, `Imports.TableEmpty`). `tables[].table` = `customers|pets|vaccinations`; `rowCount` = veri satırı sayısı; `columns[]` = dosyadaki başlıklar sırasıyla `{ source, suggested, confidence, mapped }`: `suggested` kanonik başlık veya `null`, `confidence` `Exact` (kanonik adla katlanmış eşit) | `Alias` (takma adla eşit) | `None`, `mapped` kayıtlı eşlemedeki kanonik ad (eşleme kaydedilmemişse `null`). Öneri kuralı: önce `Exact`, sonra `Alias`; bir kanonik alan en çok bir sütuna, bir sütun en çok bir kanonik alana önerilir (ilk gelen kazanır). Sütun başlığı karşılaştırması bölüm 4.1/5 katlamasıyladır. Takma ad tablosu koddadır (`ImportCanonicalSchema`); yönetilen veri değildir.

**B1 davranışı (uygulandı):** Dosya alanları henüz yoktur; yalnız `requestId` (zorunlu), `name` (≤ 100, boşluk kırpılır, boşsa null) ve `clinicId` (opsiyonel) okunur. `clinicId` verilirse kiracıya ait ve çağıranın erişebildiği klinik olmalı (`404 Clinics.NotFound` / `403 Clinics.AccessDenied`) ve aktif klinik bağlamıyla çelişmemeli (`400 Imports.ClinicContextMismatch`). Pasif kiracı `403 Tenants.TenantInactive`; abonelik yazma koruması mevcut kodlarla. Aynı `requestId` + aynı `name`/`clinicId` → `200` aynı parti; farklı `name` veya `clinicId` → `409 Imports.RequestIdConflict` (B2'de içerik özeti de karşılaştırmaya girer). Kiracıda 5 açık taslak varsa `409 Imports.TooManyOpenDrafts`. Yanıt gövdesi bölüm 7.5'teki parti nesnesidir (`tables[]` B2'de eklenir).

Aynı kiracıda **aynı içerik özetli** ve durumu `Discarded/Expired/RolledBack/CommitFailed` **olmayan** parti varsa `409 Imports.DuplicateFile` + `extensions.batchId` (aynı dosya iki kez yüklenip çift kayıt oluşmasın; geri alınmış partinin dosyası yeniden yüklenebilir).

`201 Created` (`Location` → `GET /imports/batches/{id}`):

```json
{
  "id": "guid", "status": "Uploaded", "name": "string?", "clinicId": "guid?",
  "tables": [
    { "table": "customers", "rowCount": 1200,
      "columns": [ { "source": "Ad Soyad", "suggested": "AdSoyad", "confidence": "Exact|Alias|None", "mapped": null } ] }
  ],
  "warnings": [ { "code": "Imports.EncodingSuspect", "table": "customers", "message": "..." } ],
  "createdAtUtc": "...", "createdBy": "guid"
}
```

### 7.3 `PUT /imports/batches/{id}/mapping`
`Imports.Manage`; durum `Uploaded|Mapped|Validated|ValidationFailed`. Tam değiştirme:

```json
{
  "columns": { "customers": { "AdSoyad": "Ad Soyad", "Telefon": "Cep" }, "pets": { }, "vaccinations": { } },
  "valueMappings": [ { "kind": "species", "source": "Kopek", "targetId": "guid" } ],
  "options": { "onDuplicateClient": "Match" }
}
```
`options.onDuplicateClient` `[K4]`: `Match` (öneri) | `Skip` | `Error`. Doğrulanmış parti eşleme değişince `Mapped`'a düşer (önizleme geçersiz). Yanıt: güncel parti özeti. Hatalar: `Imports.MappingIncomplete`, `Imports.MappingUnknownColumn`, `Imports.ValueMappingTargetInvalid` (hedef yok/pasif/tür uyumsuz).

**B2 davranışı (uygulandı):** `columns` anahtarı `kanonik başlık → kaynak sütun adı`dır (örnekteki gibi). `columns` / `valueMappings` / `options` tamamı tam değiştirme; `options` verilmezse `onDuplicateClient = Match`. Denetimler (tümü `400`, hiçbir şey kaydedilmez):
- Partide olmayan tablo anahtarı, tablonun kanonik listesinde olmayan alan adı (büyük/küçük harf duyarlı kanonik ad), tabloda bulunmayan kaynak sütun adı (başlıkla birebir) → `Imports.MappingUnknownColumn`.
- Aynı kaynak sütunun aynı tabloda iki kanonik alana eşlenmesi → `Imports.MappingSourceReused`.
- Partide bulunan her tablo için zorunlu alanlar: müşteri `AdSoyad`; hasta `HayvanAdi`, `Tur` ve sahip bağlantısı (`MusteriNo` **veya** `SahipAdSoyad`+`SahipTelefon`); aşı `AsiAdi`, `UygulamaTarihi` ve hasta bağlantısı (`HayvanNo` **veya** `SahipAdSoyad`+`SahipTelefon`+`HayvanAdi`+`Tur`). Eksik → `Imports.MappingIncomplete` (mesaj eksik alanları sayar). Tablolar arası bağlantı tutarlılığı (hasta `MusteriNo` ile bağlanıyorsa müşteri tablosunda `MusteriNo` eşlenmiş olması) `validate` (B3) adımındadır.
- `valueMappings[].kind` ∈ `species|breed|vaccine|color`; `source` boş olamaz (≤ 200); aynı `kind` + katlanmış `source` iki kez verilemez; `targetId` dolu ise hedef var, aktif ve (aşı için) kiracıya görünür olmalı → `Imports.ValueMappingTargetInvalid`. Tür uyumu (ırk–tür, aşı–hasta türü) satır bağlamı gerektirdiğinden `validate` (B3) adımındadır. `targetId = null` bilinçli atlamadır.
- `options.onDuplicateClient` izinli kümede değilse `400 Validation.FluentValidation`.
Durum geçişi domain'dedir (`Uploaded|Mapped|Validated|ValidationFailed` → `Mapped`, önceki `validationId` silinir); diğer durumda `409 Imports.InvalidState`. Yanıt `200` + parti nesnesi (`tables[].columns[].mapped` kayıtlı eşlemeyi gösterir). Audit eylemi `ImportBatch.Mapping` (hedef `ImportBatchId`; payload'da sütun/değer eşleme gövdesi **yazılmaz**: kaynak değerler kişisel veri taşıyabilir).

### 7.4 `POST /imports/batches/{id}/validate`
`Imports.Manage`; durum `Mapped|Validated|ValidationFailed`. **Veritabanına müşteri/hasta/aşı yazmaz** (yalnız parti ve satır sonuçları). Taslak sözleşme `202 Accepted` + arka plan idi; B3'te senkron `200` (aşağıda). Sonuçta `Validated` (satır hataları olsa da) veya `ValidationFailed` (dosya düzeyi sorun: zorunlu sütun yok, yapısal bozukluk, hiç geçerli satır yok). Aynı kiracıda başka aktif iş varsa `409 Imports.AnotherJobRunning`. Tekrar çağrılabilir; idempotent (aynı eşleme → aynı sonuç, yeni `validationId`).

**B3 davranışı (uygulandı):** İş **kısa ve senkrondur**: istek `Validating` durumuna geçirip kaydeder (kiracıdaki tek-aktif-iş benzersiz indeksi ikinci eşzamanlı doğrulamayı/uygulamayı `409 Imports.AnotherJobRunning` ile reddeder), tüm satırları bellekte işler, satır sonuçlarını ve parti durumunu **tek veritabanı işleminde** yazar ve `200` + parti nesnesi döner (`202`/yoklama yok; sınırlar 25.000 satırı aşmadığından arka plan işi gereksiz bulundu — süre ölçümü ve gerekirse arka plana taşıma kararı T diliminde). Kira (`leaseOwner`, `leaseUntilUtc`, 5 dakika) bir çökme sonrası takılı kalan `Validating` partiyi korur: kira dolduktan sonra aynı parti yeniden doğrulanabilir (`Validating` kaynak durum olur); kira dolmadan `409 Imports.AnotherJobRunning`. Beklenmeyen hata partiyi `ValidationFailed` yapar ve yeniden fırlatılır (500). Durum kuralları: kaynak `Mapped|Validated|ValidationFailed` (süresi dolmuş kirayla `Validating`); aksi `409 Imports.InvalidState` (eşlemesiz `Uploaded` dahil).
- **Dosya düzeyi başarısızlık** (`ValidationFailed`, satırlar işlenmez; nedenler `preview.blockers`): hasta tablosu yalnız `MusteriNo` ile bağlanıyorsa (sahip adı+telefon eşlenmemişse) müşteri tablosu partide yok veya orada `MusteriNo` eşlenmemiş (`Imports.OwnerLinkUnavailable`); aşı tablosu yalnız `HayvanNo` ile bağlanıyorsa hasta tablosu partide yok veya orada `HayvanNo` eşlenmemiş (`Imports.PetLinkUnavailable`); işlenen tüm satırlar hatalı (`valid + warning = 0` tüm tablolarda; `Imports.NoImportableRows`).
- Eşleme çok geç bozulmuşsa (zorunlu alan kaybı; olağan akışta `PUT mapping` engeller) `400 Imports.MappingIncomplete`.
- Önceki doğrulamanın satır sonuçları yeni sonuçlarla tamamen yer değiştirir; `validationId` her seferinde yenilenir.

### 7.5 `GET /imports/batches/{id}` ve `GET /imports/batches`
`Imports.Read`. Ayrıntı:

```json
{
  "id": "guid", "status": "Validated", "name": "string?", "clinicId": "guid?",
  "validationId": "guid?", "validatedAtUtc": "...", "committedAtUtc": "...", "rolledBackAtUtc": "...",
  "remindersReleasedAtUtc": null,
  "progress": { "phase": "Validating|Committing|RollingBack", "processed": 4300, "total": 12000 },
  "summary": { "customers": {…}, "pets": {…}, "vaccinations": {…} },
  "payloadPurgedAtUtc": null
}
```
`summary.<tablo>`: `{ "total", "valid", "warning", "error", "willCreate", "willMatch", "willSkip" }`; uygulama sonrası `created`, `matched`, `skipped`, `failed`. Liste: `?page=1&pageSize=20&status=` (`pageSize` ≤ 100), yeniden eskiye sıralı, mevcut liste zarfıyla (`items`, toplam sayı).

**B1 yanıt şekli (uygulandı; ayrıntı ve liste öğesi aynı nesne):**

```json
{
  "id": "guid", "status": "Uploaded", "name": "Eski sistem 1. parti", "clinicId": "guid|null",
  "validationId": null, "createdByUserId": "guid", "createdAtUtc": "...", "updatedAtUtc": null,
  "validatedAtUtc": null, "committedAtUtc": null, "rolledBackAtUtc": null,
  "remindersReleasedAtUtc": null, "payloadPurgedAtUtc": null,
  "summary": {
    "customers":    { "total": 0, "valid": 0, "warning": 0, "error": 0, "willCreate": 0, "willMatch": 0, "willSkip": 0, "created": 0, "matched": 0, "skipped": 0, "failed": 0 },
    "pets":         { "...": 0 },
    "vaccinations": { "...": 0 }
  }
}
```

- `progress` B4'te eklenir. `updatedAtUtc` son durum değişikliğidir (7 günlük süre bunun üzerinden, yoksa `createdAtUtc`).
- Liste: `GET /imports/batches?page=1&pageSize=20&status=Uploaded` → `{ items, page, pageSize, totalItems, totalPages }`; `pageSize` 1–100, `status` geçersizse `400`. Sıralama `createdAtUtc` azalan.
- Görünürlük: yalnız çağıranın kiracısı. Klinik bağlı parti (`clinicId` dolu) yalnız o kliniğe erişebilen kullanıcıya görünür; erişemeyen için ayrıntı `404 Imports.BatchNotFound`, listede görünmez. Klinik bağsız parti kiracıdaki `Imports.Read` sahiplerine görünür (müşteri/hasta kiracı genelidir).

### 7.6 `GET /imports/batches/{id}/preview` (kuru çalıştırma sonucu)
`Imports.Read`; doğrulaması bitmiş parti: `Validated`, `ValidationFailed`, `Committing`, `Committed`, `CommitFailed`, `RollingBack`, `RolledBack`, `PartiallyRolledBack`, `RollbackFailed`; diğer durumda (`Uploaded`, `Mapped`, `Validating`, `Discarded`, `Expired`) `409 Imports.InvalidState`. Yanıt (`Cache-Control: no-store`):

```json
{
  "validationId": "guid|null",
  "status": "Validated",
  "summary": { "customers": {…}, "pets": {…}, "vaccinations": {…} },
  "unmappedValues": [ { "kind": "species", "source": "Kopek", "rowCount": 41, "blocking": true } ],
  "reminderImpact": {
    "vaccinationsWithDueDate": 900, "vaccinationsWithFutureDueDate": 830, "vaccinationsWithPastDueDate": 70,
    "clientsWithEmail": 410, "emailsWithinReminderWindow": 95, "reminderWindowDays": 3,
    "tenantRemindersEnabled": false, "excludedByDefault": true
  },
  "issueCounts": [ { "code": "Imports.PhoneInvalid", "severity": "Warning", "rowCount": 37 } ],
  "canCommit": true,
  "blockers": [ "Imports.VaccineUnmapped" ],
  "payloadPurged": false
}
```
- `unmappedValues`: eşlenemeyen benzersiz kaynak değerler (katlanmış anahtarla tekilleştirilir; `source` ilk görülen yazım), `kind` = `species|vaccine|breed|color`, `rowCount` değerin geçtiği satır sayısı. `blocking`: tür/aşı için `true` (satırlar hatalıdır; kullanıcı `valueMappings` ile eşler veya `targetId: null` ile atlar), ırk/renk için `false` (serbest metin/nota düşer, eşleme isteğe bağlı). Sıralama: `kind`, sonra `rowCount` azalan.
- `reminderImpact` (yalnız bilgi; ADR-019 K3, bölüm 6.3): yalnız **`action = Create` ve hatasız** aşı satırları sayılır. `vaccinationsWithDueDate` sonraki aşı tarihi dolu olanlar; `vaccinationsWithFutureDueDate` / `vaccinationsWithPastDueDate` bunların çağrı anına göre gelecekte / geçmişte kalanı (geçmişte kalan hatırlatma **üretmez**); `clientsWithEmail` gelecek vadeli aşıların sahiplerinden e-postası olan benzersiz müşteri sayısı (dosyadaki veya eşleşen mevcut müşteri); `emailsWithinReminderWindow` vadesi `(şimdi, şimdi + reminderWindowDays]` içinde olan ve sahibinin e-postası bulunan aşı sayısı (hatırlatma serbest bırakılırsa mevcut işçi kuralının üreteceği e-posta üst sınırı; `BatchSize` turlarına bölünür); `reminderWindowDays` kiracı ayarı `VaccinationReminderDaysBefore` (ayar kaydı yoksa varsayılan 3); `tenantRemindersEnabled` kiracı aşı hatırlatması açık mı; `excludedByDefault` her zaman `true` (içe aktarılan aşılar varsayılan dışlanır). İleri tarihli e-postalar pencere ilerledikçe de oluşur; bu sayı yalnız bugünkü pencereyi gösterir.
- `issueCounts`: sorun kodu + önem başına etkilenen satır sayısı (aynı satırda aynı kod bir kez), `rowCount` azalan.
- `canCommit`: durum `Validated`, `validationId` dolu, `blockers` boş ve `willCreate` toplamı > 0.
- `blockers`: dosya düzeyi nedenler (`Imports.OwnerLinkUnavailable`, `Imports.PetLinkUnavailable`, `Imports.NoImportableRows`) ve satırı olan **bloklayan** eşlenmemiş değer kodları (`Imports.SpeciesUnmapped`, `Imports.VaccineUnmapped`). Hatalı satırların varlığı tek başına engel değildir `[K2]` (`onErrors`, bölüm 7.9).
- Satır verisi temizlenmişse (`payloadPurgedAtUtc` dolu) `summary` korunur; `unmappedValues`, `reminderImpact`, `issueCounts` boş/sıfır, `payloadPurged = true`, `canCommit = false`.

### 7.7 `GET /imports/batches/{id}/rows`
`Imports.Read`; durum kuralı bölüm 7.6 ile aynı. Sorgu: `table=customers|pets|vaccinations` (verilmezse üç tablo: müşteri, hasta, aşı sırasıyla), `status=Valid|Warning|Error` (doğrulama durumu; uygulama sonrası `Created|Matched|Skipped|Failed|RolledBack|Kept` uygulama durumunu süzer), `code=<kural kodu>` (satırın sorunlarından birinin kodu), `page` (varsayılan 1), `pageSize` (varsayılan 50, 1–200). Sıralama: tablo, `rowNumber`. Yanıt `{ items, page, pageSize, totalItems, totalPages }`; öğe:

```json
{ "table": "pets", "rowNumber": 12,
  "validation": { "status": "Warning", "action": "Create" },
  "apply": { "status": "Pending", "targetId": null },
  "issues": [ { "severity": "Warning", "code": "Imports.MicrochipLength", "field": "Mikrocip", "message": "…" } ],
  "values": { "name": "Pamuk", "speciesId": "guid", "…": "…" },
  "source": { "Hayvan Adı": "Pamuk", "Tür": "Kedi" } }
```
`values` normalize edilmiş alanlardır (bölüm 6.4; tablo başına küçük camelCase nesne), `source` özgün satırdır (başlık → metin). Geçersiz `table`/`status`/sayfa parametresi `400 Validation.FluentValidation`. Kişisel veri içerdiğinden `Cache-Control: no-store`; temizlenmiş partide `410 Imports.PayloadPurged`.

### 7.8 `GET /imports/batches/{id}/errors?format=xlsx|csv`
`Imports.Read`; durum kuralı bölüm 7.6 ile aynı; `format` zorunlu değil, varsayılan `xlsx` (geçersizse `400`). Hata raporu indirme: yalnız doğrulama durumu `Error` (veya uygulama durumu `Failed`) satırlar; her tablo için bölüm 4.3'teki **kanonik başlıklar değil, özgün kaynak sütunlar** (dosyadaki başlık sırasıyla, özgün değerlerle) ve ek sütunlar `SatirNo`, `Durum`, `HataKodu`, `Aciklama` (Türkçe; satırın birden çok hatası `;` ile ayrılır, `HataKodu`/`Aciklama` aynı sırada). XLSX'te tablo başına bir sayfa (`Musteriler`, `Hayvanlar`, `Asilar`; yalnız hatalı satırı olanlar; hata yoksa tek `Hatasiz` sayfası), CSV'de tablolar farklı başlık taşıdığından tek dosya sabit sütunlarla yazılır (UTF-8 BOM'lu, `;` ayırıcılı): `Tablo`, `SatirNo`, `Durum`, `HataKodu`, `Aciklama`, `Kaynak` (özgün satır `başlık=değer` çiftleri, `|` ile ayrılmış); düzeltilip yeniden yüklenecek rapor için XLSX önerilir. Düzeltilen dosya aynı şablonla **yeni parti** olarak yüklenebilir; daha önce oluşan kayıtlar `Matched`/`DuplicateVaccination` olacağından çift kayıt oluşmaz. Formül enjeksiyonu: `= + - @` (ve sekme/CR) ile başlayan **tüm** hücre değerlerinin başına `'` eklenir (`ImportCellSafety.ForExport`; başlık hücreleri dahil). XLSX'te bu `'`, Excel'in metin öneki (`quotePrefix`) olarak yazılır (hücre metindir, formül değildir, değer görünürde değişmez); CSV'de görünür `'` olarak yazılır. `Content-Disposition` dosya adı `vetinity-iceaktar-hatalar.xlsx|csv`; `Cache-Control: no-store`; temizlenmiş partide `410 Imports.PayloadPurged`. İndirme audit'e yazılır (`ImportBatch.ErrorReport`, hedef `ImportBatchId`; satır verisi yok).

### 7.9 `POST /imports/batches/{id}/commit`
`Imports.Commit` + bölüm 2'deki ek izinler. Gövde:

```json
{ "validationId": "guid", "onErrors": "SkipErrorRows" }
```
`onErrors` `[K2]`: `SkipErrorRows` (öneri; hatasız satırlar uygulanır, hatalılar atlanır ve raporlanır) | `FailIfAny` (hatalı satır varsa `409 Imports.HasErrorRows`, hiçbir şey yazılmaz). Önkoşullar: durum `Validated`; `validationId` güncel (`409 Imports.ValidationStale` aksi); kiracı aboneliği yazmaya açık (mevcut `TenantSubscriptionEffectiveWriteEvaluator`; aksi onun hata kodu); kiracı aktif. Yanıt `202 Accepted` + parti özeti (`Committing`). Aynı çağrının tekrarı (durum zaten `Committing/Committed`) **aynı özeti `200`** döner, yeni iş başlatmaz (idempotent).

Uygulama (arka plan): müşteri → hasta → aşı sırası; **sahip bazlı** işlem birimi (müşteri + hastaları + aşıları aynı veritabanı transaction'ında; sahip adımı başarısız olursa o sahibin tüm satırları `Skipped`/`Failed`, diğer sahipler etkilenmez). Her satırın sonucu ve oluşturulan/eşleşen kayıt kimliği, kaydın kendisiyle **aynı transaction'da** yazılır; süreç çökerse kaldığı yerden devam eder, yazılmış satır yeniden uygulanmaz. Uygulama anında eşleşme kuralları yeniden çözülür (önizlemeden sonra elle eklenen müşteri `Matched` olur); sayılar önizlemeden sapabilir ve nihai rapor esas alınır. Her kayıt mevcut oluşturma yoluyla (`CreateClientCommand`, `CreatePetCommand`, içe aktarma aşı yolu) yazıldığından outbox olayları (`client.created.v1`, `pet.created.v1`) aynen üretilir.

### 7.10 `POST /imports/batches/{id}/release-reminders`
`Imports.Commit`; durum `Committed|PartiallyRolledBack`. Gövde: `{ "confirmEmailCount": 95 }` (onay anındaki `emailsWithinReminderWindow` ile eşleşmezse `409 Imports.ReminderImpactChanged`, güncel sayı döner). Başarıda `remindersReleasedAtUtc` set edilir; geri alınamaz (tek yönlü), audit'e yazılır. Kiracıda aşı hatırlatması kapalıysa (`TenantReminderSettings`) yanıt `200` + `extensions.note` ("kiracı ayarı kapalı; hatırlatma gitmez"). `[K3]`

### 7.11 `GET /imports/batches/{id}/rollback-preview`
`Imports.Read`; durum `Committed|PartiallyRolledBack`. Silinecek / silinemeyecek kayıt sayıları ve nedenleri (bölüm 9):

```json
{ "deletable": { "vaccinations": 3100, "pets": 1900, "clients": 1200 },
  "blocked": [ { "entity": "client", "id": "guid", "reason": "HasVisits" } ],
  "kept": { "matchedClients": 150, "matchedPets": 80 },
  "remindersAlreadyReleased": false }
```

### 7.12 `POST /imports/batches/{id}/rollback`
`Imports.Commit`. Gövde: `{ "reason": "string (zorunlu, ≤ 500)", "blockedPolicy": "Fail" }`. `blockedPolicy` `[K7]`: `Fail` (öneri; engellenen kayıt varsa `409 Imports.RollbackBlocked` + liste, hiçbir şey silinmez) | `KeepBlocked` (silinebilenler silinir, engelliler kalır → `PartiallyRolledBack`). `202 Accepted`; `RollingBack` → `RolledBack|PartiallyRolledBack|RollbackFailed`. Yalnız o partinin oluşturduğu kayıtlar silinir. Tekrar çağrı: zaten `RollingBack/RolledBack` ise `200` aynı özet.

### 7.13 `DELETE /imports/batches/{id}`
`Imports.Manage`; yalnız taslak durumları (`Uploaded|Mapped|Validated|ValidationFailed`). Hazırlık verisini siler (ürün verisine dokunmaz) → `Discarded`; `204`. `Committing/Committed` partide `409 Imports.InvalidState`.

**Davranış (B1 + B2, uygulandı):** Parti kaydı silinmez, durumu `Discarded` olur (liste/geçmişte görünür, içerik özeti serbest kalır). B2'den itibaren partinin hazırlık satırları (`ImportBatchRows`) **aynı işlemde silinir** ve `payloadPurgedAtUtc` silme anıyla dolar. Zaten `Discarded` parti için tekrar çağrı `409 Imports.InvalidState`. Başka kiracı/erişilemeyen klinik → `404 Imports.BatchNotFound`.

## 8) Hata kodları

| HTTP | Kod | Durum |
|------|-----|-------|
| 400 | `Validation.FluentValidation` | İstek biçimi (eksik `requestId`, geçersiz sayfa vb.) |
| 400 | `Imports.UnsupportedFile` / `Imports.FileTooLarge` / `Imports.TooManyRows` / `Imports.EmptyFile` | Dosya düzeyi |
| 400 | `Imports.MappingIncomplete` / `Imports.MappingUnknownColumn` / `Imports.MappingSourceReused` / `Imports.ValueMappingTargetInvalid` | Eşleme |
| 400 | `Imports.ClinicRequired` | Aşı tablosu var, `clinicId` yok |
| 404 | `Imports.BatchNotFound` | Parti yok veya başka kiracıya ait |
| 404 | `Clinics.NotFound` | Klinik yok/kiracıya ait değil (mevcut kod) |
| 403 | — | İzin yok; klinik yazma erişimi yok (`Vaccinations.*` mevcut kodlar) |
| 409 | `Imports.DuplicateFile` | Aynı içerik özeti aktif partide (`batchId` ile) |
| 409 | `Imports.RequestIdConflict` | Aynı `requestId`, farklı içerik |
| 409 | `Imports.AnotherJobRunning` | Kiracıda aktif iş (veya süresi dolmamış doğrulama kirası) |
| 410 | `Imports.PayloadPurged` | Satır verisi saklama süresi sonunda temizlendi (`rows`, `errors`) |
| 409 | `Imports.InvalidState` | Durum bu işleme uygun değil |
| 409 | `Imports.ConcurrencyConflict` | Parti aynı anda başka istekle değişti (rowversion); yeniden okuyup deneyin |
| 409 | `Imports.TooManyOpenDrafts` | Kiracıda 5 açık taslak parti var |
| 400 | `Imports.ClinicContextMismatch` | `clinicId` aktif klinik bağlamıyla çelişiyor |
| 409 | `Imports.ValidationStale` | `validationId` güncel değil (eşleme değişti/yeniden doğrulandı) |
| 409 | `Imports.HasErrorRows` | `FailIfAny` ve hatalı satır var |
| 409 | `Imports.ReminderImpactChanged` | Onaylanan e-posta sayısı güncel değil |
| 409 | `Imports.RollbackBlocked` | `Fail` politikasında engelli kayıt var |
| 403/409 | `Tenants.TenantInactive` / abonelik yazma koruması | Mevcut kiracı kodları |
| 500 | — | Beklenmeyen; parti `CommitFailed` olur, uygulanmış satırlar korunur ve geri alınabilir |

Satır düzeyi kodlar (`rows` çıktısında, HTTP hatası değil): **tam liste, önem derecesi ve kural bölüm 6.4'tedir** (kodda tek yerde: `ImportRules`). Dosya düzeyi (HTTP hatası değil, `preview.blockers`): `Imports.OwnerLinkUnavailable`, `Imports.PetLinkUnavailable`, `Imports.NoImportableRows`. Yükleme uyarısı: `Imports.EncodingSuspect`, `Imports.TableEmpty` (bölüm 7.2). Mevcut oluşturma kurallarından gelen kodlar (`Pets.BreedSpeciesMismatch`, `Pets.BirthDateInFuture`, `Vaccinations.AppliedAtMustNotBeFuture`, `Vaccinations.DueAtMustBeAfterAppliedAt`, `Vaccinations.DueTooFarInFuture`, `VaccineDefinitions.InvalidSpecies`) aynen taşınır.

## 9) Geri alma ve güvenli silme kuralı

Geri alma **yalnız bu partinin oluşturduğu** kayıtları siler (satır sonucu `Created` olanlar). `Matched` (mevcut) kayıtlar asla silinmez. Ürün kodu yeni silme yolu açmaz; silme yalnız geri alma işi içinde, aşağıdaki sırayla ve kurallarla yapılır: **aşı → hasta → müşteri**.

Bir kayıt **silinebilir** sayılır ancak ve ancak:

1. Aşı: partinin oluşturduğu aşı kaydıdır ve hiçbir muayeneye bağlı değildir (`ExaminationId` null; içe aktarma zaten böyle üretir, sonradan bağlandıysa engel).
2. Hasta: partinin oluşturduğudur ve kendisine bağlı **parti dışı** hiçbir kayıt yoktur: aşı (elle eklenmiş), randevu, geliş (`Visit`), muayene, tedavi, reçete, lab sonucu, yatış, uygulama kaydı (`Administration`), ücret, ödeme/tahsis. Kalan tek bağ partinin kendi aşıları olmalıdır.
3. Müşteri: partinin oluşturduğudur, **hiç güncellenmemiştir** (`UpdatedAtUtc` null), bağlı tüm hastaları da partinin oluşturduğu ve silinebilir olanlardır ve müşteriye bağlı ücret/ödeme/randevu/geliş yoktur.
4. Ödeme, ücret ve muayene içermediği garantisi **bağımlılık denetimiyle** sağlanır (varsayım değil): bu tablolardan biri kayda bağlıysa kayıt silinmez.

Silinmeyen kayıt `Kept` ve nedeni (`HasVisits`, `HasCharges`, `HasExaminations`, `ManuallyAddedVaccination`, `ClientModified`, `Matched`, …) satır sonucunda ve `rollback-preview` çıktısında görünür. Silinen müşteri/hasta için Query DB projeksiyon tarafında silme olayı üretilir (mevcut `*.created.v1` / `*.updated.v1` yanında yeni silme olayı tipi gerekir; bkz. tasarım belgesi bölüm 9.4). Geri alma audit'e yazılır (aktör, gerekçe, sayılar). `ReminderDispatchLog` kayıtları silinmez (gönderim tarihçesi).

## 10) Audit ve gizlilik

- Audit (`AuditBehavior`, `IAuditableRequest`): parti oluşturma, eşleme değişikliği, doğrulama, uygulama, hatırlatma serbest bırakma, geri alma, taslak silme, hata raporu indirme. Payload'a **dosya içeriği ve satır verisi yazılmaz**; yalnız parti kimliği, sayılar, içerik özeti, durum.
- Satır verisi (kişisel veri) yalnız `ImportBatchRows` tablosunda tutulur; `Committed/RolledBack/Discarded/Expired` sonrası **30 gün** içinde `SourceJson`/`NormalizedJson` temizlenir (`payloadPurgedAtUtc`); sayılar ve sonuç özeti kalır `[K9]`. Temizlenmiş partide `rows`/`errors` uçları `410 Imports.PayloadPurged` döner. **B3 (saklama zamanlaması hazır, temizlik işi B5):** `ImportBatch.PayloadPurgeDueAtUtc` (hesaplanan alan; sütun yok) `Committed`, `RolledBack`, `PartiallyRolledBack` ve `Expired` partide son durum değişikliğinden (`updatedAtUtc`) **30 gün** sonrasıdır, temizlenmişse veya başka durumdaysa null; `Discarded` parti taslak silinirken zaten temizlenir. `ImportBatch.PurgePayload` `payloadPurgedAtUtc`'yi doldurur, `ImportBatchRow.PurgePayload` kaynak/normalize/sorun alanlarını (kişisel veri) boşaltır; süresi gelen partileri seçen sorgu (`ImportBatchesDuePayloadPurgeSpec`) B5 işçisi içindir. Süre `Imports` yapılandırmasına bağlanmadı: sabit 30 gün (K9; KVKK danışmanı farklı süre derse tek sabit değişir).
- Günlüklere (Serilog) satır verisi, telefon, e-posta, ad yazılmaz; yalnız parti kimliği, tablo, satır numarası, kod.
- Ham dosya sunucuda **saklanmaz** (dosya deposu kodu yok); yükleme sırasında bellekte okunur, satırlar normalize edilmiş biçimde saklanır.

## 11) Geriye uyumluluk / etkilenen mevcut kodlar

- `Vaccination`: nullable `ImportBatchId` alanı eklenir (migration); mevcut sorgu/DTO değişmez (isteğe bağlı olarak okuma DTO'larına alan eklenebilir, ayrı karar).
- `ReminderProcessorService.ProcessVaccinationRemindersAsync`: aday sorgusuna "içe aktarılmış ve serbest bırakılmamış değil" koşulu eklenir (bölüm 6.3).
- Yeni: `ImportBatch`, `ImportBatchRow`, izinler, arka plan işçisi, silme olayı tipleri (Client/Pet).
- **B1 migration `AddDataImportBatches`** (tek migration): `ImportBatches`, `ImportBatchRows` tabloları, `Vaccinations.ImportBatchId` (nullable, filtreli indeks, `ImportBatches`'e `NO ACTION` FK). Veritabanı güvenceleri: `UX_ImportBatches_TenantId_RequestId`; `UX_ImportBatches_TenantId_ContentHash` (özet dolu ve durum `CommitFailed/RolledBack/Discarded/Expired` dışı); `UX_ImportBatches_TenantId_ActiveJob` (durum `Validating/Committing/RollingBack` iken kiracı başına tek satır); `UX_ImportBatchRows_BatchId_Table_RowNumber`; `ImportBatchRows.TargetEntityId` indeksi; `ImportBatches.RowVersion`.
- B1 audit eylemleri: `ImportBatch.Create` (hedef `RequestId`), `ImportBatch.Discard` (hedef `ImportBatchId`). Payload yalnız `requestId`, `name`, `clinicId` / parti kimliği taşır.
- B2 audit eylemleri: `ImportBatch.Create` (payload'a ek olarak `encoding`, `delimiter`; dosya içeriği ve satır verisi yok), `ImportBatch.Mapping` (hedef `ImportBatchId`; eşleme gövdesi yok). B2 migration gerektirmez: yeni alan yok, yalnız mevcut `ContentHash`, sayaç `Total` alanları ve `ImportBatchRows` kullanılır.
- B3 audit eylemleri: `ImportBatch.Validate` (hedef `ImportBatchId`; payload yalnız parti kimliği), `ImportBatch.ErrorReport` (hata raporu indirme; hedef `ImportBatchId`, `format`). B3 migration gerektirmez: satır sonuçları mevcut `ImportBatchRows` sütunlarına (`NormalizedJson`, `ValidationStatus`, `IssuesJson`), sayaçlar mevcut parti sayaç sütunlarına, kira `LeaseOwner`/`LeaseUntilUtc` sütunlarına yazılır. Değişen mevcut kod: `ImportBatch` (kira, saklama zamanlaması), `ImportBatchRow` (doğrulama sonucu, temizleme), `VaccinationDueAtWindow` (`MaxFutureYears` sabiti ortak).
- Ortak hata zarfı: `Error` değeri isteğe bağlı `Extensions` taşır; `ProblemDetails.extensions` alanına (örn. `batchId`) kopyalanır.
- Mevcut müşteri/hasta/aşı uçlarının sözleşmesi **değişmez**.
