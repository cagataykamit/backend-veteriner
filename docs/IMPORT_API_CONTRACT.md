# Veri içe aktarma (müşteri + hasta + aşı geçmişi) API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: ADR-014 K8 (ürün deposu), `ASAMA9_READINESS.md` Dilim 7. Tasarım gerekçesi, akış, riskler ve kullanıcı kararları: `ASAMA9_IMPORT_DESIGN.md`. Tüm uçlar `api/v1` altında, JSON (dosya yükleme ve indirme hariç), standart `ProblemDetails` + `extensions.code` hata zarfı, sayfalama mevcut liste uçlarındaki biçimde.

> **Durum:** TASARIM TASLAĞI. Kod yok, migration yok. Bu sözleşme, `ASAMA9_IMPORT_DESIGN.md` bölüm 12'deki kullanıcı kararları alınmadan **kesinleşmez**; kararlara bağlı yerler `[K#]` ile işaretli ve önerilen seçenekle yazılmıştır.

## 1) Kapsam ve sınırlar

- **Var:** Müşteri (`Client`), hasta (`Pet`) ve **uygulanmış** aşı geçmişi (`Vaccination`, `Status = Applied`).
- **Yok (ilk sürüm):** muayene, tedavi, reçete, lab, yatış, randevu, geliş, ücret, ödeme, stok, uygulama kaydı (`Administration`; stok düşer, bu yüzden bilinçli olarak dışarıda), planlı (`Scheduled`) aşı.
- Kiracı yalnızca JWT/bağlamdan çözülür; istekte `tenantId` yoktur. Müşteri ve hasta **kiracı geneli**dir (klinik filtresi yok); aşı kaydı için tek bir `clinicId` partide seçilir (bölüm 4.3).
- İçe aktarma, mevcut oluşturma yollarının kurallarını **yeniden yazmaz**: telefon normalizasyonu (`TurkishMobilePhone`), e-posta/ad kuralları, müşteri ve hayvan mükerrer kontrolü, tür/ırk doğrulaması, abonelik yazma koruması, outbox olayları aynen uygulanır. Yalnız aşı **tarih penceresi** içe aktarmaya özgüdür (bölüm 6.3).

## 2) Yetki (yeni izinler) `[K8]`

Seed ile gelir (`PermissionCatalog` + `RolePermissionBindingSeeder`); varsayılan olarak yalnız **Yönetici** rolüne bağlanır. Mevcut özel roller elle almalıdır.

| İzin | Uçlar |
|------|-------|
| `Imports.Read` | Parti listesi/ayrıntısı, önizleme, satırlar, hata raporu, şablon indirme |
| `Imports.Manage` | Parti oluşturma (yükleme), eşleme, doğrulama, taslağı silme |
| `Imports.Commit` | Onaylayıp uygulama, hatırlatmaları açma, geri alma |

Ek zorunlu izinler (yetki yükseltmeyi önlemek için): `commit` çağıranın `Clients.Create` (müşteri içeren parti), `Pets.Create` (hasta içeren parti), `Vaccinations.Create` (aşı içeren parti) izinlerine de sahip olmasını gerektirir; biri eksikse `403`. Başka kiracının partisi `404` döner.

## 3) Yaşam döngüsü

Parti durumları (`status`): `Uploaded` → `Mapped` → `Validating` → `Validated` | `ValidationFailed` → `Committing` → `Committed` | `CommitFailed`; `Committed` → `RollingBack` → `RolledBack` | `PartiallyRolledBack` | `RollbackFailed`; taslak aşamalarından `Discarded`; 7 gün hareketsiz taslak `Expired`.

- Doğrulama ve uygulama **arka plan işidir** (`202 Accepted`); istemci `GET /imports/batches/{id}` ile durumu ve ilerlemeyi yoklar (önerilen aralık 2 sn).
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

### 4.2 Şablon (yönetici indirir)

`GET /imports/template?format=xlsx` (üç sayfa + `Aciklama` sayfası: kolon açıklamaları, örnek satır, kabul edilen değerler) ve `?format=csv&table=customers|pets|vaccinations`. Şablon başlıkları aşağıdaki **kanonik başlıklardır**; ayrıca kanonik başlıklar için kabul edilen takma adlar (`aliases`) sütun eşleme önerisinde kullanılır.

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

## 7) Uçlar

Hata zarfı için bölüm 8. `{id}` = parti kimliği.

### 7.1 `GET /imports/template`
`?format=xlsx` (tek dosya) veya `?format=csv&table=customers|pets|vaccinations`. `Imports.Read`. `200` dosya (`Content-Disposition`).

### 7.2 `POST /imports/batches` (parti oluştur)
`multipart/form-data`, `Imports.Manage`. Alanlar:

