# Açık işler (hesaplanan göstergeler) API sözleşmesi — Dilim D2

Backend tek doğruluk kaynağıdır. Kaynak: vetinity-product `ux/HEDEF-IS-AKISLARI-2026-10.md` (dilim D2, bölüm 1.1, 1.4, 1.12), `ux/ARAYUZ-IS-AKISI-DENETIMI-2026-10.md` (B-02, B-15), ADR-017 K6 ve "Rakip iş akışı çalışması sonrası ayarlar". Ücret, tahsis, lab ve geliş sözleşmeleri: `CHARGES_API_CONTRACT.md`, `PAYMENT_ALLOCATION_API_CONTRACT.md`, `LAB_RESULT_STATUS_API_CONTRACT.md`, `VISITS_API_CONTRACT.md`.

> **Durum:** Sözleşme. **Yeni uç, tablo, migration, izin yoktur.** Göstergeler mevcut `Visit`, `Charge` (tahsis sayaçları dahil), `Examination`, `Treatment`, `Administration`, `Vaccination`, `Hospitalization`, `LabResult` verisinden **hesaplanır**; hiçbiri saklanmaz. Yüzey: `GET /api/v1/visits/today` yanıtına eklenen alanlar (madde 3, 4) ve gün sonu raporunun (D6) aynı kuralı kullanması için tek hesap yeri (madde 6).

---

## 1) Kavramlar

- **Ücretlendirilebilir işlem:** Hekimin kaydettiği, normalde müşteriye ücret doğuran iş. Hekim ücret ekleyemez (ADR-012); bu yüzden işlem var ama ücret yoksa iş resepsiyonun önüne düşmez (B-02). Kapsam madde 2'de.
- **Ücret yok (`ChargeMissing`):** Ziyarette en az bir ücretlendirilebilir işlem var, ama ziyarete bağlı (iptal edilmemiş) **hiç ücret kaydı yok**. Kalem düzeyi yoktur: tek bir ücret kaydı varsa (tutarı işlemleri karşılasa da karşılamasa da) "ücret yok" sayılmaz. Kalem düzeyi D4 (ücret önerisi) kapsamıdır.
- **Ödeme bekliyor / kısmi:** Ziyarete bağlı ücret var ve açık tutar (`Σ amount − Σ allocatedAmount`) > 0. Hiç tahsis yoksa `PaymentPending`, bir kısmı tahsis edilmişse `PartiallyPaid`, açık tutar 0 ise `Paid`.
- **Lab bekliyor:** `status = Requested` LabResult (istendi, sonuç girilmedi). `Cancelled` ve `Resulted` sayılmaz. "Değerlendirme bekliyor" (`awaitingInterpretation`) bu sayıya girmez.
- **Tamamlanmamış geliş:** Yanlış geliş işaretsiz, `careStatus` ≠ `Completed` ve geliş zamanı **içinde bulunulan İstanbul gününün başlangıcından önce** olan Visit (önceki günden kalan açık geliş). Satır düzeyinde göstergesi mevcut `isCarriedOver` alanıdır.
- **Borçlu çıkan:** `careStatus = Completed`, yanlış geliş işaretsiz ve açık tutarı > 0 olan Visit.

Ziyaret–kayıt bağı (tek kural): bir işlem/ücret/lab, kendi `visitId` alanıyla ziyarete bağlanır; yoksa **bağlı muayenesinin** `examination.visitId` değeriyle (ücret, lab ve yatış için `visitId ?? examination.visitId`; tedavi, uygulama, aşı ve muayene için muayene üzerinden). Ziyarete (doğrudan ya da muayene üzerinden) bağlanamayan kayıt ziyaret göstergesine girmez.

## 2) Ücretlendirilebilir işlem kapsamı (kodu okuyarak belirlendi)

Ziyaret başına her kayıt **bir işlem** sayılır (`chargeableWorkCount`).

