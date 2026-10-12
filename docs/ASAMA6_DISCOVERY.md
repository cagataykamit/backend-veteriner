# Aşama 6 kod keşfi — lab istem/bekleyen sonuç ve manuel görüntüleme/dosya

Salt okunur keşif (ürün kodu değişmedi, migration yok, test/derleme çalıştırılmadı, DB'ye bağlanılmadı, uçlar HTTP ile denenmedi). Taban: yerel `main` `6d2f10e`. Kaynaklar: ana plan §11 (Aşama 6), ADR-007, `AJAN-KUYRUGU.md`, `ASAMA4_DISCOVERY.md`, `ASAMA5_DISCOVERY.md`. Doğrulanmayan her şey "çıkarım" diye işaretli; son bölümde toplandı. Aşama 5 alanlarına (stok/Uygulama/reçete) dokunulmadı, yalnız okundu. Frontend deposu (`C:\FrontEnd\veteriner-frontend`, `main`) yalnız okundu.

## 1. Mevcut durum

### 1.1 Özet

- **`LabResult` yalnız "sonucu girilmiş" kaydı modeller.** `ResultText` zorunlu, istem/bekleyen/iptal durumu yok, istek tarihi ve isteyen kişi yok. Ana plan §11'in çekirdeği (sonucu olmayan test kaydı + bekleyenler listesi) **bugün ifade edilemiyor**.
- **Dosya/Imaging altyapısı hiç yok.** Backend'de dosya yükleme, depolama soyutlaması, `IFormFile`, statik dosya, istek gövdesi sınırı, Imaging varlığı yok (grep: `IFormFile`, `FileUpload`, `Attachment`, `Imaging`, `IFileStorage`, `BlobStorage`: yalnız e-posta eki `IEmailAttachment/EmailAttachment`, ilgisiz). Frontend'de yükleme bileşeni yok (yalnız PrimeNG şablon demosu `pages/uikit/filedemo.ts`).
- Backlog'da IMG-001…008 var (ADR-007), kod karşılığı yok.

### 1.2 `LabResult` varlığı

| Alan | Not |
|---|---|
| `Id`, `TenantId`, `ClinicId`, `PetId` | Zorunlu. |
| `ExaminationId?` | Opsiyonel; var ise klinik + hayvan uyumu handler'da denetleniyor (`LabResults.ExaminationClinicMismatch/PetMismatch`). |
| `ResultDateUtc` | Zorunlu; **en çok 7 gün geçmiş, 2 yıl gelecek** (`ResultDateUtcWindow`). Bekleyen istem için tarih anlamı belirsiz (istek tarihi mi, sonuç tarihi mi). |
| `TestName` (≤500), `ResultText` (≤8000, **zorunlu**), `Interpretation?` (≤4000), `Notes?` (≤4000) | `Interpretation` "hekim değerlendirmesi" için en yakın alan ama ayrı durum değil. |
| `CreatedAtUtc`, `UpdatedAtUtc?` | `CreatedByUserId`, `RequestedByUserId`, `VeterinarianId` **yok**. |

Eksik olanlar: `VisitId`, durum, isteyen kişi/hekim, istek tarihi, sonuç giriş tarihi/kişisi, iptal alanları (neden/kim/ne zaman), `RowVersion`, `RequestId` (idempotency), ücret/dosya bağı. Silme ve iptal ucu yok. Domain olayı/outbox, audit (`IAuditableRequest`) yok (çıkarım: Create/Update handler'ları `IRequest`, `ITransactionalRequest` değil; grep ile tam doğrulanmadı).

Config (`LabResultConfiguration`): tablo `LabResults`, FK/navigasyon tanımı **yok** (yalnız Guid'ler), indeksler `TenantId`, `(TenantId, ResultDateUtc)`, `(TenantId, ClinicId)`, `(TenantId, PetId)`, `(TenantId, ExaminationId)`. Durum veya "bekleyen" filtresi için indeks yok.

### 1.3 Uçlar ve izinler

| Uç | İzin | Not |
|---|---|---|
| `POST /api/v1/lab-results` | `LabResults.Create` | JSON gövde; 201 + Guid. |
| `PUT /api/v1/lab-results/{id}` | `LabResults.Update` | **Tam gövde** (`UpdateLabResultBody`; ClinicId/PetId/ExaminationId/tarih/metinler hepsi gönderilir). `RowVersion` yok → son yazan kazanır (iki sekme senaryosunda sessiz ezme). Hayvan/klinik/muayene değiştirilebiliyor. |
| `GET /api/v1/lab-results/{id}` | `LabResults.Read` | `LabResultDetailDto` (pet/müşteri adı dahil). |
| `GET /api/v1/lab-results` | `LabResults.Read` | Filtre: `search`, `clinicId`, `petId`, `dateFromUtc`, `dateToUtc`; sayfalama. **Durum, muayene, "bekleyen" filtresi yok**; sıralama uygulanmıyor. |

İzin dağılımı (`RolePermissionBindings`): Admin, Owner, ClinicAdmin, Veteriner → Read/Create/Update; Sekreter → yalnız Read (çıkarım: satır 439 civarı, rol başlığı satır aralığından). `LabResults.Delete/Cancel/Correct` ve dosya izinleri yok. Yeni izin = `PermissionCatalog` + bağlama + seeder; "seed yalnız birleşik main'den" kuralı geçerli.

Klinik kapsamı: Create/Update handler'ları `LabResultClinicWriteScope` (IDOR-7G) ve aktif klinik bağlamı eşleşmesi uyguluyor; entegrasyon testleri `LabResultDetailIdorIntegrationTests`, `LabResultWriteClinicAssignmentIdorIntegrationTests` mevcut (çalıştırılmadı).

### 1.4 Examination / Visit / timeline / ücret bağlantıları

| Bağ | Durum |
|---|---|
| `ExaminationId` | Var, opsiyonel. |
| **Kesinleşmiş muayene kilidi** | **Lab'a uygulanmıyor (gerçek açık).** `ExaminationLinkedRecordLock.EnsureOpenAsync` (kural `Examination.EnsureAcceptsLinkedRecords`, tek yer) tedavi, reçete, aşı Create/Update handler'larında çağrılıyor; `LabResults/` altındaki Create/Update handler'ında çağrılmıyor (grep ile doğrulandı). Yani kesinleşmiş muayeneye `POST /lab-results` ile lab eklenebilir ve mevcut lab PUT ile değiştirilebilir (çıkarım: koddan; HTTP ile denenmedi). Aşama 4 D1 ile çelişir. Frontend muayene detayında "Lab Sonucu Oluştur" düğmesi `Draft` koşuluna bağlı görünmüyor (kodda ilgili blok yalnız `clientId/petId` doluluğuna bakıyor), "Düzenle" ise `Draft` koşullu. |
| `VisitId` | **Yok** (ana plan: "bağlı ziyaret korunur"). Muayene üzerinden dolaylı. |
| Timeline | Var (`LabResultTimelineSource`): `ResultDateUtc` zamanında, başlık `TestName`, `Status = null`, `Summary = null`, `RelatedVisitId = null`. Bekleyen istem olay zamanı olarak `ResultDateUtc`'ye bağlı → istek tarihi ayrı yoksa timeline'da yanlış zamanda durur. Dosya/sonuç özeti timeline'da yok. İptal/`IsVoided` her zaman `false`. |
| Muayene ilişkili özet | `ExaminationRelatedLabResultItemDto(Id, ResultDateUtc, Clinic, TestName)`: durum yok. |
| Hayvan geçmiş özeti | `recentLabResults` listesi var (durum yok). |
| Ücret | `Charge`: `ClientId, PetId?, VisitId?, ExaminationId?, Description, Quantity, UnitPrice`; **kaynak (lab/görüntü) alanı yok**. "Lab/görüntü kaydı ücret satırına kaynak olabilir" (plan #9) bugün yok; ücret şemasına dokunmak Aşama 5 Dilim 5 ile aynı koordinasyon konusu. |
| Bekleyen işler yüzeyi | **Yok.** Bugün/dashboard "bekleyen sonuç" bilmiyor (grep'te lab ile ilgili yalnız Dashboard klinik kapsam yardımcısı). Query DB read model'i lab için yok (projeksiyonlar yalnız Appointments/Clients/Payments/Pets). |

### 1.5 Dosya depolama (yok)

Mevcut bir şey yok: depolama arayüzü, yerel/bulut sağlayıcı, anahtar adlandırma, virüs taraması, boyut sınırı (Kestrel/`FormOptions`/`RequestSizeLimit` kodda aranınca bulunmadı; Kestrel varsayılanı ~30 MB gövde sınırı çıkarım), içerik türü doğrulaması, imzalı URL/akış ucu. `appsettings.json`'da depolama bölümü yok (grep: yalnız Serilog `File` sink'i). Dağıtım hedefi (tek sunucu diski mi, bulut mu) bu keşifte bilinmiyor.

### 1.6 Frontend (okundu)

- `features/lab-results/`: liste, yeni, düzenle, detay sayfaları; rota izin korumalı (`LAB_RESULTS_READ/CREATE/UPDATE_CLAIM`); salt-okunur mod (`ro.mutationBlocked`) abonelik kilidi içindir, muayene kilidi değildir.
- Form: müşteri → hayvan → tarih (yıl aralığı ve "çok geçmiş" doğrulayıcı) → test adı → **sonuç metni zorunlu** → yorum/notlar. İstek/bekleyen kavramı, dosya, durum rozeti yok.
- Girişler: menü "Lab" (`LAB_RESULTS_READ_CLAIM`), muayene detayı (`/panel/lab-results/new?clientId&petId&examinationId`), hayvan detayı "Son lab sonuçları", hayvan timeline (`LabResult` türü, `/panel/lab-results/:id` bağlantısı).
- Yükleme bileşeni, dosya önizleme/indirme, bekleyen sonuçlar yüzeyi, Bugün'de bekleyen gösterimi yok.
- Frontend `main` `subscription-access.utils.ts` kullanıcıya ait commit dışı değişiklik içeriyor (dokunulmadı).

## 2. Ana plan §11 kabul kriterleri — eksik ve riskler

Kabul: *"Sonucu henüz olmayan test kaydedilir ve bekleyenlerde görünür. Sonra sonuç/dosya eklenir; hasta geçmişi ve ilgili işlem aynı kaydı gösterir. Yetkisiz dosya erişimi reddedilir; upload hatasında kullanıcı girdisi korunur."*

| # | Plan maddesi | Mevcut | Eksik |
|---|---|---|---|
| L1 | İstem hasta/muayene içinden; açıklama, isteyen, tarih, bağlı ziyaret korunur | Hasta/muayene bağı var; `VisitId`, isteyen, istek tarihi yok | Yeni alanlar + istem oluşturma komutu |
| L2 | Durum: istenmiş/bekleyen, sonuç girilmiş, iptal | Durum yok; `ResultText` zorunlu | Durum modeli, `ResultText`/tarih zorunluluğunun gevşetilmesi (mevcut kayıtlar "Sonuçlandı" olarak göç eder) |
| L3 | Sonuç manuel metin + izinli dosya | Metin var; dosya yok | Dosya katmanı (bkz. F-maddeleri) |
| L4 | "Sonuç hazır" denmez; sonuç var ama değerlendirme yok ayrı gösterilir | `Interpretation` alanı var, durum bağı yok | Değerlendirme durumu (alan boş mu / hekim kim, ne zaman) |
| L5 | Bekleyen sonuç ziyaret kapansa da görünür; ziyareti her zaman engellemez | Bekleyenler yüzeyi yok; muayene kesinleştirme lab'a bakmıyor (doğru: engellememeli) | Klinik geneli "bekleyen" listesi ucu, filtre, indeks; kesinleşmiş muayenede **sonuç girişi** nasıl olur (kilit çatışması, bkz. R1/D2) |
| L6 | Başarısız yüklemede form metni/mevcut dosya kaybolmaz | — | Sözleşme: metin kaydı ile dosya yüklemesi ayrı çağrı; önce metin, dosya sonra; kısmi başarı yanıtı. Frontend taslak koruma |
| L7 | Dosya kaynağı, yükleyen, tür/boyut sınırı, erişim açıklanır | Yok | Dosya meta tablosu (yükleyen, zaman, boyut, içerik türü, orijinal ad, SHA-256) |
| L8 | Orijinal korunur, önizleme ayrı olabilir | Yok | İlk dilimde önizleme üretimi gerekmez (tarayıcı render); küçük resim ileriye |
| L9 | Lab/görüntü timeline'da ve ücret satırına kaynak | Lab timeline'da var (durumsuz); görüntü yok; ücret kaynağı yok | Timeline'a durum + görüntü türü; ücret kaynağı Aşama 5/FIN ile koordine |
| L10 | Tek kaynak: hasta bazında kayıt ile klinik geneli bekleyenler aynı veriye dayanır | İki yüzey de yok | Bekleyenler sorgusu `LabResults` tablosundan (read model gerekmez, ilk sürüm) |
| I1 | Görüntü kaydı: hasta, muayene, tarih, tür, açıklama (ADR-007) | Yok | `ImagingRecord` varlığı + tür enum'u |
| I2 | İlk dosya kapsamı görüntü + PDF; video/büyük dosya sonra | — | İzinli tür listesi (jpeg, png, webp?, pdf), DICOM/video dışarıda |

### Riskler

- **R1 — Kilit boşluğu (yüksek):** Lab Create/Update kesinleşmiş muayeneye açık (§1.4). Ama plan #5 "bekleyen sonuç ziyaret/muayene kapandıktan sonra da görünür/girilebilir" der: kilidi lab'a olduğu gibi kopyalamak (tedavide olduğu gibi) **sonucu geç gelen testi bloke eder**. Kural lab için farklı olmalı (D2): mevcut `EnsureAcceptsLinkedRecords` "ekle/değiştir yok" diyor.
- **R2 — Dosya güvenliği (yüksek):** Hiç altyapı yok; her karar yeni: uzantıya değil içeriğe bakan tür doğrulaması (magic bytes), boyut sınırı (uygulama + Kestrel/ters vekil), yol geçişi/dosya adı temizleme, kök dışı yazma, aynı adda ezme, depolamada tahmin edilemez anahtar (Guid), virüs taraması (ürün/maliyet kararı), SVG/HTML/aktif içerik yasağı (XSS: tarayıcıda `Content-Type` + `Content-Disposition` + `X-Content-Type-Options: nosniff`), PDF gömülü betik riski (indirme yerine satır içi göstermeme veya sandbox).
- **R3 — Yetkisiz erişim / IDOR (yüksek):** Dosya doğrudan statik URL ile sunulmamalı; her indirme `LabResults.Read`/`Imaging.Read` + kiracı + klinik kapsamı denetleyen yetkili akış ucundan geçmeli (Lab detay IDOR desenine uygun). Tarayıcı `<img src>` Bearer başlığı gönderemez → kısa ömürlü imzalı URL veya `blob` ile fetch kararı (D4).
- **R4 — Yarım yükleme / tutarsızlık (orta):** Dosya diske yazıldı ama DB kaydı düşmedi (veya tersi) → yetim dosya. Sıra: meta kayıt "Yükleniyor" → dosya yaz → "Hazır"; temizlik işi. Transaction DB ile dosya sistemini kapsamaz.
- **R5 — Eşzamanlılık / idempotency (orta):** `LabResult` PUT'ta `RowVersion` yok; durum geçişleri (sonuç gir, iptal) için `RowVersion` + 409 gerekir (Charge/Examination deseni). Çift tıkla çift istem için `RequestId` (Charge deseni).
- **R6 — Veri göçü (orta):** `ResultText` zorunlu ve `ResultDateUtc` zorunlu kalırsa bekleyen kaydı yazılamaz. Alanları nullable yapmak mevcut kodu (liste/özet/timeline/DTO/mapper/frontend) etkiler. Mevcut tüm kayıtlar "Sonuçlandı" olmalı (migration veri doldurması).
- **R7 — Tarih penceresi (orta):** Sonuç tarihi "en çok 7 gün geçmiş" kuralı gerçek akışta bozar: 10 gün önce istenen test bugün sonuçlanırsa sorun yok (sonuç tarihi bugün), ama **geçmiş bir istek tarihi** girilemez; istek tarihi sunucuda atanmalı (`RequestedAtUtc = now`), kullanıcı girmemeli.
- **R8 — Yetki (orta):** Dosya yükleme/indirme/silme için ayrı izin yok. Sekreter lab Read'e sahip: sekreter lab dosyalarını ve röntgenleri görsün mü? (D6). Yeni izin = seeder + özel rollere elle ekleme.
- **R9 — Timeline/özet etkisi (orta):** Durum alanı gelince timeline, muayene ilişkili özet ve hayvan geçmiş özeti DTO'ları sözleşme değişikliği alır (frontend uyumu). Bekleyen kayıt timeline'da `RequestedAtUtc` zamanında, sonuçlanınca aynı satır mı yoksa ikinci olay mı? (D5).
- **R10 — Depolama maliyeti/yedek (orta):** Dosyalar DB yedeğinde değil; yedekleme, saklama süresi, tenant silme/KVKK silme akışı belirsiz. Büyük dosya = maliyet (ADR-007 riski).
- **R11 — İptal/silme (orta):** Dosya silme "kalıcı silme" mi "gizle" mi; klinik kayıt bütünlüğü için yumuşak silme + gerekçe önerilir.
- **R12 — Kapsam kayması (orta):** Cihaz entegrasyonu, DICOM, analyte, referans aralığı planın dışında; "Imaging" adı PACS izlenimi vermemeli (plan #8). UI'da "Görüntü arşivi" dili.
- **R13 — Query DB / sağlık (düşük):** `/health` 503 pet projeksiyonu bekleyen olaylarından; bu aşamayla ilgisiz, lab için projeksiyon yok.

## 3. Önerilen dilimler

Genel ilke: ADR-009/012 ve Aşama 5 desenleri (kural domain'de, `RowVersion`, `RequestId`, 409, yeni izin = seeder). Okuma → yazma; migration yalnız yerel DB'ye ve yalnız kullanıcı onayıyla.

| # | Dilim | Kapsam | Migration | Bağımlılık | Paralel? |
|---|---|---|---|---|---|
| 0 | Lab kilit boşluğu (kısa) | Lab Create/Update'te muayene kilidi **kararına göre** (D2); testler | Yok | D2 | Evet (1, 2 ile) |
| 1 | Lab istem + durum modeli | `LabResult`: durum (Requested/Resulted/Cancelled), `RequestedAtUtc`, `RequestedByUserId`, `VisitId?`, sonuç giriş zamanı/kişisi, iptal alanları, `RowVersion`, `RequestId`; `ResultText`/`ResultDateUtc` nullable; komutlar: istem oluştur, sonuç gir, iptal, (değerlendirme); mevcut kayıtlar `Resulted`; yeni izin(ler); sözleşme dokümanı | **Var** | D1, D2, D3, D5, D6 | 0 ve 2 ile (2 yalnız yeni tablolar; snapshot çakışması sırayla birleştirilerek çözülür) |
| 2 | Dosya altyapısı (genel) | `IFileStorage` (Application soyutlaması) + yerel disk uygulaması, boyut/tür sınırı, içerik doğrulaması, akış ucu, meta tablo (`StoredFile`: tenant/klinik, yükleyen, zaman, boyut, içerik türü, hash, durum), yetim temizliği, güvenlik testleri | **Var** | D4, D7, D8 | Evet (1 ile; yalnız yeni tablo) |
| 3 | Görüntüleme kaydı (`ImagingRecord`) | Varlık: hayvan, muayene?, ziyaret?, tarih, tür (Röntgen/Ultrason/Fotoğraf/Diğer), açıklama, klinik not, dosya bağı (1..n); Create/Update/Cancel, listeler, izin, sözleşme | **Var** | 2, D6, D9 | 2'den sonra |
| 4 | Lab sonucuna dosya | `LabResult` ↔ `StoredFile` (sonuç PDF/foto) yükle/listele/indir/sil(yumuşak) | **Var** (bağ tablosu) | 1, 2 | 3 ile paralel |
| 5 | Bekleyenler + okuma zenginleştirme | Klinik geneli "bekleyen sonuçlar" ucu (durum + yaş + hayvan/müşteri, klinik kapsamı, sayfalama, indeks); timeline'a durum + görüntüleme türü; muayene ilişkili özet / hayvan özeti alanları; ücret kaynağı **yalnız okuma/öneri** | Olası (bekleyen indeksi) | 1, 3, 4 | Sözleşmeler hazır olunca |
| 6 | Ücret kaynağı bağı | Charge ↔ lab/görüntü kaynağı | Charge'a alan eklenirse var | 1, 3; **Aşama 5 Dilim 5 / FIN ajanı ile koordinasyon** | Charge şemasını başkası değiştirirken değil |
| 7 | Frontend | İstem diyaloğu (hasta/muayeneden), bekleyenler sayfası + Bugün rozeti, sonuç girişi, dosya yükleme/önizleme (hata durumunda form korunur), görüntü arşivi sekmesi, timeline | — | Backend sözleşmeleri | Backend sonrası; 1, 3, 4, 5 parçalı |

Özet: **0 ∥ 1 ∥ 2**, sonra **3 ∥ 4**, sonra 5, 6 (FIN koordinasyonu), frontend dilim dilim. Migration gerektirenler: 1, 2, 3, 4 (+5/6 olasılıkla). Migration'sız: 0, 5'in büyük kısmı. Dalları ayrı yürütürken `AppDbContextModelSnapshot` çakışması olur; migration'lı dilimler sırayla birleştirilmeli (Aşama 3/4/5'te yaşandı). İzin gerektiren dilimlerde seed yalnız birleşik main'den.

## 4. Kullanıcıya sorulacak ürün kararları

| # | Soru | Seçenekler | Öneri ve gerekçe |
|---|---|---|---|
| D1 | Lab istem şekli | **a)** Mevcut `LabResult`'ı duruma çevir (tek kayıt: İstendi → Sonuçlandı). **b)** Ayrı `LabRequest` + `LabResult` iki varlık. **c)** Yalnız bekleyen not | **a** — plan #2/#4 tek kayıt/tek kaynak; "hasta geçmişi ve ilgili işlem aynı kaydı gösterir" (kabul). b gereksiz bağ ve eşitleme yükü. Mevcut kayıtlar "Sonuçlandı" olur. |
| D2 | Kesinleşmiş muayenede lab | **a)** Kilit: ekleme/değişiklik yok (tedavi/reçete gibi). **b)** Yeni **istem** ve **sonuç girişi** kesinleşmiş muayenede serbest (sonuç sonradan gelir), diğer alanlar (test adı, muayene bağı) değişmez. **c)** Tam serbest (bugünkü durum) | **b** — plan #5 "bekleyen sonuç kapanıştan sonra da görünür" ve "sonuç beklemek ziyaret kapanışını engellemez"; sonuç geç gelince hekim kesinleşmiş muayeneden dolayı bloke olmamalı. Kayıt zaman damgalı/kullanıcılı olduğu için denetlenebilir. Karşı risk: kilit kuralı iki yerde (tedavi: tam kilit, lab: kısmi); kural domain'de tek isimli yöntemle tutulur. |
| D3 | Durum kümesi | **a)** İstendi / Sonuçlandı / İptal. **b)** + "Değerlendirildi" ayrı durum. **c)** a + değerlendirme ayrı alan/rozet | **c** — plan #4 sonuç var değerlendirme yok ayrı gösterilir; durum makinesini şişirmeden `Interpretation` + `InterpretedBy/At` alanı ve "Değerlendirme bekliyor" rozeti. "Hekimin değerlendirmesi açık takip işi" ileriye (Aşama 4 kararı: açık iş göstergesi yok). |
| D4 | Dosya depolama yeri | **a)** Sunucu yerel diski (kök yapılandırılır, DB dışı). **b)** Bulut nesne depolama (Azure Blob/S3) ilk günden. **c)** DB'de `varbinary` | **a**, `IFileStorage` arkasında (b'ye geçiş tek uygulama değişikliği); yedek/disk planı kullanıcıya not. c: yedek şişer ama tek yedek; küçük pilot için tartışılabilir. **Dağıtım hedefi bilinmiyor; kullanıcı doğrulamalı.** |
| D5 | Timeline'da bekleyen | **a)** Tek satır, istek tarihinde, sonuçlanınca aynı satır durumu güncellenir. **b)** İstek ve sonuç iki ayrı olay | **a** — plan "aynı kaynak"; iptal varsayılan gizli (Aşama 4 D4). |
| D6 | Yetki | **a)** Dosya yükleme `LabResults.Create/Update`'e bağlı, Imaging ayrı yeni izin (`Imaging.Read/Create/Update/Cancel`). **b)** Her şey tek izinle. **c)** Dosya için ayrı `Files.*` | **a** — Veteriner+yönetim yazar; Sekreter lab/görüntü **okuyabilir mi** ayrı soru: öneri okuyamaz (röntgen klinik içerik), lab metni bugün okuyor (davranış korunur). İptal/silme yalnız Veteriner+yönetim, gerekçeli. |
| D7 | İzinli dosya türleri ve boyut | **a)** JPEG, PNG, WebP, PDF; en çok 10 MB; video/DICOM yok. **b)** + kısa video (≤50 MB). **c)** Boyut daha büyük | **a** — plan #6 "görüntü ve PDF ile sınırlı". Sınır yapılandırmayla değişir, tür listesi koddan. Röntgen JPEG'leri ~1–5 MB çıkarım; **pilot dosya boyutları bilinmiyor**. |
| D8 | Güvenlik düzeyi | **a)** Magic-byte doğrulama + boyut + imzasız akış ucu (Bearer ile blob fetch). **b)** a + kısa ömürlü imzalı URL (img etiketi doğrudan). **c)** a + virüs tarama | **a** ilk sürüm; tarama (c) pilot dış kullanıcı/e-posta dosyası kabul etmiyorsa ertelenir (yalnız personel yükler). Karar: kullanıcı yükleyenin yalnız klinik personeli olduğunu onaylasın. |
| D9 | Imaging kapsamı | **a)** Ayrı `ImagingRecord` (tür, tarih, açıklama, muayene, 1..n dosya). **b)** Yalnız genel "Ek dosya" (lab ve muayene altında). | **a** — ADR-007: genel dosya klinik bağlamı kaybeder; röntgen/ultrason tür alanı gerekli. Genel dosya katmanı (`StoredFile`) a'nın altında ortak olduğundan b'ye geri dönüş ucuz. |
| D10 | Silme/iptal | **a)** Dosya ve görüntü kaydı yumuşak silinir (gerekçe, kim, ne zaman), erişimden kalkar, fiziksel temizlik sonra. **b)** Kalıcı silme | **a** — klinik kayıt bütünlüğü; KVKK silme talebi ayrı süreç (backlog). |
| D11 | Bekleyen sonuç "yaşı" uyarısı | Bekleyen testin kaç gün sonra "gecikti" sayılacağı | İlk sürümde eşik yok, yalnız "X gündür bekliyor" bilgisi; eşik pilot verisiyle. |
| D12 | Ücret kaynağı | Dilim 6 şimdi mi, FIN/Aşama 5 Dilim 5 sonrası mı | Sonra; Charge şeması tek ajanda değişsin. İstem/görüntü kaydı ücret üretmez (sessiz ücret yok). |

