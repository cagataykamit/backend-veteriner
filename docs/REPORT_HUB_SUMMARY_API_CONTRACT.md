# Rapor Merkezi ana ekran özeti — API sözleşmesi (ADR-052)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-052-ozet-kaldirilir-rapor-merkezi-sorular.md` (K3–K7), tasarım FE deposu `design/raporlar/B.html` ve `README.md` (B · Sorular). Hata zarfı standart `ProblemDetails` + `extensions.code` (`BACKEND-CONTRACT-STANDARD.md` §9).

> **Durum:** onaylandı (2026-11-20, kullanıcı: "Onaylıyorum"; açık noktalar öneriye göre, §10). Dal `feature/rapor-merkezi-ozet`. Migration yok, yeni izin yok, yeni hesap kuralı yok: her sayı, aynı veriyi gösteren raporun ya da sayfanın kuralından gelir.

## 0) Karar: tek uç

K6'daki iki yoldan **tek uç** seçildi: `GET /api/v1/reports/hub-summary`. Gerekçe:

- B ekranı sekiz kart ve beş uyarı çizer. Özet uçlarını (`/dashboard/*`) kullanmak en az dört istek demek; üstelik o uçlar `Dashboard.Read` (yalnız yönetici) ister. Rapor Merkezi'ni sekreter ve hekim de açar (K4: kart, raporun izniyle görünür).
- `performance-summary` deseni hazır: uç tek izne bağlı değildir, her bölüm raporunun izniyle dolar, izin yoksa `null` döner. Böylece ana ekran yeni veri açmaz.
- Özet uçları bu uca taşınmaz. Hiçbir hesap kopyalanmaz; uç mevcut okuyucuları ve spec'leri çağırır. `/dashboard/*` uçları Özet kaldırıldıktan sonra ayrı işte silinir (K6, K7).

## 1) Uç

`GET /api/v1/reports/hub-summary?period=week`

| Parametre | Tür | Not |
|---|---|---|
| `period` | `today` \| `week` \| `month` | Zorunlu. Dönem seçici (Bugün / Bu hafta / Bu ay) |
| `clinicId` | GUID, opsiyonel | Boşsa aktif klinik bağlamı (JWT `clinic_id`). Bağlamla çelişirse 400. **Tek klinik zorunlu** (K3: sayılar seçili şubeye göre); ne `clinicId` ne aktif klinik varsa 400 |

Kimlik: personel JWT, `[Authorize]`, policy yok. Kiracı JWT'den; klinik `IClinicReadScopeResolver` ile doğrulanır (atanmamış klinik 403).

## 2) Dönem pencereleri

Tüm pencereler İstanbul saatiyle, yarı açık `[başlangıç, bitiş)`. Kural tek yerde: yeni `ReportHubPeriod` (Application/Reports/Hub), `OperationDayBounds` ve `OperationPeriodBounds` üzerine kurulur.

| `period` | Bu dönem | Karşılaştırma dönemi (yalnız ciro farkı için) |
|---|---|---|
| `today` | bugün 00:00 → şimdi | dün 00:00 → şimdi − 1 gün ("dün aynı saate göre") |
| `week` | bu haftanın Pazartesi 00:00 → şimdi (ISO hafta) | geçen Pazartesi 00:00 → şimdi − 7 gün ("geçen haftaya göre") |
| `month` | ayın 1'i 00:00 → şimdi | geçen ayın 1'i 00:00 → geçen ayın aynı günü ve saati; geçen ay daha kısaysa ayın sonu ("geçen aya göre") |

- Karşılaştırma **aynı geçen süre** üzerindendir. Yarım ay, geçen ayın tamamıyla karşılaştırılmaz.
- Dönemden bağımsız bölümler: `todayCash` (her zaman bugün), `outstanding` ve `attention` (şu anki durum). Tasarımda bu kartlar dönem seçiciden etkilenmez.

### 2.1 Eğilim serileri

| `period` | Nokta | Nokta sayısı |
|---|---|---|
| `today` | Gün; **son 7 gün**, bugün dahil (bugün şimdiye kadar) | 7 |
| `week` | Gün; Pazartesi → bugün | 1–7 |
| `month` | Hafta (Pazartesi başlar, ayın başı ve şimdi ile kırpılır) | 1–6 |