| Alan | Zorunlu | Not |
|------|---------|-----|
| `requestId` | **Evet** (Guid) | Aynı kiracı + aynı `requestId` ikinci çağrıda **aynı partiyi** döndürür (`200`); farklı içerikle gelirse `409 Imports.RequestIdConflict`. |
| `file` | Evet (XLSX) | XLSX: üç sayfa içerebilir. |
| `customersFile`, `petsFile`, `vaccinationsFile` | CSV için | Her biri ayrı CSV. `file` ile birlikte verilemez. |
| `clinicId` | Aşı varsa evet | Bölüm 4.3. |
| `name` | Hayır | Listede görünen etiket (≤ 100); kişisel veri içermemeli. |
| `encoding`, `delimiter` | Hayır | CSV zorlaması. |

İşlem: dosya boyut/biçim denetimi, **içerik özeti (SHA-256)** hesabı, başlıkların okunması, kanonik başlıklarla otomatik eşleme önerisi. Satırlar bu aşamada saklanır (`Uploaded`) ama doğrulanmamıştır.

Aynı kiracıda **aynı içerik özetli** ve durumu `Discarded/Expired/RolledBack/CommitFailed` **olmayan** parti varsa `409 Imports.DuplicateFile` + `extensions.batchId` (aynı dosya iki kez yüklenip çift kayıt oluşmasın; geri alınmış partinin dosyası yeniden yüklenebilir).

`201 Created` (`Location` → `GET /imports/batches/{id}`):

```json
{
  "id": "guid", "status": "Uploaded", "name": "string?", "clinicId": "guid?",
  "tables": [
    { "table": "customers", "rowCount": 1200,
      "columns": [ { "source": "Ad Soyad", "suggested": "AdSoyad", "confidence": "Exact|Alias|None" } ] }
  ],
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

### 7.4 `POST /imports/batches/{id}/validate`
`Imports.Manage`; durum `Mapped|Validated|ValidationFailed`. **Veritabanına müşteri/hasta/aşı yazmaz** (yalnız parti ve satır sonuçları). `202 Accepted` + parti özeti (`status = Validating`). Bittiğinde `Validated` (satır hataları olsa da) veya `ValidationFailed` (dosya düzeyi sorun: zorunlu sütun yok, yapısal bozukluk, hiç geçerli satır yok). Aynı kiracıda başka aktif iş varsa `409 Imports.AnotherJobRunning`. Tekrar çağrılabilir; idempotent (aynı eşleme → aynı sonuç, yeni `validationId`).

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

### 7.6 `GET /imports/batches/{id}/preview` (kuru çalıştırma sonucu)
`Imports.Read`; durum `Validated`, `Committing`, `Committed`, … Yanıt:

```json
{
  "validationId": "guid",
  "summary": { "customers": {…}, "pets": {…}, "vaccinations": {…} },
  "unmappedValues": [ { "kind": "species", "source": "Kopek", "rowCount": 41 } ],
  "reminderImpact": { "vaccinationsWithFutureDueDate": 830, "clientsWithEmail": 410, "emailsWithinReminderWindow": 95 },
  "issueCounts": [ { "code": "Imports.PhoneInvalid", "severity": "Warning", "rowCount": 37 } ],
  "canCommit": true,
  "blockers": [ "Imports.VaccineUnmapped" ]
}
```
`canCommit`: en az bir geçerli satır var, eşlenmemiş değer yok (veya `targetId: null` ile bilinçli atlanmış), parti `Validated` ve doğrulama güncel. `reminderImpact` yalnız bilgidir; hatırlatma varsayılan olarak kapalıdır (bölüm 6.3).

### 7.7 `GET /imports/batches/{id}/rows`
`Imports.Read`. `?table=customers|pets|vaccinations&status=Valid|Warning|Error|Created|Matched|Skipped|Failed&code=&page=&pageSize=` (`pageSize` ≤ 200). Her öğe: `table`, `rowNumber`, `validation` (`status`), `apply` (`status`, `targetId?`), `issues[]`, `values` (normalize edilmiş özet alanlar). Kişisel veri içerdiğinden `Cache-Control: no-store`.

### 7.8 `GET /imports/batches/{id}/errors?format=xlsx|csv`
`Imports.Read`. Hata raporu indirme: yalnız `Error` (ve `Failed`) satırlar; **orijinal kaynak sütunları + `SatirNo`, `Durum`, `HataKodu`, `Aciklama`** (Türkçe). Kullanıcı düzeltip aynı şablonla **yeni parti** olarak yükleyebilir; daha önce oluşan kayıtlar `Matched`/`DuplicateVaccination` olacağından çift kayıt oluşmaz. İndirme audit'e yazılır. Formül enjeksiyonu: `= + - @` ile başlayan hücre değerleri başına `'` eklenir. `Cache-Control: no-store`.

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

