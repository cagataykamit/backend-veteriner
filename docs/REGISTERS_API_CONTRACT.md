# Kayıt defterleri API sözleşmesi: Muayene ve Reçete defterleri

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-015 (parti bazlı stok ve kayıt defterleri), `KAYIT-DEFTERLERI-RAKIP-BICIMI.md` (ürün deposu). İlgili: `OUTSTANDING_BALANCE_REPORT_API_CONTRACT.md` (rapor ucu deseni), `EXAMINATIONS_WORKSPACE_API_CONTRACT.md`, `PRESCRIPTION_ITEMS_API_CONTRACT.md`, `ADMINISTRATIONS_API_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme. Salt okuma; **migration yok**, yeni izin yok.
>
> **Resmî biçime uygunluk doğrulanmadı.** Sütunlar bir rakip yazılımın ekran görüntülerinden alınmıştır; yasal dayanak ve resmî biçim (sütun seti, üstbilgi, imza alanı, sayfa numarası) doğrulanmamıştır. Biçim doğrulanınca yalnız sütun tanımı (bölüm 6) ve üstbilgi/imza alanı güncellenir. Bu not arayüzde ve çıktıda görünür olmalıdır.

Kapsam: yalnız **Muayene** ve **Reçete** defterleri. Aşı, ilaç, narkotik/psikotropik defterleri parti (lot) bazlı stok gerektirir (ADR-015) ve ayrı iştir.

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

Yanıt `ExaminationRegisterResultDto`: `totalCount` (süzgece uyan muayene sayısı, tüm sayfalar), `items`.

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
| `treatments` | string[] | Tedavi Şekli | Muayeneye bağlı tedavilerin başlığı (tarih sıralı); tedavi yoksa muayenenin `Plan` metni (tek eleman), o da yoksa boş |
| `prescriptions` | `RegisterPrescriptionDto[]` | Reçete Bilgileri | Muayeneye bağlı reçeteler: `title`, `items[]` (`name`, `quantity?`, `instructions?`); satırsız (eski) reçetede `items` boş |
| `administrations` | `RegisterAdministrationDto[]` | Satışa Eklenen İlaçlar | Muayeneye bağlı uygulamalar (bölüm 3): `productName`, `quantity`, `unit`, `addedAfterFinalization`, `isVoided` |
| `veterinarianName` | string? | Veteriner Hekimin Adı Soyadı | Bölüm 4 |

Dosyada `treatments`, `prescriptions`, `administrations` üç ayrı sütundur (rakipte tek sütun); hücre biçimi tek yerde tanımlıdır (bölüm 6): tedaviler `; ` ile; reçete `Başlık: İlaç ×Miktar, İlaç ×Miktar`; uygulama `Ürün Miktar Birim`, sonradan eklenen için ` (sonradan eklendi)`, iptal edilen için ` (iptal)` eki.

Sıralama: `examinedAtUtc`, `createdAtUtc`, `id` artan.

## 3) Sonradan eklenen ve iptal edilen kayıtlar (ADR-013)

- **Sonradan eklenen uygulama** (`Administration.AddedAfterFinalization`): satırda `addedAfterFinalization = true`; dosyada ` (sonradan eklendi)` eki. Gerekçe (`LateEntryReason`) deftere yazılmaz (ayrıntı klinik kayıttadır).
- **İptal edilen uygulama** (`Administration.VoidedAtUtc`): `includeVoided=false` (varsayılan) iken muayene satırına **hiç girmez**; `true` iken `isVoided = true` ile ve ` (iptal)` ekiyle görünür. Gerekçe deftere yazılmaz.
- Muayene ve reçetede iptal kavramı yoktur (yanlış kayıt düzeltmesi ek notla yapılır); `includeVoided` bu iki varlığı etkilemez ve reçete defterinde yoktur.

## 4) Hekim adı kuralı (tek yerde)

`veterinarianName`: muayenenin geliş (Visit) kaydındaki **sorumlu hekim** (`ResponsibleVeterinarianUserId`); geliş yoksa veya sorumlu hekim atanmamışsa muayeneyi **kesinleştiren** kullanıcı; ikisi de yoksa null. Ad: kullanıcının `DisplayName` değeri, yoksa e-postası. Reçete defterinde aynı kural, reçetenin bağlı olduğu muayene üzerinden uygulanır; muayenesiz reçetede hekim **boş**tur (reçetede hekim alanı yok; bölüm 7).

## 5) Reçete defteri: `GET /api/v1/reports/registers/prescriptions`

Sayfalama ve sayım **reçete** bazlıdır (bir reçete sayfaya bölünmez); `totalCount` süzgece uyan reçete sayısıdır. Her reçete bir `entry`, içindeki ilaç satırları `items` olur. **Dosyada reçete satırı başına bir satır** vardır (reçete bilgileri her satırda tekrarlanır; `Sıra No` aynı reçetenin satırlarında aynıdır). Satırı olmayan (eski, serbest metin) reçete dosyada **tek satır** verir: ilaç adı = reçete `Content` metni, adedi boş.

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
| `items` | `PrescriptionRegisterItemDto[]` | İlacın … | Aşağıda; satırsız reçetede tek öğe (`name = Content`) |

`PrescriptionRegisterItemDto`: `name` (İlacın Adı), `pharmacologicalForm` (Farmakolojik Şekli; **her zaman null**), `packaging` (Ambalajı; **her zaman null**), `quantity` (Adedi, ondalık?).

Dosya başlıkları iki katmanlıdır: XLSX'te ilk satır grup başlığı (REÇETENİN / VETERİNER HEKİMİN / HAYVAN SAHİBİNİN / HAYVANIN / İLAÇ), ikinci satır sütun başlığı (grup hücreleri birleştirilir); CSV'de tek başlık satırı `Grup · Sütun` biçimindedir (CSV iki satırlı başlığı güvenle taşımaz).

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
| Hekim (gelişsiz/atanmamış muayene) | Muayene | Kısmen | `Examination.ResponsibleVeterinarianUserId`; bugün Visit sorumlu hekimi, yoksa kesinleştiren kullanıcı (bölüm 4) |
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
- Entegrasyon (LocalDB, kendi kiracısıyla): İstanbul gün sınırı (gece yarısı iki yanı), sıra no ve sıralama, sayfalama (kesişme/atlama yok), sonradan eklenen işareti, iptal edilen uygulamanın varsayılan gizliliği ve `includeVoided`, eksik alanların boşluğu, reçete satır genişletmesi ve satırsız reçete, ekran ≡ CSV ≡ XLSX, klinik kapsamı, başka kiracı (IDOR), `Examinations.Read` / `Prescriptions.Read` olmadan 403, token olmadan 401.