Nokta şekli: `{ "from": "2026-11-17", "to": "2026-11-17", "value": 0 }`. `from` ve `to` İstanbul takvim günleri, ikisi dahil. Hareketsiz nokta `0` ile döner; seri boşluk içermez, eskiden yeniye sıralıdır. `today` serisinin 7 günü, son 7 günü gösteren eski `last7DaysPaid` ve `last7DaysAppointments` alanlarının yerini alır.

## 3) Yanıt (200)

```json
{
  "period": "week",
  "clinicId": "…",
  "currency": "TRY",
  "generatedAtUtc": "2026-11-20T09:30:00Z",
  "window": { "from": "2026-11-17", "to": "2026-11-20", "previousFrom": "2026-11-10", "previousTo": "2026-11-13" },

  "todayCash": {
    "netCollectedAmount": 0, "collectedCount": 0,
    "last7Days": [ { "from": "2026-11-14", "to": "2026-11-14", "value": 0 } ],
    "mainRegisterBalance": 0
  },
  "turnover": {
    "netChargeAmount": 0, "previousNetChargeAmount": 0,
    "series": [ { "from": "2026-11-17", "to": "2026-11-17", "value": 0 } ]
  },
  "collections": {
    "netAmount": 0, "collectedCount": 0,
    "byMethod": [ { "paymentMethod": "Card", "netAmount": 0 }, { "paymentMethod": "Cash", "netAmount": 0 }, { "paymentMethod": "Transfer", "netAmount": 0 } ]
  },
  "outstanding": { "totalOutstandingAmount": 0, "clientCount": 0, "maxClientOutstandingAmount": 0 },
  "appointments": {
    "totalCount": 0, "noShowCount": 0,
    "statusCounts": { "scheduled": 0, "completed": 0, "cancelled": 0, "noShow": 0 },
    "series": [ { "from": "2026-11-17", "to": "2026-11-17", "value": 0 } ]
  },
  "examinations": { "count": 0, "series": [ { "from": "2026-11-17", "to": "2026-11-17", "value": 0 } ] },
  "vaccinations": { "appliedCount": 0, "upcomingCount": 0 },
  "attention": {
    "hbsNotEnteredPrescriptions": { "count": 0, "oldestAtUtc": null },
    "hbsNotEnteredVaccinations": { "count": 0, "oldestAtUtc": null },
    "endOfDayUnchargedVisitCount": 0,
    "overdueVaccinationCount": 0,
    "lowStockProductCount": 0,
    "overdueAppointmentCount": 0
  }
}
```

`window.to` ve `window.previousTo` takvim günüdür (bugün ve karşılaştırma döneminin son günü); saat kesimi §2'deki gibidir.

## 4) Bölümler, izinler ve kaynaklar