## 5. Doğrulanmayanlar

- Hiçbir test çalıştırılmadı, derleme yapılmadı, DB'ye bağlanılmadı, uçlar HTTP ile denenmedi, frontend `ng build/test` çalıştırılmadı.
- Lab'ın kesinleşmiş muayeneye yazılabilir olması (R1) handler'ların ve `ExaminationLinkedRecordLock` kullanıcılarının grep/okumasından çıkarım; başka katmanda (validator, behavior, spec) kilit olmadığı tam taranmadı.
- Lab Create/Update'in `IAuditableRequest`/`ITransactionalRequest` olmadığı yalnız handler'ların `IRequestHandler` imzasından çıkarım; pipeline kaydı tam okunmadı.
- Rol/izin dağılımı `RolePermissionBindings` satır aralıklarından okundu; Sekreter'in yalnız Read aldığı doğrudan test çalıştırılarak doğrulanmadı (`RolePermissionBindingsMatrixTests` çalıştırılmadı).
- Kestrel/IIS/ters vekil istek gövdesi sınırı: kodda ayar bulunmadı; varsayılan (~30 MB) çıkarım, dağıtım ortamı ve proxy sınırları bilinmiyor.
- Dağıtım hedefi (disk/bulut), yedekleme, saklama süresi, pilot kliniğin gerçek dosya türü/boyutu ve cihaz (dijital röntgen) çıktısı bilinmiyor; lab cihazı/dış laboratuvar akışı pilotla netleşmedi.
- `ResultDateUtc`'yi nullable yapmanın etkilediği tüm kod noktaları (liste spec'leri, dashboard, özet, timeline, mapper'lar, frontend) tek tek çıkarılmadı; etki alanı Dilim 1 başında ayrıca taranmalı.
- Frontend: muayene detayında "Lab Sonucu Oluştur" düğmesinin kesinleşmiş durumda görünürlüğü şablon okumasından çıkarım (çalıştırılıp görülmedi).
- Query DB / read model etkisi: lab için read model olmadığı Aşama 4 keşfine dayanıyor, yeniden doğrulanmadı.
- Rakip davranışları (E-Vet, DaySmart) ana plandan alındı; yeniden doğrulanmadı.