## 8) Hata kodları

| HTTP | Kod | Durum |
|------|-----|-------|
| 400 | `Validation.FluentValidation` | İstek biçimi (eksik `requestId`, geçersiz sayfa vb.) |
| 400 | `Imports.UnsupportedFile` / `Imports.FileTooLarge` / `Imports.TooManyRows` / `Imports.EmptyFile` | Dosya düzeyi |
| 400 | `Imports.MappingIncomplete` / `Imports.MappingUnknownColumn` / `Imports.ValueMappingTargetInvalid` | Eşleme |
| 400 | `Imports.ClinicRequired` | Aşı tablosu var, `clinicId` yok |
| 404 | `Imports.BatchNotFound` | Parti yok veya başka kiracıya ait |
| 404 | `Clinics.NotFound` | Klinik yok/kiracıya ait değil (mevcut kod) |
| 403 | — | İzin yok; klinik yazma erişimi yok (`Vaccinations.*` mevcut kodlar) |
| 409 | `Imports.DuplicateFile` | Aynı içerik özeti aktif partide (`batchId` ile) |
| 409 | `Imports.RequestIdConflict` | Aynı `requestId`, farklı içerik |
| 409 | `Imports.AnotherJobRunning` | Kiracıda aktif iş |
| 409 | `Imports.InvalidState` | Durum bu işleme uygun değil |
| 409 | `Imports.ValidationStale` | `validationId` güncel değil (eşleme değişti/yeniden doğrulandı) |
| 409 | `Imports.HasErrorRows` | `FailIfAny` ve hatalı satır var |
| 409 | `Imports.ReminderImpactChanged` | Onaylanan e-posta sayısı güncel değil |
| 409 | `Imports.RollbackBlocked` | `Fail` politikasında engelli kayıt var |
| 403/409 | `Tenants.TenantInactive` / abonelik yazma koruması | Mevcut kiracı kodları |
| 500 | — | Beklenmeyen; parti `CommitFailed` olur, uygulanmış satırlar korunur ve geri alınabilir |

Satır düzeyi kodlar (`rows` çıktısında, HTTP hatası değil): `Imports.RequiredMissing`, `Imports.NameTooShort`, `Imports.PhoneInvalid` (uyarı), `Imports.EmailInvalid` (uyarı), `Imports.DateInvalid`, `Imports.DateOutOfRange`, `Imports.DateFormatAmbiguous`, `Imports.OwnerNotFound`, `Imports.PetNotFound`, `Imports.SpeciesUnmapped`, `Imports.VaccineUnmapped`, `Imports.BreedFreeText` (bilgi), `Imports.MicrochipInvalid`, `Imports.MicrochipScientificNotation`, `Imports.MicrochipConflict`, `Imports.SharedPhone` (bilgi), `Imports.PossibleDuplicate` (uyarı), `Imports.DuplicateInFile`, `Imports.DuplicateVaccination`, `Imports.DuplicateReference`, `Imports.EncodingSuspect`, ayrıca mevcut oluşturma kurallarından gelen kodlar (`Pets.BreedSpeciesMismatch`, `Pets.BirthDateInFuture`, `Vaccinations.DueAtMustBeAfterAppliedAt` …) aynen taşınır.

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
- Satır verisi (kişisel veri) yalnız `ImportBatchRows` tablosunda tutulur; `Committed/RolledBack/Discarded/Expired` sonrası **30 gün** içinde `SourceJson`/`NormalizedJson` temizlenir (`payloadPurgedAtUtc`); sayılar ve sonuç özeti kalır `[K9]`. Temizlenmiş partide `rows`/`errors` uçları `410 Imports.PayloadPurged` döner.
- Günlüklere (Serilog) satır verisi, telefon, e-posta, ad yazılmaz; yalnız parti kimliği, tablo, satır numarası, kod.
- Ham dosya sunucuda **saklanmaz** (dosya deposu kodu yok); yükleme sırasında bellekte okunur, satırlar normalize edilmiş biçimde saklanır.

## 11) Geriye uyumluluk / etkilenen mevcut kodlar

- `Vaccination`: nullable `ImportBatchId` alanı eklenir (migration); mevcut sorgu/DTO değişmez (isteğe bağlı olarak okuma DTO'larına alan eklenebilir, ayrı karar).
- `ReminderProcessorService.ProcessVaccinationRemindersAsync`: aday sorgusuna "içe aktarılmış ve serbest bırakılmamış değil" koşulu eklenir (bölüm 6.3).
- Yeni: `ImportBatch`, `ImportBatchRow`, izinler, arka plan işçisi, silme olayı tipleri (Client/Pet).
- Mevcut müşteri/hasta/aşı uçlarının sözleşmesi **değişmez**.