İzin yoksa bölüm (ya da alan) `null` döner. İzinler, aynı veriyi gösteren rapor ucunun izinleridir. Panel kartı ayrıca bağlandığı raporun menü iznine bakar (K4, FE'de tek yer `REPORT_SECTIONS`).

| Bölüm / alan | İzin | Kart | Kaynak (hesap tek yerde) |
|---|---|---|---|
| `todayCash` | `Payments.Read` | Bugünün kasası tuttu mu? | Kasa özeti (`CashSummaryLoader`): bugünün `totals.netAmount`, `collectedCount`; `last7Days` = son 7 günün `byDay.netAmount` |
| `todayCash.mainRegisterBalance` | `CashRegisters.Read` | aynı | Ana kasanın şu anki bakiyesi, `CashRegisterRules.LoadTotalsAsync` (`NetAmount`); kasa raporunun kapanış bakiyesiyle aynı. Paket istemez (kasa raporu da istemez). Klinikte kasa kaydı yoksa sanal Ana kasa (kasasız nakit) |
| `turnover` | `Payments.Read` **ve** `Charges.Read` | Bu dönem nasıl geçti? | Gün sonu dökümünün `totals.netChargeAmount`'u (`IDailyTransactionsReader`), performans özetiyle aynı. Seri: her noktanın penceresi için aynı toplam |
| `collections` | `Payments.Read` | Para hangi yolla geldi? | Kasa özeti, dönem penceresi: `totals.netAmount`, `collectedCount`, `byMethod[].netAmount`. Yüzde istemcide türetilir |
| `outstanding` | `Charges.Read` | Kimin bize borcu var? | Borç raporu (`IOutstandingBalancesReportReader`): toplam ve müşteri sayısı; `maxClientOutstandingAmount` = en yüksek borçlu müşterinin tutarı (raporun ilk satırı). Borç yoksa `0` |
| `appointments` | `Appointments.Read` | Randevular boşa gidiyor mu? | Randevu raporunun durum dağılımı (`IAppointmentsReportStatusBreakdownReader`, `ScheduledAtUtc` pencerede). `totalCount` = dört durumun toplamı (raporun `totalCount`'uyla aynı), `noShowCount` = `statusCounts.noShow`. Seri: nokta başına tüm durumlar |
| `examinations` | `Examinations.Read` | Klinikte ne kadar iş yaptık? | Muayene raporunun sayımı (`ExaminationsReportFilteredCountSpec`, `ExaminedAtUtc` pencerede). Seri aynı kuralla |
| `vaccinations` | `Vaccinations.Read` | aynı | `appliedCount`: aşı raporunun `Applied` sayımı (`AppliedAtUtc` pencerede). `upcomingCount`: önümüzdeki 7 gün planlı (`VaccinationDueRule.IsUpcoming`, Özet'teki "yaklaşan aşılar" ile aynı) |
| `attention.hbsNotEnteredPrescriptions` | `Prescriptions.Read` | HBS'e girilmemiş kayıt kaldı mı? + bant | HBS kuralı (`HbsTracking`), **Bugün'deki sayıyla aynı pencere**: son 30 İstanbul günü (`OpenItemsDefaults.HbsLookbackDays`). `oldestAtUtc` = penceredeki en eski reçete tarihi; sayı 0 ise `null` |
| `attention.hbsNotEnteredVaccinations` | `Vaccinations.Read` | aynı | Aynı kural ve pencere; tarih `AppliedAtUtc` |
| `attention.endOfDayUnchargedVisitCount` | `Visits.Read` **ve** `Charges.Read` | Bugünün kasası + bant | Gün sonu açık işlerinin "Ücret yok" sayısı, bugünün penceresi (`IVisitOpenItemsReader`, `GET /reports/end-of-day/open-items` ile aynı) |
| `attention.overdueVaccinationCount` | `Vaccinations.Read` | bant | `VaccinationDueRule.IsOverdue` (vadesi son 1 takvim ayı içinde geçmiş; daha eskisi sayılmaz); vefat eden hayvanlar hariç (Özet uyarısıyla aynı spec) |
| `attention.lowStockProductCount` | `Products.Read` | bant | `ProductStockMinimumFilter` (Stok durumu "Düşük stok" süzgeciyle aynı): elde − süresi geçmiş parti < asgari stok |
| `attention.overdueAppointmentCount` | `Visits.Read` **ve** `Appointments.Read` | bant | Bugün'ün `openItems.overdueAppointmentCount`'u (§7, §11): aynı okuyucu (`IVisitOpenItemsReader.GetSummaryAsync`), aynı pencere (`OpenItemsSummaryRequest.ForToday`), Bugün'deki çiple aynı izin |

- "Denetim gelse defterler hazır mı?" kartı sayı istemez (sabit "6 yasal defter"); BE alanı yoktur.
- Uyarı bandındaki bağlantılar (FE): HBS → `registers/hbs-prescriptions` ve `registers/hbs-vaccinations`; gün sonu → `end-of-day`; gecikmiş randevu → Randevu takvimi, Durum: Gecikmiş; geciken aşı → `/panel/vaccinations?overdue=true`; düşük stok → Stok durumu, düşük stok süzgeci. Sayısı 0 olan uyarı çizilmez (K4).

## 5) Hatalar

| Kod | HTTP | Durum |
|---|---|---|
| `ReportsHub.PeriodInvalid` | 400 | `period` yok ya da tanımsız |
| `ReportsHub.ClinicScopeRequired` | 400 | Ne `clinicId` ne aktif klinik bağlamı var |
| `ReportsHub.ClinicContextMismatch` | 400 | `clinicId` aktif klinik bağlamından farklı |
| `Clinics.AccessDenied` | 403 | Kullanıcı kliniğe atanmamış |
| `Clinics.NotFound` | 404 | Klinik kiracıda yok |
| `CashSummary.TooManyRows` | 400 | Dönemde 50.000'den fazla tahsilat olayı (kasa özetiyle aynı sınır) |
| `Tenants.ContextMissing` | 400 | Kiracı yok |

## 6) Güvenlik ve performans

- Her sorgu `TenantId` + tek `ClinicId` ile süzülür. İzinsiz bölüm için sorgu **çalışmaz**.
- Seri noktası, raporun kendi okuyucusu ya da sayım spec'iyle nokta başına bir sorgudur (en çok 7 nokta); kural kopyalanmaz. Tam yetkili kullanıcıda bir istek ~40 küçük `COUNT`/`SUM` sorgusu çalıştırır.
- Önbellek yok. Yeni indeks ve migration yok. Gerçek veri hacminde ölçülmedi (bilinen eksik).

## 7) Bugün: zamanı geçmiş randevular (K5)

`GET /api/v1/visits/today` yanıtındaki `openItems` nesnesine bir alan eklenir (`OPEN_ITEMS_API_CONTRACT.md` §4'e işlenecek):

| Alan | Tip | Tanım | İzin |
|---|---|---|---|
| `overdueAppointmentCount` | int? | Önceki günlerden kalan, hâlâ planlı randevu sayısı | `Appointments.Read` |

- **Tanım:** `Status = Scheduled`, `ScheduledAtUtc` ∈ `[bugün başı − 30 gün, bugün başı)`, randevuya bağlı iptal edilmemiş geliş (Visit) yok, hayvan vefat etmemiş.
- **Neden gün bazlı:** Takvimdeki "Gecikmiş" durumu (`CalendarItemStateRule`) gün bazlıdır; bugünün saati geçmiş randevusu zaten Bugün listesinde görünür. Bu sayı yalnız listede görünmeyen eski randevuları yakalar; Özet'teki saat bazlı sayı (bugünküler dahil) Bugün'de çift sayım olurdu.
- **Neden geliş hariç:** Gelişi açılmış randevu "Önceki günden açık geliş" (`incompleteVisitCount`) olarak zaten sayılır.
- **Neden 30 gün:** Diğer açık iş sayılarıyla aynı pencere (`OpenItemsDefaults`); sınırsız sayım yıllar önce unutulmuş randevuları da toplar.
- Tarih ve hekim süzgecinden etkilenmez (diğer `openItems` alanları gibi). Panel bağlantısı: Randevu takvimi, Durum: Gecikmiş.
- Hesap tek yerde: `IVisitOpenItemsReader.GetSummaryAsync`; pencere `OpenItemsSummaryRequest.ForToday` (`OpenItemsDefaults.OverdueAppointmentLookbackDays`). Gün sonu raporunda `null`.
- **B-02 (2026-11-23):** Sayı Rapor Merkezi bandına da eklendi (`attention.overdueAppointmentCount`, §11); panel Bugün'de göstermez. Bugün alanı geriye uyum için kalır.

### 7.1 Bugün: iptal edilen randevular (§10 madde 1)

`TodayDto` yanıtına alan eklenir:

| Alan | Tip | Tanım | İzin |
|---|---|---|---|
| `cancelledAppointmentCount` | int? | Seçili günde (`localDate`) planlanmış ve iptal edilmiş randevu sayısı | `Appointments.Read` |

- Randevu raporunun iptal sayımıyla aynı (`AppointmentsReportFilteredCountSpec`, `status = Cancelled`, `ScheduledAtUtc` gün içinde); panel bağlantısı o günün Randevu raporu, durum İptal.
- Hekim süzgecinden etkilenmez (bağlandığı rapor hekime göre süzmez). `voided=true` görünümünde `null`.

## 8) Kabul (testler)

Entegrasyon testi `ReportHubSummaryIntegrationTests` (LocalDB, `CustomWebApplicationFactory`):

- `turnover.netChargeAmount` = aynı pencerenin `daily-transactions` `totals.netChargeAmount`'u; `collections` = `cash-summary`; `outstanding` = `outstanding-balances` toplamı; HBS sayıları = Bugün'ün `openItems` HBS sayıları.
- İzni olmayan bölüm `null`; izinsiz kullanıcı 200 ve tüm bölümler `null`.
- Başka kiracının ya da atanmamış kliniğin `clinicId`'si 403/404; klinik bağlamı yoksa 400.
- Seri nokta sayısı ve sınırları §2.1'e uyar; boş günler 0.

Birim testleri: `ReportHubPeriod` (ay sonu kırpma, hafta başı Pazartesi, Pazar günü, ayın 1'i), `overdueAppointmentCount` tanımı (bugünkü randevu sayılmaz, gelişi olan sayılmaz, 31 gün önceki sayılmaz).

## 9) Frontend etkisi

- Yeni model ve servis `features/reports/hub`; B ana ekranı bu tek uçtan beslenir.
- Bugün "Açık işler" satırına "Gecikmiş randevu: N" çipi. **B-02 ile geri alındı:** gecikmiş randevu ve HBS çipleri Bugün'den kalkar; bant `attention.overdueAppointmentCount` ve `attention.hbsNotEntered*` alanlarını okur (§11).
- Mevcut uçlar değişmez. `/dashboard/*` uçları Özet kaldırılana kadar aynen çalışır.

## 10) Açık noktalar (kararlar, 2026-11-20: "açık noktaları da önerine göre yap")

1. **Bugün iptal edilen randevular:** Bugün listesi iptal edilen randevuyu göstermiyordu (`VisitTodayReader`, `Status != Cancelled`). ADR-052 "yoksa Bugün'e" diyor. Karar: gün listesi başlığında "N randevu iptal edildi" sayacı (§7.1).
2. **Aşı eğilim serisi:** Taslakta "Klinikte ne kadar iş yaptık?" kartında tek çizgi var (muayene). Karar: aşı serisi eklenmez; gerekirse sonradan eklenir.

## 11) Birikmiş işler Bugün'den Rapor Merkezi'ne (B-02, 2026-11-23)

Kullanıcı kararı (seçenek A): Bugün'ün açık iş çiplerinden birikmiş olanlar ("HBS'e girilmedi" reçete ve aşı, "Gecikmiş randevu") Bugün'den çıkar, "Bakmanız gerekenler" bandında durur. ADR-052 K5'teki "zamanı geçmiş randevular Bugün'e" maddesini geri çevirir.

- **Yeni alan:** `attention.overdueAppointmentCount` (int?). Tanım §7 ile aynı; hesap yine tek yerde (`IVisitOpenItemsReader.GetSummaryAsync`, pencere `OpenItemsSummaryRequest.ForToday`). Gün sonu "Ücret yok" sayısıyla tek okumada gelir: ücret yok gün sonu penceresiyle, gecikmiş randevu Bugün penceresiyle.
- **İzin:** Bugün'deki çiple aynı: `Visits.Read` (Bugün ucunun politikası) **ve** `Appointments.Read`. Biri yoksa `null`. Sayı 0 ise bant çizmez (K4).
- **HBS:** `attention.hbsNotEnteredPrescriptions` ve `attention.hbsNotEnteredVaccinations` zaten vardı (§4); değişmedi. Sayı Bugün'deki `hbsNotEntered*Count` ile aynı pencere ve kural (§8 testi). İzin: `Prescriptions.Read` / `Vaccinations.Read`; Bugün çipi ayrıca `Visits.Read` ister. Tüm sistem rollerinde (`RolePermissionBindings`) bu izinler `Visits.Read` ile birlikte verildiği için görünürlük fiilen aynıdır.
- **Bugün ucu değişmez:** `GET /visits/today` yanıtındaki `openItems.overdueAppointmentCount`, `hbsNotEnteredPrescriptionCount` ve `hbsNotEnteredVaccinationCount` kalır (geriye uyum: yayındaki eski panel bir süre aynı ucu çağırabilir). Yeni panel bu alanları Bugün'de göstermez. Alanların kaldırılması, eski panel sürümü yayından çıktıktan sonra ayrı iştir.
- **Testler:** birim `ReportHubWorkLoaderAttentionTests` (pencere, izin, okuyucu çağrılmaması); entegrasyon `TodayOpenItemsIntegrationTests.Report_Hub_Overdue_Appointments_Should_Equal_Today_And_Follow_Its_Permissions` ve §8 testinde bant sayısı = Bugün sayısı.
