# Gün sonu raporları API sözleşmesi — Dilim D6

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-017 K8 ("Günlük işlem dökümü" ve tarih aralığı "tahsilat/kasa özeti"), vetinity-product `ux/HEDEF-IS-AKISLARI-2026-10.md` bölüm 1.12 (dilim D6), `ux/RAKIP-IS-AKISI-CALISMASI-2026-10.md` (rakipte Günlük İşlem Dökümü, Kasa tarih aralığı, Tahsilat Değişiklik Raporu: yalnız iş kapsamı referans, görünüm kopyalanmaz), ADR-012 (ön muhasebe kapsam dışı), ADR-016 (doğrudan satış). İlgili sözleşmeler: `REGISTERS_API_CONTRACT.md` (rapor ucu deseni), `OUTSTANDING_BALANCE_REPORT_API_CONTRACT.md`, `OPEN_ITEMS_API_CONTRACT.md` (açık iş hesabı tek yerde), `FINANCE_CORRECTION_API_CONTRACT.md` (iptal/düzeltme), `DIRECT_SALE_API_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme + kod aynı dalda. Salt okuma. **Yeni tablo, migration, izin yok.** Kasa/banka/gider/cari (ön muhasebe), kasa mutabakatı, "sayılan nakit" ve kapanış kilidi **kapsam dışı** (ADR-012). Bölüm C (sayılan nakit) pilot sonrasına bırakıldı.

Üç uç, tek ortak "mali olay" kaynağı:

| Uç | Ne | İzin |
|---|---|---|
| `GET /api/v1/reports/daily-transactions` (+ `/export`, `/export-xlsx`) | Gün/aralık için zaman sıralı işlem dökümü | `Payments.Read` **ve** `Charges.Read` |
| `GET /api/v1/reports/cash-summary` (+ `/export`, `/export-xlsx`) | Tarih aralığı tahsilat/kasa özeti | `Payments.Read` |
| `GET /api/v1/reports/end-of-day/open-items` | Gün sonu açık işler | `Visits.Read` (ücret/lab alanları ek izne bağlı) |

---

## 1) Kararlar (rakip pratiği varsayılan; ürün sahibi onayına açık)

- **K-D6-1 — satır = olay.** Dökümde her satır bir **mali olaydır**; bir ücretin oluşması, iptali ve düzeltilmesi ayrı olaylar, ödemenin alınması, iptali ve düzeltilmesi ayrı olaylardır. Olay zamanı: ücret `createdAtUtc`; ödeme `paidAtUtc`; iptal `voidedAtUtc`; düzeltme denetim kaydının zamanı. Sıralama: olay zamanı artan, sonra tür, sonra kayıt kimliği (deterministik).
- **K-D6-2 — iptal bir olaydır, geçmişi silmez.** Dün alınan ödeme bugün iptal edilirse dünün dökümünde "Tahsilat" satırı **iptal edildi** işaretiyle durur, bugünün dökümünde ayrıca "Tahsilat iptali" satırı çıkar. Net tahsilat = alınan − o pencerede iptal edilen (günlük kasa görünümü).
- **K-D6-3 — doğrudan satış ayrı tür değil, işaret.** Satış kendi ücretini ve ödemesini üretir (ADR-016). Çift sayım olmaması için satış ayrı olay yazılmaz; satışın ücret ve ödeme olayları `isDirectSale = true` taşır, toplamlarda ayrı alt satır olarak da verilir ("Satışlar ayrı satır"). Satış iptali, ödeme iptali + ücret iptali olarak görünür.
- **K-D6-4 — düzeltme = denetim kaydı.** Ücret ve ödeme düzeltmesi tabloda saklanmaz (`FINANCE_CORRECTION_API_CONTRACT.md` bölüm 6); düzeltme olayı `AuditLogs`'tan (`Charge.Correct`, gerekçeli `Payment.Update`; yalnız başarılı) okunur. Düzeltme satırında **tutar boştur** (önceki/sonraki tutar saklanmaz), gerekçe doludur. Düzeltilen kaydın kiracı/klinik kapsamı ana tablodan doğrulanır; başka kiracının denetim kaydı görünmez.
- **K-D6-5 — işlemi yapan.** Ücret: `createdByUserId`; ödeme: ödemeyi ücrete tahsis eden (ilk tahsisi yapan) kullanıcı — `Payment` varlığında oluşturan alanı yoktur, bu yüzden tahsisi olmayan **eski (bağlanmamış) ödemelerde boştur**; iptal: `voidedByUserId`; düzeltme: denetim kaydındaki kullanıcı.
- **K-D6-6 — ücret tutarı güncel tutardır.** Düzeltilmiş ücretin "Ücret" satırı güncel tutarı gösterir (ilk tutar saklanmaz). Brüt ücret toplamı bu yüzden düzeltme sonrası değişebilir.
- **K-D6-7 — kasa özeti yalnız tahsilattır.** Ücret/borç gün sonu dökümündedir; kasa özeti `Payments.Read` ile görünür. Kullanıcı kırılımında tahsilat, tahsilatı alana; iptal, iptal edene yazılır (iptali yapan yönetici ise iptal onun satırındadır); satırdaki net bu iki sayının farkıdır, kullanıcının kasası olarak yorumlanmamalıdır (kasa mutabakatı yok).
- **K-D6-8 — gün sınırı** Europe/Istanbul; `from`/`to` takvim günleri, ikisi dahil; en çok 366 gün (defterlerle aynı sınır, `Registers.RangeTooLong`).
- **K-D6-9 — açık işler pencereye bağlanır.** Gün sonu açık işler `IVisitOpenItemsReader.GetSummaryAsync` ile hesaplanır (kural kopyalanmaz); gün sonuna özgü olan yalnız pencere parametreleridir (bölüm 5).

## 2) Ortak kurallar

- Kiracı bağlamı zorunlu (`Tenants.ContextMissing` 400). Klinik kapsamı `payments` raporu ile aynı çözümleyici (`IClinicReadScopeResolver`): `clinicId` veya aktif klinik bağlamı → tek klinik; bağlam yoksa kullanıcının erişebildiği tüm klinikler (Admin/Owner: kiracı geneli). JWT `clinic_id` ile sorgu `clinicId` farklıysa `Context.ClinicConflict` (403) / `Registers.ClinicContextMismatch` (400); atanmamış klinik `Clinics.AccessDenied` (403); kiracıda olmayan klinik `Clinics.NotFound` (404). Başka kiracının kaydı hiçbir koşulda görünmez.
- `from` ve `to` zorunlu (`yyyy-MM-dd`); `from > to` → `Registers.DateRangeInvalid` (400); aralık 366 günü aşarsa `Registers.RangeTooLong` (400).
- Okuma Command DB'dedir (para verisi eventual-consistent projeksiyondan okunmaz). İptal edilmiş ücret/ödeme normalde global sorgu filtresiyle gizlidir; bu raporlar iptalleri göstermek için filtreyi bilerek atlar, kiracı/klinik süzgeci elle uygulanır.
- Yalnızca TRY.
- Yeni izin yoktur. Varsayılan rollerde `Payments.Read` ve `Charges.Read` birlikte verilir (Admin, Owner, ClinicAdmin, Veteriner, Sekreter); özel rol yalnız birine sahipse `daily-transactions` 403 verir (kısmi toplam gösterilmez).

## 3) `GET /api/v1/reports/daily-transactions`

Sorgu parametreleri:

| Parametre | Tip | Not |
|---|---|---|
| `from`, `to` | date | Zorunlu, İstanbul günleri, ikisi dahil. Tek gün için ikisi aynı |
| `clinicId` | guid? | Klinik kapsamı (bölüm 2) |
| `kind` | enum? | `Charge` \| `ChargeVoid` \| `ChargeCorrection` \| `Payment` \| `PaymentVoid` \| `PaymentCorrection`; boş = hepsi |
| `paymentMethod` | enum? | `Cash` \| `Card` \| `Transfer`; verilirse yalnız ödeme türü olan olaylar (tahsilat, tahsilat iptali) gelir |
| `performedByUserId` | guid? | İşlemi yapan kullanıcı (K-D6-5) |
| `page` | int | Varsayılan 1 |
| `pageSize` | int | Varsayılan 50, en çok 200 (üstü sınırlanır) |

Yanıt `DailyTransactionsReportResultDto`:

| Alan | Tip | Not |
|---|---|---|
| `currency` | string | `TRY` |
| `from`, `to` | date | İstenen aralık |
| `totals` | `DailyTransactionTotalsDto` | **Süzgeçlere uyan tüm olaylar** (yalnız sayfa değil) |
| `totalCount` | int | Süzgeçlere uyan olay sayısı |
| `items` | `DailyTransactionItemDto[]` | Sayfadaki satırlar |

`DailyTransactionTotalsDto` (tutarlar decimal, sayılar int):

| Alan | Tanım |
|---|---|
| `grossChargeAmount`, `grossChargeCount` | `Charge` olaylarının toplamı (satış ücretleri dahil) |
| `salesChargeAmount` | Bunun içinden doğrudan satış ücretleri |
| `voidedChargeAmount`, `voidedChargeCount` | `ChargeVoid` olayları |
| `netChargeAmount` | `grossChargeAmount − voidedChargeAmount` |
| `collectedAmount`, `collectedCount` | `Payment` olaylarının toplamı (satış tahsilatları dahil) |
| `salesCollectedAmount` | Bunun içinden doğrudan satış tahsilatları |
| `voidedCollectionAmount`, `voidedCollectionCount` | `PaymentVoid` olayları |
| `netCollectedAmount` | `collectedAmount − voidedCollectionAmount` |
| `correctionCount` | `ChargeCorrection` + `PaymentCorrection` olay sayısı |

`DailyTransactionItemDto`:

| Alan | Tip | Not |
|---|---|---|
| `occurredAtUtc` | datetime | Olay zamanı (K-D6-1); istemci İstanbul'a çevirir, saat buradan |
| `kind` | enum | Yukarıdaki altı değer |
| `referenceId` | guid | Ücret veya ödeme kimliği (kayda bağlantı için) |
| `clientId`, `clientName` | guid, string | |
| `petId`, `petName` | guid?, string? | Ücrette hayvan varsa; ödemede varsa |
| `description` | string? | Ücret açıklaması; ödemede ödeme notu |
| `amount` | decimal? | Pozitif tutar; yönü `kind` belirler. **Düzeltme satırında null** |
| `paymentMethod` | enum? | Yalnız `Payment`/`PaymentVoid`/`PaymentCorrection` |
| `isDirectSale` | bool | K-D6-3 |
| `isVoided` | bool | Kaydın **şu anki** durumu iptal (ör. dünkü tahsilat bugün iptal edildiyse dünkü satırda `true`) |
| `reason` | string? | İptal/düzeltme gerekçesi |
| `performedByUserId`, `performedByName` | guid?, string? | K-D6-5; ad `DisplayName` yoksa e-posta |
| `clinicId` | guid | |

Sıralama: `occurredAtUtc` artan, sonra `kind`, sonra `referenceId`. Örnek: 10:00 ücret 600 TL, 10:30 nakit tahsilat 600 TL, 16:00 yönetici tahsilatı iptal → döküm 3 satır; toplamlar `grossCharge 600, collected 600, voidedCollection 600, netCollected 0`; iptal satırı `reason` taşır.

### Dışa aktarma

- `GET /reports/daily-transactions/export` (CSV: UTF-8 BOM, `;`, Excel TR) ve `/export-xlsx`. Parametreler listedekiyle aynı, **`page`/`pageSize` hariç**; tüm eşleşen olaylar gelir. Süzgeç ve sıralama JSON ile **aynı okuyucu çağrısından** geçer (ekran = CSV = XLSX).
- Tavan `ReportsSharedLimits.MaxExportRows` (50.000); aşılırsa `Registers.ExportTooManyRows` (400).
- Kolonlar (teknik ID yok): `Tarih Saat` (İstanbul, `dd.MM.yyyy HH:mm`), `Tür`, `Müşteri`, `Hayvan`, `Açıklama`, `Tutar`, `Ödeme Türü`, `Kaynak` (`Doğrudan satış` / boş), `Durum` (`Geçerli` / `İptal edildi`), `Gerekçe`, `İşlemi Yapan`. Tutar CSV'de `0,00`, XLSX'te sayı biçimli hücre; tarih-saat XLSX'te tarih-saat biçimli hücre. Düzeltme satırında tutar boş.
- Toplam satırı dosyaya eklenmez (ekrandaki toplam `totals` ile gösterilir; dosyada Excel toplamı kullanıcıdadır).
- Dosya adı: `gunluk-islem-dokumu-yyyyMMdd.csv` / `.xlsx` (İstanbul tarihi).
- Tür etiketleri: Ücret, Ücret iptali, Ücret düzeltmesi, Tahsilat, Tahsilat iptali, Tahsilat düzeltmesi. Ödeme türü: Nakit, Kart, Havale-EFT.

## 4) `GET /api/v1/reports/cash-summary`

Parametreler: `from`, `to`, `clinicId` (bölüm 2). Yalnız tahsilat olayları (`Payment`, `PaymentVoid`) kullanılır; düzeltmeler dahil değildir.

Yanıt `CashSummaryReportDto`:

| Alan | Tip | Not |
|---|---|---|
| `currency`, `from`, `to` | | |
| `totals` | `CashSummaryAmountsDto` | Aralığın toplamı |
| `byMethod` | `CashSummaryMethodDto[]` | Her zaman üç satır (`Cash`, `Card`, `Transfer`), boş olanlar sıfır |
| `byDay` | `CashSummaryDayDto[]` | Yalnız hareketi olan İstanbul günleri, tarih artan; `cashNetAmount`, `cardNetAmount`, `transferNetAmount` ile birlikte |
| `byUser` | `CashSummaryUserDto[]` | K-D6-7; tahsilatı veya iptali olan kullanıcılar, `netAmount` azalan, sonra ad. Kullanıcısı bilinmeyen (eski ödeme) tek satır `userId = null` |
| `voidedPayments` | `CashSummaryVoidedPaymentDto[]` | Aralıkta iptal edilen tahsilatlar, iptal zamanı artan: `paymentId`, `paidAtUtc`, `voidedAtUtc`, `clientId`, `clientName`, `amount`, `paymentMethod`, `isDirectSale`, `collectedByUserId/Name`, `voidedByUserId/Name`, `reason` |

`CashSummaryAmountsDto`: `collectedAmount`, `collectedCount`, `voidedAmount`, `voidedCount`, `netAmount` (= tahsil − iptal), `salesCollectedAmount` (doğrudan satış tahsilatı, tahsile dahil). Satırlar (`byMethod`, `byDay`, `byUser`) aynı alanları taşır.

- Hesap: tahsilat olayı `paidAtUtc` penceredeyse sayılır; iptal olayı `voidedAtUtc` penceredeyse sayılır (K-D6-2). Hareket sayısı tavanı 50.000 (`CashSummary.TooManyRows` 400: "aralığı daraltın"); gün/kullanıcı/yöntem toplamı bellekte aynı olay kümesinden türetildiği için kendi içinde tutarlıdır.
- Dışa aktarma: `/cash-summary/export` (CSV) ve `/export-xlsx`. Düz tablo, kolonlar `Bölüm`, `Ad`, `Tahsilat Adedi`, `Tahsilat Tutarı`, `İptal Adedi`, `İptal Tutarı`, `Net`; bölümler `Toplam` (tek satır), `Ödeme Türü`, `Gün` (tarih `dd.MM.yyyy`), `Kullanıcı`. Aynı veriden ekranla birebir üretilir. `voidedPayments` listesi dosyada yoktur (iptal listesi için `daily-transactions?kind=PaymentVoid`). Dosya adı `kasa-ozeti-yyyyMMdd.csv` / `.xlsx`.

## 5) `GET /api/v1/reports/end-of-day/open-items`

Parametreler: `from`, `to` (gün sonu penceresi; tek gün için aynı), `clinicId`. **Tek klinik zorunlu** (açık iş hesabı klinik düzeyindedir): `clinicId` ya da aktif klinik bağlamı yoksa `EndOfDay.ClinicScopeRequired` (400).

Yanıt: `{ "from", "to", "clinicId", "openItems": OpenItemsSummaryDto }`; `OpenItemsSummaryDto` `OPEN_ITEMS_API_CONTRACT.md` bölüm 4'teki ile aynıdır (alanlar izne göre null olabilir) ve **aynı arayüzle** (`IVisitOpenItemsReader`) hesaplanır. Pencere eşlemesi:

| Alan | Gün sonu anlamı |
|---|---|
| `unchargedVisitCount` | Geliş zamanı `[from günü başı, to günü sonu)` içinde olan ve "ücret yok" kuralına uyan ziyaretler (30 günlük Bugün penceresi yerine seçilen pencere) |
| `incompleteVisitCount` | `to` gününün sonuna kadar gelip **şu anda** tamamlanmamış geliş (Bugün'den farkı: aynı gün açık kalanlar da sayılır; gün sonunda asıl aranan budur) |
| `outstandingVisitCount`, `outstandingAmount` | Klinik geneli güncel durum (pencereden bağımsız; Bugün ile aynı) |
| `awaitingLabCount` | Klinik geneli güncel durum (pencereden bağımsız) |

Not: Geçmiş bir günün gün sonu bugünkü durumu gösterir (açık iş anlık görüntü değildir; geçmiş tarihli anlık görüntü saklanmaz). Bu uç ziyaret listesi vermez; satıra inme (hangi ziyaret) için ayrı liste ucu ürün kararıdır (D2 ile aynı bırakıldı).

## 6) Hata kodları

| Kod | HTTP |
|---|---|
| `Tenants.ContextMissing` | 400 |
| `Registers.DateRangeInvalid`, `Registers.RangeTooLong` | 400 |
| `Registers.ClinicContextMismatch` (savunma), `Context.ClinicConflict` | 400 / 403 |
| `Registers.ExportTooManyRows` (daily-transactions) | 400 |
| `CashSummary.TooManyRows` | 400 |
| `EndOfDay.ClinicScopeRequired` | 400 |
| `Clinics.AccessDenied` | 403 |
| `Clinics.NotFound` | 404 |
| (yetkisiz) | 401 / 403 |

Not: tarih ve klinik doğrulaması defterlerle **aynı bileşenden** geçtiği için kodlar `Registers.*` önekini taşır; istemci kodu önekten bağımsız olarak `extensions.code` ile eşler.

## 7) Uygulama notları

- **Tek mali olay kaynağı:** `FinanceEvents` (Infrastructure, `Persistence/Repositories/Reports`) ücret oluşumu, ücret iptali, ödeme alımı, ödeme iptali ve denetim kaydından düzeltmeyi tek `UNION ALL` sorgusunda birleştirir. Döküm okuyucusu (toplam + sayfa) ve kasa özeti okuyucusu **aynı** kaynaktan beslenir; kural tek yerde.
- Toplamlar tek gruplu sorgudur (tür × satış işareti); sayfa SQL'de `OrderBy/Skip/Take`. Ad/hayvan/kullanıcı sayfa kümesiyle birer toplu sorguda okunur (N+1 yok).
- Satış işareti `Sales.ChargeId` / `Sales.PaymentId` ile belirlenir (ücret/ödeme tablosunda satış alanı yoktur).
- Ödeme sahibi kullanıcı `PaymentAllocations` (iptalde geri alınmış tahsis dahil) üzerinden okunur.
- **İndeks:** `Charges (TenantId, ClinicId, CreatedAtUtc)` mevcut; `Payments` için `PaidAtUtc`/`VoidedAtUtc` tarih aralığı ve `AuditLogs.Action` indeksleri mevcut olanlarla çalışır, yeni indeks eklenmedi (migration yok). Gerçek veri hacminde sorgu ölçümü **yapılmadı** (bilinen eksik).
- Yeni `RegisterColumn` türleri (`Time`, `Money`) ortak CSV/XLSX yazıcılarına eklendi: tarih-saat ve 2 ondalıklı tutar. Mevcut defter çıktıları değişmez.

## 8) Frontend için değişenler

1. **Rapor Merkezi / Gün sonu** sayfası üç uca bağlanır: bölüm A açık işler (`end-of-day/open-items`), bölüm B tahsilat (`cash-summary` + isteğe bağlı `daily-transactions`), iptal/düzeltme listesi (`daily-transactions?kind=PaymentVoid|ChargeVoid|...` veya `cash-summary.voidedPayments`). Yazdır/PDF istemci tarafıdır.
2. Menü/kart izni: `daily-transactions` için `Payments.Read` **ve** `Charges.Read`; `cash-summary` için `Payments.Read`; açık işler için `Visits.Read` (alanlar null olabilir: "yetki yok", "yok" demek değildir).
3. Tarih seçici: tek gün için `from = to`. Bugün'den "Günü kapat" kısayolu: `from = to = bugün`, `clinicId = aktif klinik`.
4. Döküm tablosu: saat (`occurredAtUtc` İstanbul), tür etiketi (`kind`), müşteri/hayvan, tutar, ödeme türü, işlemi yapan, durum (`isVoided` → "İptal edildi"; `ChargeVoid`/`PaymentVoid`/`...Correction` satırları iptal/düzeltme rozeti), gerekçe, `isDirectSale` rozeti. Düzeltme satırında tutar boş (`—`).
5. Toplam şeridi `totals` ile (sayfadan değil, tüm eşleşen kümeden). Net tahsilat = `netCollectedAmount`. Satışlar için `salesChargeAmount`/`salesCollectedAmount` ayrı satır.
6. `cash-summary.byMethod` her zaman üç satır döner; `byDay` yalnız hareketli günleri içerir (boş günleri istemci doldurur).
7. Açık işler anlık görüntü değildir (bölüm 5 notu); geçmiş gün seçilince ekranda "şu anki durum" ifadesi yazılmalı.
8. Dışa aktarma düğmeleri aynı süzgeçlerle `export`/`export-xlsx` uçlarını çağırır.

## 9) Bilinen eksikler

- Düzeltme olaylarında önceki/sonraki tutar yoktur (saklanmıyor); ücret satırı güncel tutardır (K-D6-6).
- Eski (bağlanmamış) ödemelerde işlemi yapan kullanıcı bilinmez; kullanıcı kırılımında "bilinmiyor" satırı görünür.
- Ödeme `paidAtUtc` kullanıcı tarafından geçmişe verilebilir; geriye dönük girilen tahsilat eski günün dökümüne düşer (kayıt zamanı ayrıca tutulmuyor).
- Kasa mutabakatı, sayılan nakit, günü kilitleme, iade kaydı, gider/banka yoktur (ADR-012).
- Gerçek veri hacmiyle sorgu ölçümü yapılmadı.
- Denetim kaydı yazımı başarısız olursa (yazım hatası) düzeltme olayı döküme girmez; düzeltme etkisi (güncel tutar) yine de görünür.

## 10) Test odağı

- Application: tarih aralığı/klinik doğrulama (defter bileşeni), tavan, CSV/XLSX içerik ve biçim (saat, 2 ondalık tutar, durum/kaynak etiketleri), gün sonu klinik zorunluluğu ve pencere parametreleri (okuyucuya geçen değerler).
- Entegrasyon (LocalDB, kendi kiracısıyla): olay sırası ve toplamlar (ücret, tahsilat, iptal, net), satışın ücret/ödemesinin `isDirectSale` işareti ve çift sayılmaması, dünkü ödemenin bugün iptali (iki günde doğru satır ve net), düzeltme olayı (denetim kaydından, tutarsız), ekran ≡ CSV ≡ XLSX, kasa özeti yöntem/gün/kullanıcı toplamlarının döküm toplamlarıyla eşitliği, başka kiracı (IDOR), klinik kapsamı, yetki (401/403), açık iş özetinin Bugün ile aynı arayüzden gelmesi.
