# Kayıt defterleri API sözleşmesi: Muayene, Reçete, Aşı, İlaç, Narkotik/Psikotropik defterleri ve Aşı Bilgi raporu

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-015 (parti bazlı stok ve kayıt defterleri), `KAYIT-DEFTERLERI-RAKIP-BICIMI.md` (ürün deposu). İlgili: `OUTSTANDING_BALANCE_REPORT_API_CONTRACT.md` (rapor ucu deseni), `EXAMINATIONS_WORKSPACE_API_CONTRACT.md`, `PRESCRIPTION_ITEMS_API_CONTRACT.md`, `ADMINISTRATIONS_API_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme. Salt okuma; **migration yok**, yeni izin yok. Bölüm 1–11: Muayene ve Reçete defterleri. **Bölüm 12–18: parti (lot) verisinden türetilen dört çıktı** (Aşı Kayıt Defteri, İlaç Kayıt Defteri, Narkotik ve Psikotropik Ürünler Stok ve Sarf Defteri, Aşı Bilgi Raporu); ortak kurallar (bölüm 1, 6) bunlar için de geçerlidir, farklar bölüm 12'de listelenir.
>
> **Resmî biçime uygunluk doğrulanmadı.** Sütunlar bir rakip yazılımın ekran görüntülerinden alınmıştır; yasal dayanak ve resmî biçim (sütun seti, üstbilgi, imza alanı, sayfa numarası) doğrulanmamıştır. Biçim doğrulanınca yalnız sütun tanımı (bölüm 6) ve üstbilgi/imza alanı güncellenir. Bu not arayüzde ve çıktıda görünür olmalıdır.

Kapsam (bölüm 1–11): **Muayene** ve **Reçete** defterleri. Aşı, ilaç, narkotik/psikotropik defterleri ve Aşı Bilgi raporu parti (lot) bazlı stoktan türetilir (ADR-015, `BATCH_STOCK_API_CONTRACT.md`); bölüm 12–18.

---

## 1) Ortak kurallar

### Uçlar

| Defter | Liste (JSON, sayfalı) | CSV | XLSX | İzin |
|---|---|---|---|---|
| Muayene | `GET /api/v1/reports/registers/examinations` | `…/examinations/export` | `…/examinations/export-xlsx` | `Examinations.Read` |
| Reçete | `GET /api/v1/reports/registers/prescriptions` | `…/prescriptions/export` | `…/prescriptions/export-xlsx` | `Prescriptions.Read` |

Yeni izin yoktur. Yoksa 403; token yoksa 401. Defter, sahibin adı/telefonu/adresini içerir ama `Clients.Read` istemez (borç raporundaki ilke): defter, ilgili klinik kaydını okuyabilen herkese açıktır.

### Sorgu parametreleri (üç uç da aynı)

| Parametre | Tip | Not |
|---|---|---|
| `from` | date (`yyyy-MM-dd`) | Zorunlu. **İstanbul takvim günü**, dahil |
| `to` | date (`yyyy-MM-dd`) | Zorunlu. İstanbul takvim günü, dahil. `from ≤ to` |
| `clinicId` | guid? | Klinik kapsamı (aşağıda) |
| `includeVoided` | bool | Yalnız muayene defteri; varsayılan `false` (bölüm 3) |
| `page` | int | Yalnız JSON. Varsayılan 1 |
| `pageSize` | int | Yalnız JSON. Varsayılan 50, en çok 200 (üstü sınırlanır) |

Dışa aktarma uçlarında `page`/`pageSize` yoktur; tüm eşleşen satırlar gelir.

**Tarih aralığı.** `from` günün 00:00'ı, `to` günün sonu (ertesi günün 00:00'ı, hariç) Europe/Istanbul saatidir; UTC karşılığı `OperationDayBounds` ile hesaplanır, kayıt tarihleri (`ExaminedAtUtc`, `PrescribedAtUtc`) bu yarı açık aralıkla `[başlangıç, bitiş)` karşılaştırılır. Aralık en çok **366 gün** (`to − from`) olabilir (defterler yıllık okunur; bu rapor Rapor Merkezi'nin 93 günlük sınırına tabi değildir).

**Klinik kapsamı** ödeme/borç raporlarıyla aynı çözümleyicidir (`IClinicReadScopeResolver`): `clinicId` (veya JWT/header `clinic_id` bağlamı) varsa yalnız o klinik; bağlam yoksa kullanıcının erişebildiği tüm klinikler (Admin/Owner: kiracı geneli). JWT `clinic_id` ile sorgu `clinicId` farklıysa `Context.ClinicConflict` (403). Atanmamış klinik `Clinics.AccessDenied` (403); kiracıda olmayan klinik `Clinics.NotFound` (404). Kiracı bağlamı zorunlu (`Tenants.ContextMissing`, 400). Başka kiracının kaydı görünmez.

**Okuma** Command DB'dedir (klinik kaydı, eventual-consistent projeksiyondan okunmaz).

### Sıra No

Süzgeçlere uyan satırların **tarih sıralı** konumudur (1'den başlar; sayfa 2'nin ilk satırı `(page−1)·pageSize + 1`). Sıralama deterministiktir (kayıt tarihi, sonra oluşturulma zamanı, sonra kimlik); sayfalar kesişmez, atlamaz; ekran, CSV ve XLSX aynı numarayı verir. Numara **süzgece göreceli**dir: aralık değişince değişir. Resmî, aralıktan bağımsız cilt/sıra numarası kalıcı bir sayaç ister (bölüm 7, "Kayıt sıra numarası").

### Dışa aktarma

- CSV: UTF-8 BOM, `;` ayırıcı (Excel TR), tarih `dd.MM.yyyy` (Europe/Istanbul), teknik ID kolonu yok. XLSX: tarih hücresi tarih biçimli, sayı hücresi sayı biçimli, sabit başlık satırı, süzgeç.
- Ekran ile dışa aktarma aynı okuyucu çağrısından geçer (aynı süzgeç, aynı sıra, aynı Sıra No); testle kanıtlanır.
- Satır tavanı `ReportsSharedLimits.MaxExportRows` (50.000); aşılırsa `Registers.ExportTooManyRows` (400): "filtreyi daraltın".
- Dosya adı: `muayene-defteri-yyyyMMdd.csv|xlsx`, `recete-defteri-yyyyMMdd.csv|xlsx` (İstanbul tarihi).
- Boş (eksik alan) sütunlar dosyada başlığıyla ve boş hücreyle yer alır; böylece sütun seti defter biçimine uygun kalır ve alan eklenince değişmez.

### PDF / yazdırma

Backend PDF üretmez; projede PDF paketi yoktur (yalnız ClosedXML) ve yeni paket eklenmedi. Yazdırma/PDF için önerilen yol **frontend yazdırma görünümüdür**: ekran bu sözleşmenin JSON'unu sayfa sayfa gösterir, tarayıcıdan yazdır/PDF'e kaydet. Üstbilgi, imza alanı ve sayfa numarası resmî biçim doğrulanınca netleşir. Sunucu tarafı PDF istenirse bir paket kararı gerekir (ayrı onay).

---

## 2) Muayene defteri: `GET /api/v1/reports/registers/examinations`

Her satır **bir muayene**dir. Taslak ve kesinleşmiş muayeneler birlikte gelir (`isFinalized` ile ayırt edilir).

Yanıt `{ totalCount, items }`: `totalCount` süzgece uyan muayene sayısı (tüm sayfalar), `items` sayfadaki satırlar.

`ExaminationRegisterItemDto`:

| Alan | Tip | Defter sütunu | Not |
|---|---|---|---|
| `rowNumber` | int | Sıra No | |
| `examinationId` | guid | | Detay bağlantısı için (dosyada yok) |
| `examinedAtUtc` | datetime | Muayene Tarihi | Dosyada İstanbul günü |
| `isFinalized` | bool | | Dosyada yok |
| `ownerName` | string | Hasta Sahibinin Adı Soyadı | |
| `ownerPhone` | string? | Telefonu | `905XXXXXXXXX` veya null |
| `ownerAddress` | string? | Adresi | `Client.Address` |
| `petName` | string | Hasta · Ad | |
| `species` | string | Hasta · Tür | |
| `breed` | string? | Hasta · Irk | Katalog ırkı, yoksa serbest metin |
| `gender` | string? | Hasta · Cinsiyet | `Erkek` / `Dişi` / null |
| `age` | string? | Hasta · Yaş | **Muayene tarihindeki** yaş (doğum tarihinden): `N yıl M ay`, 1 yıldan küçükse `M ay`, 1 aydan küçükse `G gün`; doğum tarihi yoksa null |
| `cardNumber` | string? | Hasta · Kart No | **Her zaman null** (eksik alan) |
| `microchipNumber` | string? | Hasta · Çip No | |
| `protocolNumber` | string? | Hasta · Protokol No | `Pet.SpecialProtocolNumber` |
| `ePrescriptionId` | string? | Hasta · e-Reçete Kimlik | **Her zaman null** (eksik alan) |
| `complaint` | string | Hastanın Şikayeti | `VisitReason` |
| `diagnosis` | string? | Teşhis Edilen Hastalık | `Assessment` |
| `treatments` | string[] | Tedavi Şekli, Reçete Bilgileri, Satışa Eklenen İlaçlar (tek sütun) | Muayeneye bağlı tedavilerin başlığı (tarih sıralı); tedavi yoksa muayenenin `Plan` metni (tek eleman), o da yoksa boş |
| `prescriptions` | `RegisterPrescriptionDto[]` | (tek sütun, yukarıda) | Muayeneye bağlı reçeteler: `title`, `items[]` (`name`, `quantity?`, `instructions?`); satırsız (eski) reçetede `items` boş |
| `administrations` | `RegisterAdministrationDto[]` | (tek sütun, yukarıda) | Muayeneye bağlı uygulamalar (bölüm 3): `productName`, `quantity`, `unit`, `addedAfterFinalization`, `isVoided` |
| `veterinarianName` | string? | Veteriner Hekimin Adı Soyadı | Bölüm 4 |

**Karar (2026-10-14):** dosyada (ve ekranda) `treatments`, `prescriptions`, `administrations` **TEK sütun**dur (rakip E-vet biçimi); başlık: `Tedavi Şekli, Reçete Bilgileri, Satışa Eklenen İlaçlar`. API satırı yapısal alanları ayrı döndürmeye devam eder; birleştirme kodda tek yerde yapılır (bölüm 6, `ExaminationRegisterColumns`). Hücre biçimi (frontend `register-definitions.ts` aynısını kullanır): önce tedaviler, sonra `Reçete: ...`, sonra `Satış: ...`; boş bölüm atlanır; bölümler ve kayıtlar `; ` ile ayrılır. Reçete kaydı `Başlık: İlaç ×Miktar, İlaç ×Miktar`; satış kaydı `Ürün Miktar Birim`, sonradan eklenen için ` (sonradan eklendi)`, iptal için ` (iptal)` eki. Örnek: `Serum; Enjeksiyon; Reçete: Reçete 1: Amoksilin ×2,5, Probiyotik; Satış: Aşı X 1 doz (sonradan eklendi)`.

Sıralama: `examinedAtUtc`, `createdAtUtc`, `id` artan.

## 3) Sonradan eklenen ve iptal edilen kayıtlar (ADR-013)

- **Sonradan eklenen uygulama** (`Administration.AddedAfterFinalization`): satırda `addedAfterFinalization = true`; dosyada ` (sonradan eklendi)` eki. Gerekçe (`LateEntryReason`) deftere yazılmaz (ayrıntı klinik kayıttadır).
- **İptal edilen uygulama** (`Administration.VoidedAtUtc`): `includeVoided=false` (varsayılan) iken muayene satırına **hiç girmez**; `true` iken `isVoided = true` ile ve ` (iptal)` ekiyle görünür. Gerekçe deftere yazılmaz.
- Muayene ve reçetede iptal kavramı yoktur (yanlış kayıt düzeltmesi ek notla yapılır); `includeVoided` bu iki varlığı etkilemez ve reçete defterinde yoktur.

## 4) Hekim adı kuralı (tek yerde)

`veterinarianName`: muayenenin geliş (Visit) kaydındaki **sorumlu hekim** (`ResponsibleVeterinarianUserId`); geliş yoksa veya sorumlu hekim atanmamışsa **null** (boş). Muayeneyi kesinleştiren kullanıcı **yedek değildir** (karar 2026-10-14: admin/resepsiyon kesinleştirince resmî defterde hekim yerine o kişi görünüyordu). Ad: kullanıcının `DisplayName` değeri, yoksa e-postası. Reçete defterinde aynı kural, reçetenin bağlı olduğu muayene üzerinden uygulanır; muayenesiz reçetede hekim **boş**tur (reçetede hekim alanı yok; bölüm 7).

## 5) Reçete defteri: `GET /api/v1/reports/registers/prescriptions`

Her reçete bir `entry`, içindeki ilaç satırları `items` olur; sayfalama ve sayım reçete bazlıdır, `totalCount` süzgece uyan reçete sayısıdır. **Dosyada reçete başına bir satır** vardır; ilaçlar son sütunda **tek hücrede birleşik metin** olarak gelir (rakip biçimi: "İlacın Adı, Farmakolojik Şekli, Ambalajı, Adedi"). Hücre biçimi: `Ad (farmakolojik şekil, ambalaj) ×Adet`, ilaçlar `; ` ile ayrılır, boş parçalar atlanır (şekil ve ambalaj şu an hep boş). Satırı olmayan (eski, serbest metin) reçetede hücre reçete `Content` metnidir.

`PrescriptionRegisterEntryDto`:

| Alan | Tip | Defter sütunu | Not |
|---|---|---|---|
| `rowNumber` | int | Sıra No | Reçete sırası |
| `prescriptionId` | guid | | Dosyada yok |
| `prescribedAtUtc` | datetime | Reçetenin · Tarihi | |
| `prescriptionClass` | string? | Reçetenin · Sınıfı | **Her zaman null** (eksik alan) |
| `volumeSequenceNumber` | string? | Reçetenin · Cilt/Sıra No | **Her zaman null** (eksik alan) |
| `veterinarianName` | string? | Veteriner Hekimin · Adı Soyadı | Bölüm 4 |
| `veterinarianAddress` | string? | Veteriner Hekimin · Adresi | **Kliniğin adresi** (`Clinic.Address`); hekime özel adres yok (bölüm 7) |
| `ownerName` | string | Hayvan Sahibinin · Adı Soyadı | |
| `ownerAddress` | string? | Hayvan Sahibinin · Adresi | `Client.Address` |
| `species` | string | Hayvanın · Türü | |
| `earTagNumber` | string? | Hayvanın · Kulak/İşletme No | **Her zaman null** (eksik alan) |
| `animalCount` | int | Hayvanın · Sayısı | Her zaman 1 (reçete tek hayvanadır) |
| `items` | `PrescriptionRegisterItemDto[]` | İlacın Adı, Farmakolojik Şekli, Ambalajı, Adedi (tek sütun) | Aşağıda; satırsız reçetede tek öğe (`name = Content`) |

`PrescriptionRegisterItemDto`: `name` (İlacın Adı), `pharmacologicalForm` (Farmakolojik Şekli; **her zaman null**), `packaging` (Ambalajı; **her zaman null**), `quantity` (Adedi, ondalık?).

Gruplu sütunlarda (reçete defterinde Sıra No ve ilaç sütunu hariç tümü; muayene defterinde "Hasta") XLSX başlığı iki katmanlıdır: ilk satır grup başlığı (aynı gruptaki bitişik sütunlar birleştirilir, grupsuz sütun iki satırı kaplar), ikinci satır sütun başlığı; CSV iki satırlı başlığı güvenle taşımadığından tek başlık satırı `Grup · Sütun` biçimindedir. Reçete grupları: Reçetenin, Veteriner Hekimin, Hayvan Sahibinin, Hayvanın; ilaç sütunu grupsuzdur.

Sıralama: `prescribedAtUtc`, `createdAtUtc`, `id` artan; reçete içinde `SortOrder`.

---

## 6) Sütun tanımı (tek yerde)

Defter sütunları kodda **tek bir tanım listesinde** yaşar (`ExaminationRegisterColumns`, `PrescriptionRegisterColumns`): her sütun `grup`, `başlık`, `değer`, `tür` (metin / tarih / sayı) taşır. CSV yazıcısı, XLSX yazıcısı ve (başlık gerektiren her yüz) bu listeyi okur; sütun eklemek, sıralamak, adını değiştirmek veya eksik alanı doldurmak yalnız bu listeyi değiştirir. Yazıcılar iki defter için ortaktır.

## 7) Eksik alanlar (ayrı küçük işler)

Aşağıdakiler bu sürümde **boş** (veya kısmen doludur). Her biri ayrı bir migration'lı iştir; bu görevde yapılmadı.

| Sütun | Defter | Durum | Eklenmesi gereken |
|---|---|---|---|
| Kart No | Muayene | Boş | `Pet.CardNumber`. Not: `Pet.PassportOrTagNumber` "karne/küpe" karışık alandır; kart no ile ilişkisi ürün kararıdır |
| e-Reçete kimliği | Muayene | Boş | `Prescription.EPrescriptionId` (e-reçete entegrasyonu) |
| Reçete sınıfı | Reçete | Boş | `Prescription.PrescriptionClass` (rakipte örnek "B"; sınıf listesi doğrulanmadı) |
| Cilt/Sıra No | Reçete | Boş | Kalıcı kayıt sıra numarası: `Prescription.RegisterNumber` (klinik veya kiracı + yıl bazlı sayaç). Aynısı `Examination` için de gerekir; Sıra No'yu süzgeçten bağımsız ve kalıcı yapar |
| Kulak/İşletme No | Reçete | Boş | `Pet.EarTagNumber` (veya işletme no), özellikle büyük/küçükbaş hayvan |
| Farmakolojik şekil, Ambalaj | Reçete | Boş | `Product` alanları (ADR-015 madde 3, parti bazlı stok işi). `PrescriptionItem` yalnız serbest ad ve opsiyonel `ProductId` taşır; ürün alanları eklenince `ProductId` dolu satırlarda üründen okunur |
| Hekim (muayenesiz reçete) | Reçete | Boş | `Prescription.PrescribedByUserId`. Bugün hekim yalnız bağlı muayeneden çıkar |
| Hekim (gelişsiz/atanmamış muayene) | Muayene | Kısmen | `Examination.ResponsibleVeterinarianUserId`; yalnız Visit sorumlu hekimi, yoksa boş (bölüm 4) |
| Hekimin adresi | Reçete | Kısmen | Klinik adresi kullanılıyor; hekime özel adres/işyeri adresi alanı yok |
| Sahip adresi | İkisi | Var (opsiyonel) | `Client.Address` serbest metin; boş olabilir |

## 8) Hata kodları

| Kod | HTTP |
|---|---|
| `Tenants.ContextMissing` | 400 |
| `Registers.DateRangeInvalid` (`from`/`to` eksik veya `from > to`) | 400 |
| `Registers.RangeTooLong` (> 366 gün) | 400 |
| `Registers.ClinicContextMismatch` (savunma amaçlı; ara katmanda `Context.ClinicConflict`) | 400 |
| `Context.ClinicConflict` (JWT clinic_id ≠ sorgu clinicId) | 403 |
| `Registers.ExportTooManyRows` | 400 |
| `Clinics.AccessDenied` | 403 |
| `Clinics.NotFound` | 404 |
| (yetkisiz) | 401 / 403 |

## 9) Uygulama notları

- Okuyucu: `IRegisterReader<TRow>` (Application) ve iki uygulaması (Infrastructure: `ExaminationRegisterReader`, `PrescriptionRegisterReader`; `OutstandingBalancesReportReader` deseni). JSON ve dışa aktarmalar aynı çağrıyı kullanır.
- Sayım, sıralama ve sayfalama veritabanındadır. Sayfadaki muayeneler tek sorguda hasta/sahip/tür/ırk ile birleştirilir; tedavi, reçete (satırlarıyla), uygulama (ürün adıyla) ve hekim adları sayfa kimlik kümesiyle **birer toplu sorguda** okunur (N+1 yok; dışa aktarma tavanı 50.000 muayene).
- Mevcut indeksler (`TenantId, ClinicId` + tarih) yeterli beklenir; bu sürümde indeks eklenmedi (indeks migration gerektirir, ayrı onay).
- Mevcut `Examinations`/`Prescriptions` rapor ve liste uçlarına dokunulmaz.

## 10) Frontend için (ayrı kart)

Defter sayfaları `Examinations.Read` / `Prescriptions.Read` ile görünür. Süzgeç yalnız tarih aralığı (+ klinik); "Filtrele"; sayfalama; CSV/XLSX düğmeleri; yazdırma görünümü (üstbilgi/imza resmî biçim doğrulanınca). Ekranda "resmî biçime uygunluk doğrulanmadı" notu yazılmalı.

## 11) Test odağı

- Application: tarih aralığı doğrulaması (eksik, ters, 366 gün), klinik bağlam uyuşmazlığı, dışa aktarma tavanı, sütun tanımı ↔ başlık/değer, yaş ve hücre biçimleri, CSV/XLSX içerik.
- Entegrasyon (LocalDB, kendi kiracısıyla): İstanbul gün sınırı (gece yarısı iki yanı), sıra no ve sıralama, sayfalama (kesişme/atlama yok), sonradan eklenen işareti, iptal edilen uygulamanın varsayılan gizliliği ve `includeVoided`, eksik alanların boşluğu, reçete başına tek satır ve ilaçların tek hücrede birleşmesi, satırsız reçete, ekran ≡ CSV ≡ XLSX, klinik kapsamı, başka kiracı (IDOR), `Examinations.Read` / `Prescriptions.Read` olmadan 403, token olmadan 401.

---

# Bölüm II: parti (lot) verisinden türetilen çıktılar (ADR-015 dilim c)

Veri kaynağı `BATCH_STOCK_API_CONTRACT.md`'dir (parti, stok hareketi, uygulama). Bu bölüm yeni veri **tutmaz**; salt okuma, **migration yok, yeni izin yok, seed gerekmez**. **Resmî biçime uygunluk doğrulanmadı** (başlık notu geçerli): sütunlar `KAYIT-DEFTERLERI-RAKIP-BICIMI.md` (kullanıcı doğrulaması: gizli sağ sütun yok) listesidir.

## 12) Ortak kurallar ve bölüm 1'den farklar

### Uçlar ve izinler

| Çıktı | Liste (JSON, sayfalı) | CSV | XLSX | İzin (hepsi **birlikte** gerekir) |
|---|---|---|---|---|
| Aşı Kayıt Defteri | `GET /api/v1/reports/registers/vaccines` | `…/vaccines/export` | `…/vaccines/export-xlsx` | `StockMovements.Read` |
| İlaç Kayıt Defteri | `…/registers/medications` | `…/medications/export` | `…/medications/export-xlsx` | `StockMovements.Read` |
| Narkotik ve Psikotropik Ürünler Stok ve Sarf Defteri | `…/registers/controlled-substances` | `…/export` | `…/export-xlsx` | `StockMovements.Read` + `Administrations.Read` |
| Aşı Bilgi Raporu | `…/registers/vaccine-info` | `…/export` | `…/export-xlsx` | `Administrations.Read` + `Vaccinations.Read` + `Products.Read` |

İzin gerekçeleri (yeni izin yok; birden çok `[Authorize(Policy=…)]` birbirine **VE** ile bağlanır):

- **Aşı / İlaç giriş defterleri:** satırlar ürün, firma, fatura, seri/SKT içerir; parti okuma uçlarıyla (`/stock-batches`) aynı veri olduğundan aynı izin, `StockMovements.Read`. Hayvan/sahip verisi yoktur.
- **Narkotik/psikotropik defteri (en sıkı kombinasyon):** stok hareketlerini gösterir (`StockMovements.Read`) ve çıkış satırlarında uygulamanın **hasta ve sahip adını** taşır; bu veri `Administrations.Read` ile korunduğundan o da gerekir. Kontrollü ürün verisi bu iki iznin kesişimine bağlıdır; biri eksikse 403. Varsayılan rollerde ikisi de Admin, Owner, ClinicAdmin, Veteriner, Sekreter'dedir (Sekreter yalnız okur); özel rollerde elle verilmelidir. Yeni kısıtlayıcı izin (örn. `ControlledSubstances.Read`) **eklenmedi**; istenirse ayrı karar + seed gerekir (açık soru S4, bölüm 17).
- **Aşı Bilgi Raporu:** hayvan bazlı aşı uygulaması (`Administrations.Read`), aşı alanı (`Vaccinations.Read`), sütunlarda ürün **fiyatı** (`Products.Read`). Sahip adı/çip no bu uçta `Clients.Read` istemez (bölüm 1 ilkesi: defter, klinik kaydı okuyabilene açıktır).

Yetki yoksa 403; token yoksa 401.

### Ortak sorgu parametreleri

Bölüm 1 ile aynıdır (`from`, `to`, `clinicId`, `page`, `pageSize`; İstanbul günü, ≤ 366 gün, klinik kapsamı, kiracı bağlamı, tavan, hata kodları bölüm 8). Farklar:

| Parametre | Uç | Anlam |
|---|---|---|
| `includeVoided` | vaccines, medications | **İptal edilen (geri alınan) girişler** de gelsin (varsayılan `false`) |
| `includeVoided` | vaccine-info | İptal edilen uygulamalar da gelsin (varsayılan `false`) |
| `includeVoided` | controlled-substances | **Yoktur** (hareket defteri: geri alma kendi satırıdır, hiçbir şey gizlenmez) |
| `productId` (guid?) | vaccine-info | Ürün süzgeci |
| `veterinarianId` (guid?) | vaccine-info | Veteriner (geliş kaydındaki sorumlu hekim) süzgeci |

Kiracıda olmayan `productId`/`veterinarianId` hata vermez, satır da eşleşmez (IDOR yok).

### Tarih ve sıra kuralı (tek yerde)

- **Aşı, İlaç, Narkotik/Psikotropik:** defterin tarihi **stok hareketinin zamanı** (`StockMovement.OccurredAtUtc`) olup süzgeç ve sıralama onunla yapılır: `[from 00:00, to+1 00:00)` İstanbul. Sıra: `OccurredAtUtc`, `CreatedAtUtc`, hareket `Id` artan. Sıra No bölüm 1'deki gibidir (süzgece göreceli, sayfalar kesişmez, ekran = CSV = XLSX). *Gerekçe:* hareket zamanı her satırda vardır, veritabanında doğrudan karşılaştırılır ve ölçüt tek yerdedir; `batch.receivedDate` (geliş/teslim) **isteğe bağlıdır** ve kullanıcı geçmiş tarihli teslimi sonradan girebilir. Geliş/teslim tarihi ayrı **sütunda** gösterilir (`receivedDate`, yoksa hareketin İstanbul günü). Sonuç: geçmişe dönük girilmiş bir teslim, girildiği günün aralığında görünür; teslim tarihine göre listelemek ürün kararıdır (S1, bölüm 17).
- **Aşı Bilgi Raporu:** uygulamanın zamanı (`Administration.AdministeredAtUtc`); sıra `AdministeredAtUtc`, `CreatedAtUtc`, `Id` artan.

### Tarih tipleri

`expiryDate`, `receivedDate`, `invoiceDate`, `dispatchedDate`, `deliveredDate` ve narkotik defterindeki alınan/çıkış tarihleri **takvim günüdür** (`yyyy-MM-dd`, saat dilimi yok; CSV `dd.MM.yyyy`, XLSX tarih hücresi). `…Utc` alanları an'dır (dosyada İstanbul günü).

### Sütun tanımı ve dışa aktarma

Her çıktı kendi sütun tanım listesine sahiptir (`VaccineRegisterColumns`, `MedicationRegisterColumns`, `ControlledSubstanceRegisterColumns`, `VaccineInfoRegisterColumns`; bölüm 6 ile aynı tek-yer kuralı, ortak CSV/XLSX yazıcıları). Dosya adları: `asi-kayit-defteri-`, `ilac-kayit-defteri-`, `narkotik-psikotropik-defteri-`, `asi-bilgi-raporu-` + `yyyyMMdd` (`.csv|.xlsx`). XLSX sayfa adları: "Aşı Kayıt Defteri", "İlaç Kayıt Defteri", "Narkotik-Psikotropik", "Aşı Bilgi Raporu". Tavan ve boş-hücre kuralı bölüm 1'deki gibi; ekran–CSV–XLSX eşitliği testle kanıtlanır. Gruplu başlıklar yalnız narkotik defterindedir (iki katmanlı XLSX başlığı, CSV'de `Grup · Sütun`).

## 13) Aşı Kayıt Defteri: `GET /api/v1/reports/registers/vaccines`

**Satır = bir aşı stok girişi** (`In`/`Initial` hareketi). Yalnız `Product.kind = Vaccine`. Partili girişte parti alanları dolar. Partisiz giriş (parti öncesi eski stok) da satırdır; seri/SKT/firma sütunları boş kalır (`isBatched = false`). Aşı ürünü yeni girişte lot+SKT zorunlu olduğundan partisiz satır yalnız eski veridir.

**Geri alınan giriş** (`Reversal` ile geri alınmış): varsayılan gizli, `includeVoided=true` ile `isReversed = true` görünür; dosyada ticari adın sonuna ` (iptal)` eklenir.

`VaccineRegisterItemDto`:

| Alan | Tip | Defter sütunu | Kaynak |
|---|---|---|---|
| `rowNumber` | int | Sıra No | |
| `movementId`, `productId` | guid | | Dosyada yok |
| `tradeName` | string | Ticari Adı | `Product.Name` |
| `lotNumber` | string? | Seri No | `StockBatch.LotNumber` |
| `expiryDate` | date? | Son Kullanma Tarihi | `StockBatch.ExpiryDate` |
| `totalQuantity` | decimal | Toplam Miktar (Doz) | Giriş hareketinin miktarı (= partinin `initialQuantity`'si; kalan değil) |
| `supplierName` | string? | Satın Alınan Firma | |
| `transportMethod` | string? | Nakil Şekli (Soğuk Zincir) | |
| `coldChainMonitor` | string? | Nakilde Kullanılan İzleme Cihazı (Soğuk Zincir İndikatörü) | |
| `dispatchedDate` | date? | Depo Çıkış Tarihi | |
| `deliveredDate` | date | Teslim Tarihi | `receivedDate`, yoksa hareketin İstanbul günü (hiç boş değil) |
| `invoiceDate` | date? | Fatura Tarihi | |
| `invoiceNumber` | string? | Fatura No | |
| `isBatched`, `isReversed` | bool | | Dosyada yok |

## 14) İlaç Kayıt Defteri: `GET /api/v1/reports/registers/medications`

Aşı defteriyle aynı kaynak ve kurallar (giriş hareketi başına satır, partisiz giriş satırdır, geri alınan giriş gizli/`includeVoided`), süzgeç `Product.kind = Medicine` (**aşılar ve `Other` hariç**). Kontrollü bir ilaç hem bu defterde hem narkotik defterinde görünür (ayrı yasal defterler).

`MedicationRegisterItemDto`:

| Alan | Tip | Defter sütunu | Kaynak |
|---|---|---|---|
| `rowNumber` | int | Sıra No | |
| `movementId`, `productId` | guid | | Dosyada yok |
| `productName` | string | Ürün | `Product.Name` (+ iptalde ` (iptal)`) |
| `pharmacologicalForm` | string? | Farmakolojik Şekli | `Product` |
| `presentationForm` | string? | Takdim Şekli | `Product` |
| `quantity` | decimal | Adet | Giriş miktarı (ürünün stok birimi cinsinden) |
| `unit` | string | Birim | `Product.Unit` |
| `supplierName` | string? | Firma Adı | |
| `receivedDate` | date | Geldiği Tarih | `receivedDate`, yoksa hareketin İstanbul günü |
| `invoiceDate` | date? | Fatura Tarihi | |
| `invoiceNumber` | string? | Fatura No | |
| `lotNumber` | string? | Seri No | |
| `isBatched`, `isReversed` | bool | | Dosyada yok |

## 15) Narkotik ve Psikotropik Ürünler Stok ve Sarf Defteri: `GET /api/v1/reports/registers/controlled-substances`

Kapsam: `Product.controlledClass ≠ None` olan (narkotik, psikotropik) ürünlerin **bütün stok hareketleri**. Defterde ayrı kalan/bakiye sütunu yoktur.

### Satır kuralı: **hareket başına bir satır** (parti başına değil)

*Gerekçe:* defterin yapısı "alınan" ve "çıkış yapılan" olarak iki ayrı taraf taşır ve her biri kendi tarih/belge/yer bilgisine sahiptir. Bir parti bir kez alınır ama birçok kez harcanır; parti başına tek satır bu çıkışları yutar ve çıkış tarihi/belgesi için yer bırakmaz. Hareket tabanı denetimi de sağlar: stok defteri (`StockMovement`) append-only'dir; giriş ve her sarf, iade, geri alma, düzeltme kendi satırıdır. Bir satırda **yalnız bir taraf** dolu olur (Alınan **veya** Çıkış); `Miktar` hareketin (her zaman pozitif) miktarıdır. İstenirse bakiye, satırlar toplanarak türetilir (alınanlar − çıkışlar); parti kalanı için bkz. `/stock-batches`.

### Taraf ve içerik kuralları (tek yerde: `ControlledSubstanceRegisterMapper`)

| Hareket | Taraf | Yer | Tarih | Belge No |
|---|---|---|---|---|
| `In` / `Initial`, partili | Alınan | `supplierName`; yoksa `Initial` için `Devir`, `In` için boş | `receivedDate`, yoksa hareketin İstanbul günü | `invoiceNumber` |
| `In` / `Initial`, **partisiz** (eski devir) | Alınan | `Initial` için `Devir`, `In` için boş | hareketin İstanbul günü | boş |
| `Out`, kaynak `Administration` | Çıkış | `Uygulama: {hasta adı} ({sahip adı})` | hareketin İstanbul günü | boş (eksik alan) |
| `Out`, diğer (manuel çıkış, imha vb.) | Çıkış | hareketin `reason` metni; yoksa `Stok çıkışı` | hareketin İstanbul günü | boş |
| `Reversal` (geri alınan `Out`: stok **artar**, iade) | Alınan | `Geri alma: {reason}` | hareketin İstanbul günü | boş |
| `Reversal` (geri alınan `In`/`Initial`: stok **azalır**) | Çıkış | `Geri alma: {reason}` | hareketin İstanbul günü | boş |
| `Adjustment`, partili, kalan arttı | Alınan | `Sayım düzeltmesi (artış)` | hareketin İstanbul günü | boş |
| `Adjustment`, partili, kalan azaldı | Çıkış | `Sayım düzeltmesi (azalış)` | hareketin İstanbul günü | boş |
| `Adjustment`, **partisiz** | Belirsiz | iki tarafın yer hücresi de `Sayım düzeltmesi (yön belirlenemedi)` | hareketin İstanbul günü (iki tarafta) | boş |

- **Adjustment yönü saklanmaz** (`BATCH_STOCK_API_CONTRACT.md` bölüm 1): partili düzeltmede yön, aynı partinin bir önceki hareketinin `batchBalanceAfter` değeriyle karşılaştırılarak türetilir (sıra: `CreatedAtUtc`, `Id`; giriş hareketinin `batchBalanceAfter`'ı giriş miktarıdır, bu nedenle partili düzeltmenin her zaman bir öncüsü vardır). **Partisiz düzeltmenin yönü türetilemez** (yön ve geçmiş toplam saklanmıyor); satır `direction = Undetermined` ile gösterilir ve yön uydurulmaz. Kontrollü ürünün yeni girişi lot ister; bu durum yalnız sınıfı sonradan kontrollü yapılmış eski stokta oluşur. Yön saklaması bir migration işidir (S2, bölüm 17).
- **Eski partisiz "devir" bakiyesi:** ayrı sentetik "devir" satırı **üretilmez**. Stok satırı yalnız hareketle oluştuğundan (`Initial`/`In`), eski bakiye zaten kendi giriş hareketiyle bir satırdır: Seri No boş, `Initial` ise Alındığı Yer = `Devir`. Aralık dışındaki eski devir aralığın dışında kalır (defter tarih aralıklıdır; açılış bakiyesi satırı yoktur). *Gerekçe:* hareketten bağımsız bir "devir bakiyesi" satırı türetilmiş bir kalem olurdu ve defterin "her satır gerçek bir kayıt" ilkesini bozardı.
- Aynı hareket iki gruba ikilenmez; **Belirsiz** düzeltme istisnadır (iki taraf da aynı metni taşır ki satır görünmez kalmasın).
- Uygulamanın iptali `StockReturned` ise geri alma hareketi (`Reversal`) alınan tarafta görünür; `WrittenOff` ise ek hareket yoktur, orijinal çıkış satırı kalır.

`ControlledSubstanceRegisterItemDto`:

| Alan | Tip | Defter sütunu / not |
|---|---|---|
| `rowNumber` | int | Sıra No |
| `movementId`, `productId` | guid | Dosyada yok |
| `movementType` | string | `Initial` \| `In` \| `Out` \| `Adjustment` \| `Reversal` (dosyada yok) |
| `direction` | string | `Received` \| `Issued` \| `Undetermined` (dosyada yok) |
| `productName` | string | Ürün Adı, Ticari Adı ve Farmakolojik Şekli: dosyada tek hücre `Ad (farmakolojik şekil)` (şekil boşsa yalnız ad) |
| `pharmacologicalForm` | string? | JSON'da ayrıca |
| `commercialPresentation` | string? | Ticari Takdim Şekli: `presentationForm` ve `packaging` birleşik, `, ` ile (biri boşsa dolu olan) |
| `quantity` | decimal | Miktar |
| `lotNumber` | string? | Seri No (hareketin `batchId` partisinin lot'u) |
| `receivedFrom` / `receivedDate` / `receivedDocumentNumber` | string? / date? / string? | SATIN/İADE/DEVİR ALINAN ÜRÜNÜN: Alındığı Yer / Tarih / Belge No (yalnız Alınan veya Belirsiz) |
| `issuedTo` / `issuedDate` / `issuedDocumentNumber` | string? / date? / string? | SATILAN/İADE VEYA DEVREDİLEN ÜRÜNÜN: Çıkış Yapılan Yer / Tarih / Reçete/Belge No (yalnız Çıkış veya Belirsiz) |
| `isBatched` | bool | Dosyada yok |

İade ve devir **hareketleri** (tedarikçiye iade, başka kliniğe devir) bugün sistemde **yoktur** (`BATCH_STOCK_API_CONTRACT.md` bölüm 9, ertelendi); bu tür çıkışlar manuel `Out` olarak, gerekçe metniyle girilirse "Çıkış Yapılan Yer" olarak o metin görünür. Ayrı tip gelince yalnız mapper kuralı değişir.

## 16) Aşı Bilgi Raporu: `GET /api/v1/reports/registers/vaccine-info`

**Satır = bir aşı uygulaması** (`Administration`, hayvan bazlı). Yalnız `Product.kind = Vaccine` ürünlerin uygulamaları. Süzgeç: tarih aralığı, `productId`, `veterinarianId`, `clinicId`, `includeVoided`.

- **Veteriner** = uygulamanın bağlı olduğu muayenenin geliş (Visit) kaydındaki **sorumlu hekim** (`Visit.ResponsibleVeterinarianUserId`), muayene ve reçete defterleriyle aynı kural (bölüm 4). `veterinarianId` süzgeci yalnız buna bakar; muayenesiz uygulama, gelişsiz muayene veya sorumlu hekimi atanmamış geliş süzgeçle **eşleşmez**. `Administration.CreatedByUserId` (kaydı giren kullanıcı) artık hekim sayılmaz. Raporda hekim sütunu yoktur; kural yalnız süzgeçtedir.
- **İptal edilen uygulamalar** varsayılan gizli (`includeVoided=false`); `true` iken `isVoided = true` ve dosyada ürün hücresinde ` (iptal)`.
- **Sonradan eklenen** (ADR-013, `AddedAfterFinalization`): `addedAfterFinalization = true`; dosyada ürün hücresinde ` (sonradan eklendi)`. Gerekçe deftere yazılmaz.
- **Seri No / S.K.T:** uygulamanın partisinden (`Administration.BatchId`); partisiz bakiyeden düşülen (eski) uygulamada boş.
- **Fiyat / Toplam:** `Product.UnitPrice` (**ürünün güncel katalog fiyatı**) ve `Toplam = round(Miktar × Fiyat, 2)`; para birimi `currency` (JSON). **Uyarı:** uygulama kaydı bir ücret satırına (`Charge`) bağlı değildir ve uygulama anında fiyat saklanmaz; bu nedenle tutar, fiilen tahsil edilen değil, **rapor anındaki katalog fiyatı**dır. Ücret–uygulama bağı (ücret önerisi) gelince kaynak değişir (S3, bölüm 17).
- **T.C.:** müşteri kimlik no alanı **yok** (`Client`'ta T.C. kimlik no yok); `clientIdentityNumber` her zaman null, sütun boş (eksik alan).
- **Aşı kaydı ilişkisi (2026-10-14):** ürünle girilen aşı kaydı bağlı bir `Administration` yazar (`ADMINISTRATIONS_API_CONTRACT.md` bölüm 9); rapor satırı **uygulamadır**, aşı kaydı ayrıca satır üretmez (çift sayım yok). Ürünsüz `Vaccination` (takvim/geçmiş aşı) stok düşmez, partiye bağlı değildir ve bu raporda yer almaz. Rakipteki "doğrudan satış" satırı bizde yoktur (doğrudan satış modülü yok).

`VaccineInfoRegisterItemDto`:

| Alan | Tip | Defter sütunu | Kaynak |
|---|---|---|---|
| `rowNumber` | int | # | |
| `administrationId` | guid | | Dosyada yok |
| `administeredAtUtc` | datetime | Tarih | Dosyada İstanbul günü |
| `ownerName` | string | Hasta Sahibi | `Client.FullName` |
| `clientIdentityNumber` | string? | T.C. | **Her zaman null** |
| `petName` | string | Hasta | |
| `species` | string | Tür | |
| `microchipNumber` | string? | Çip No | |
| `productName` | string | Ürün | `Product.Name` (+ ek işaretler) |
| `lotNumber` | string? | Seri No | Partiden |
| `expiryDate` | date? | S.K.T | Partiden |
| `unit` | string | Birim | `Product.Unit` |
| `quantity` | decimal | Miktar | |
| `unitPrice` | decimal | Fiyat | Bkz. uyarı |
| `totalPrice` | decimal | Toplam | |
| `currency` | string | | Dosyada yok |
| `addedAfterFinalization`, `isVoided` | bool | | Dosyada yok |

## 17) Eksik alanlar ve açık sorular

| Konu | Durum |
|---|---|
| T.C. kimlik no (Aşı Bilgi) | Boş; müşteri kimlik no alanı migration'lı iş, KVKK değerlendirmesi gerekir |
| Belge no, uygulama çıkışı (narkotik) | Boş; kalıcı kayıt/belge sıra numarası işi (bölüm 7 "Cilt/Sıra No") ile çözülür |
| Reçete no, narkotik çıkış | Uygulama ile reçete ilişkisi yok; boş |
| Aşı/ilaç/kontrollü ürün sınıfı ataması | Mevcut ürünler `Other`/`None`; sınıflar elle atanmadıkça defterlerde satır çıkmaz (`BATCH_STOCK_API_CONTRACT.md` bölüm 2) |
| Aşı Bilgi fiyatı | Güncel katalog fiyatı (bölüm 16) |

Açık sorular (ürün kararı):

- **S1.** Aşı/ilaç/narkotik tarih süzgeci stok hareketi zamanına göredir (bölüm 12). Teslim/geliş tarihine göre mi olsun? (`receivedDate` boş olabildiği için "geliş tarihi, yoksa hareket günü" hibrit sıralaması gerekir.)
- **S2.** Partisiz stok düzeltmesinin yönünü saklayalım mı (hareket alanı, migration)? Aksi hâlde narkotik defterinde bu satırlar "yön belirlenemedi" kalır.
- **S3.** Aşı Bilgi `Fiyat/Toplam`: katalog fiyatı mı kalsın, yoksa uygulama–ücret bağı gelene kadar sütun boş mu bırakılsın?
- **S4.** Narkotik defteri için ayrı izin (`ControlledSubstances.Read`) istenir mi?

## 18) Frontend için (Rapor Merkezi yeni kartları)

Dört yeni kart (bölüm 10 ile aynı sayfa deseni: tarih aralığı + klinik + "Filtrele", sayfalama, CSV/XLSX, yazdırma görünümü, "resmî biçime uygunluk doğrulanmadı" notu):

| Kart | Uç | Görünürlük (hepsi gerekir) | Ek süzgeç |
|---|---|---|---|
| Aşı Kayıt Defteri | `/reports/registers/vaccines` | `StockMovements.Read` | "İptal edilen girişleri göster" |
| İlaç Kayıt Defteri | `/reports/registers/medications` | `StockMovements.Read` | "İptal edilen girişleri göster" |
| Narkotik ve Psikotropik Ürünler Stok ve Sarf Defteri | `/reports/registers/controlled-substances` | `StockMovements.Read` + `Administrations.Read` | Yok. İki gruplu başlık (Alınan / Çıkış) |
| Aşı Bilgi Raporu | `/reports/registers/vaccine-info` | `Administrations.Read` + `Vaccinations.Read` + `Products.Read` | Ürün, Veteriner (kullanıcı listesinden), "İptal edilenleri göster"; sonradan eklenenler rozetli |

Ekran notları: `…Utc` dışındaki tarihler takvim günüdür (saat dilimi dönüşümü yapılmaz); narkotik defterinde boş taraf hücreleri boş bırakılır ve `direction = Undetermined` satırı vurgulanabilir; Aşı Bilgi'de "Fiyat = güncel katalog fiyatı" ipucu gösterilir; T.C. sütunu boş gelir.

## 19) Test odağı (bölüm II)

- Application: sütun tanımı ↔ başlık/değer (4 çıktı), narkotik mapper (her hareket tipi × taraf, Adjustment yönü, Belirsiz), hücre biçimleri, takvim günü (`DateOnly`) sütun tipi (CSV/XLSX).
- Entegrasyon (LocalDB, kendi kiracısı): her çıktı için İstanbul gün sınırı, sıra no + sayfalama, geri alınan girişin gizliliği/`includeVoided`, partisiz giriş, ürün sınıfı süzgeci (aşı/ilaç/diğer/kontrollü), ekran ≡ CSV ≡ XLSX, klinik kapsamı, başka kiracı (IDOR), `productId`/`veterinarianId` süzgeci, iptal/sonradan eklenen işaretleri, izin kombinasyonu (tek izin eksikse 403) ve token yok 401.