| Tür | Sayılan kayıt | Ziyaret bağı | Görme izni |
|---|---|---|---|
| Muayene | Her muayene (taslak veya kesinleşmiş) | `visitId` | `Examinations.Read` |
| Tedavi | Her tedavi | muayene → `visitId` | `Treatments.Read` |
| Uygulama (stoktan düşen) | İptal edilmemiş `Administration` | muayene → `visitId` | `Administrations.Read` |
| Aşı | `status = Applied` aşı; **bağlı iptal edilmemiş bir uygulaması (`Administration.vaccinationId`) varsa ayrıca sayılmaz** (stoklu aşı tek girişte hem aşı hem uygulama üretir; çift sayım olmaz) | muayene → `visitId` | `Vaccinations.Read` |
| Yatış | Her yatış (aktif veya taburcu) | `visitId` veya muayene → `visitId` | `Hospitalizations.Read` |

Kapsam dışı (gerekçeli): **reçete** (ilaç satışı ayrı akış, ADR-016 karar 9), **lab** (muayene ücretinin parçası sayılır; kalem düzeyi D4'te), **`Scheduled`/`Cancelled` aşı** (uygulanmadı), **iptal edilmiş uygulama**, ziyarete bağlanamayan (muayenesiz, ziyaretsiz) kayıt ve doğrudan satış (Sale kendi ücretini üretir). Ürün kapsamı genişletirse yalnızca bu tablo ve tek hesap yeri (madde 6) değişir.

## 3) Bugün satırına eklenen alanlar (`TodayItemDto`)

Yalnızca **Visit satırlarında** dolu olabilir; planlı (Visit'siz) randevu satırında hepsi `null`. Hepsi eklemedir.

| Alan | Tip | Not |
|---|---|---|
| `chargeIndicator` | enum? | `NoChargeableWork` \| `ChargeMissing` \| `PaymentPending` \| `PartiallyPaid` \| `Paid`. Önce ücret varlığına bakılır: ücret varsa açık tutara göre `Paid`/`PartiallyPaid`/`PaymentPending`; ücret yoksa işlem varsa `ChargeMissing`, yoksa `NoChargeableWork`. **`Charges.Read` yoksa `null`** |
| `chargeableWorkCount` | int? | Madde 2'deki kayıt sayısı (yalnız kullanıcının görebildiği türler). `Charges.Read` yoksa `null` |
| `awaitingLabCount` | int? | Ziyarete bağlı `Requested` lab sayısı. `LabResults.Read` yoksa `null` |

- Mevcut `chargedAmount`, `allocatedAmount`, `outstandingAmount` artık ziyarete **muayene üzerinden bağlı ücretleri de** kapsar (`visitId ?? examination.visitId`); önceden yalnızca doğrudan `visitId` taşıyan ücretler toplanıyordu (düzeltme: gösterge ile tutarın aynı kuralı kullanması için). `Charges.Read` yoksa aynen `null`.
- `paymentIndicator` (tahsilat kaydı var/yok) **değişmez**; ücret/borç sorusunu `chargeIndicator` yanıtlar. İkisi farklı sorulardır (ilki randevu/muayene bağlı tahsilat kaydı, ikincisi ücret–tahsis).
- `isCarriedOver = true` satırı "tamamlanmamış geliş" göstergesidir.
- Hiçbiri `careStatus`'u etkilemez.

İstemci özeti: ücret yok → `chargeIndicator = ChargeMissing`; ödeme bekliyor/kısmi → `PaymentPending`/`PartiallyPaid`; lab bekliyor → `awaitingLabCount > 0`; tamamlanmamış geliş → `isCarriedOver`.

## 4) Özet (`TodayDto.openItems`)

`GET /visits/today` yanıtında `openItems` nesnesi (veya `null`). **Klinik geneli güncel durumdur**: seçilen `localDate` ve `responsibleVeterinarianUserId` süzgecinden **etkilenmez** (`activeHospitalizations` ile aynı gerekçe: açık iş tarihe bağlı değildir). `voided=true` görünümünde `null`.

| Alan | Tip | Tanım | Görme izni |
|---|---|---|---|
| `unchargedVisitCount` | int? | "Ücret yok" olan Visit sayısı; **yalnızca son 30 İstanbul günü** (aşağıya bakın) | `Charges.Read` |
| `outstandingVisitCount` | int? | Borçlu çıkan Visit sayısı (madde 1) | `Charges.Read` |
| `outstandingAmount` | decimal? | Borçlu çıkan Visit'lerin toplam açık tutarı (TRY) | `Charges.Read` |
| `awaitingLabCount` | int? | Klinikteki tüm `Requested` lab kayıtları (ziyarete bağlı olmayanlar dahil) | `LabResults.Read` |
| `incompleteVisitCount` | int | Tamamlanmamış geliş sayısı (madde 1) | `Visits.Read` (uç zaten gerektirir) |

- **HBS (ADR-036 Dilim 2):** `hbsNotEnteredPrescriptionCount` (`Prescriptions.Read`) ve `hbsNotEnteredVaccinationCount` (`Vaccinations.Read`), son 30 İstanbul günü; ayrıntı `HBS_ASSISTANT_API_CONTRACT.md` bölüm 7. Gün sonu raporunda `null`.
- **Pencere (yalnız `unchargedVisitCount`):** geliş zamanı `[bugünün İstanbul başlangıcı − 30 gün, şimdi)` aralığında. Gerekçe: ücret sistemi öncesi geliş kayıtlarının hiç ücreti yoktur; sınırsız sayım geçmişi sahte "kaybolan gelir" gösterir. Diğer sayılar ücret/lab sistemi sonrası verilerden oluştuğu veya gerçekten açık olduğu için pencere almaz. Sabit `OpenItemsDefaults.UnchargedLookbackDays = 30`.
- **İzin süzmesi:** kullanıcının `Read` izni olmayan kayıt türü ne sayılır ne gösterilir; ilgili alan `null` döner. Ücretsiz-işlem sayımında yalnız görünür türler işlem sayılır (ör. `Administrations.Read` yoksa uygulama "ücret yok" nedeni olmaz); bu, yetkisiz kullanıcının yetkisi olmadığı türün varlığını çıkarsamasını önler. Varsayılan rollerin (Admin, Owner, ClinicAdmin, Veteriner, Sekreter) hepsi bu okuma izinlerine sahiptir.
- Yeni izin yoktur; `Visits.Read` yeterlidir (ücret/lab alanları ek izne bağlı olarak `null` olur).

## 5) Güvenlik ve performans

- **Kiracı/klinik:** Her sorgu `TenantId` ve `ClinicId` ile süzülür; klinik, Bugün'ün mevcut klinik kapsamı çözümünden gelir (atanmamış klinik → `Clinics.AccessDenied`, değişmedi). Satır alanları yalnızca o Bugün satırlarının `visitId`'leri için hesaplanır.
- **N+1 yok:** Satır alanları tüm ziyaretler için **sabit sayıda** (tür başına bir gruplu) sorguyla hesaplanır; özet yalnız sayı sorgularıdır (SQL tarafında `COUNT`/`SUM`, satır çekilmez). Satır sınırı değişmedi: klinik/gün başına en çok 500 satır, aşımda `Visits.TodayLimitExceeded`.
- **Gün sınırı:** `Europe/Istanbul` → UTC (`OperationDayBounds`, mevcut Bugün ile aynı).
- Yeni indeks/migration yoktur; gerçek veri hacminde sorgu ölçümü yapılmadı (bilinen eksik).

## 6) Hesap kuralı tek yerde (gün sonu D6 için)

Kural tek bileşendedir: `IVisitOpenItemsReader` (Application, `Visits/ReadModels`), uygulaması `VisitOpenItemsReader` (Infrastructure). Bugün işleyicisi yalnızca bu arayüzü çağırır; kural kopyalanmaz.

- `GetForVisitsAsync(scope, visitIds)`: ziyaret başına satır alanları (madde 3) ve ücret tutarları.
- `GetSummaryAsync(request)`: özet sayılar (madde 4). Gün sonu raporu (D6) aynı arayüzü, kendi pencere parametreleriyle çağırır; ayrı bir "ücretsiz ziyaret" kuralı yazılmaz.
- `OpenItemsScope.For(tenantId, clinicId, permissions)`: kullanıcının görebildiği türleri tek yerde çıkarır.

## 7) Frontend için değişenler

1. `GET /visits/today` satırlarında yeni alanlar: `chargeIndicator`, `chargeableWorkCount`, `awaitingLabCount` (hepsi nullable; `null` = yetki yok veya planlı satır, "yok" anlamına gelmez).
2. `TodayDto.openItems`: üst şerit/rozet için özet (`null` olabilir, alan alan da `null` olabilir).
3. Önerilen çipler: **Ücret yok** (`ChargeMissing`, `chargeableWorkCount` ile), **Ödeme bekliyor** (`PaymentPending`), **Kısmi** (`PartiallyPaid`), **Lab bekliyor** (`awaitingLabCount > 0`), **Önceki günden açık** (`isCarriedOver`). `NoChargeableWork` ve `Paid` için çip gösterilmez.
4. `chargedAmount`, `allocatedAmount`, `outstandingAmount` artık muayene bağlı ücretleri de içerir; sayılar önceki sürüme göre **artabilir** (hata düzeltmesi).
5. `paymentIndicator` olduğu gibi kalır; "ödendi" anlamı taşımaz.
6. Bu sürüm ücret **kalemi** göstermez; "n işlem" yalnız sayıdır. Kalem düzeyi ve "Ücretlendir" paneli D4'tür.
7. Özet sayılarının açacağı klinik geneli liste (önceki günler dahil) için ayrı uç yoktur; D2 yalnız sayı ve satır göstergeleri verir. Ayrı liste ucu ürün kararı/ayrı iş olarak bırakıldı.

## 8) Kararlar (rakip pratiği varsayılan)

- **K-D2-1:** İşlem kapsamı madde 2 (tedavi, uygulama, aşı, yatış, muayene). Rakip pratiği: işlem doğuran her kayıt faturalanabilirdir; reçete ve lab kalem düzeyinde D4'e bırakıldı.
- **K-D2-2:** "Ücret yok" ziyaret düzeyindedir (kalem yok), tek ücret kaydı yeterlidir. Pilot-öncesi küçük sürüm kararı (HEDEF §1.4).
- **K-D2-3:** Tamamlanmamış geliş = önceki günden açık kalan. Aynı gün açık olan zaten Bugün kuyruğundadır.
- **K-D2-4:** Özet klinik geneli, hekim/tarih süzgecinden bağımsızdır; ücretsiz ziyaret sayımı 30 gün pencerelidir.
- **K-D2-5:** Borçlu çıkan yalnız Visit'e bağlı ücretlerden hesaplanır; ziyaretsiz müşteri borcu için mevcut `clientOutstandingAmount`, bakiye ve borç raporu (`OUTSTANDING_BALANCE_REPORT_API_CONTRACT.md`) geçerlidir.

## 9) Bilinen eksikler

- Kalem düzeyi yok (D4); işlem var, ücret eksik tutarlı durumu yakalanmaz.
- Muayenesiz ve ziyaretsiz kaydedilmiş işlemler (ör. `examinationId` boş tedavi) ziyarete bağlanamaz, göstergeye girmez.
- Ücretsiz sayma (gerekçeli) kavramı yok; bilerek ücretsiz bırakılan ziyaret "ücret yok" görünür (D4'te kapanır).
- Gerçek veri hacmiyle sorgu ölçümü yapılmadı.
