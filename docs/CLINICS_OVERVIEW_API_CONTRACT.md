# Patron ekranı (şubeler özeti) API sözleşmesi — TIER-007

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-007` ("Paket: Kurumsal. Tüm şubelerin ciro, tahsilat, borç ve iş yükünü tek özette gösteren ekran"), `REPORT-008`, ADR-035. İlgili: `PERFORMANCE_SUMMARY_API_CONTRACT.md`, `PERFORMANCE_BREAKDOWNS_API_CONTRACT.md`, `DAILY_TRANSACTIONS_REPORT_API_CONTRACT.md`, `PLAN_FEATURES_API_CONTRACT.md`.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-007-patron-ekrani`. Migration yok, **yeni izin yok**, yeni hesap kuralı yok. §6'daki maddeler **çalışma varsayımıdır, kullanıcı onayı bekliyor.**

---

## 1) Kapsam

Paket: yalnız Kurumsal (`PlanFeature.ClinicsOverview`, `requiredPlanCode: "Premium"`). Denemede seçilen pakete göre davranır (`MultiClinic` istisnası buna uygulanmaz). Şube başına işletme performansı özeti ve gün sonu raporları her pakette aynen kalır.

Kullanıcının erişebildiği tüm **aktif** şubeler tek tabloda, şube başına ve toplam:

| Alan | Anlam | Kaynak (hesap tek yerde) | İzin |
|---|---|---|---|
| `netChargeAmount` | Ciro (net ücret) | Gün sonu dökümü `totals.netChargeAmount` (`IDailyTransactionsReader`) | `Payments.Read` **ve** `Charges.Read` |
| `netCollectedAmount` | Net tahsilat | Kasa özeti `totals.netAmount` (`CashSummaryLoader`) | `Payments.Read` |
| `outstanding` | Bugünkü açık alacak ve alacaklı müşteri; dönemden bağımsız, karşılaştırmada yok | Borç raporu (`IOutstandingBalancesReportReader`) | `Charges.Read` |
| `visitCount` | Geliş: iptal edilmemiş, geliş zamanı dönemde | Kırılımlardaki geliş (`IPerformanceBreakdownReader.ReadActivityAsync`) | `Visits.Read` |
| `examinationCount` | Muayene: muayene tarihi dönemde, taslak dahil | Muayene raporu sayımı | `Examinations.Read` |
| `appointments` | Randevu durum dağılımı (planlı, tamamlandı, iptal, gelmedi) | Randevu raporu okuyucusu | `Appointments.Read` |

İzni olmayan alan `null` döner; uç tek izne bağlı değildir (özetle aynı desen, yeni veri açılmaz). Şube satırı, o klinikle açılan `performance-summary` ile aynı yüklemeden geçer (`PerformancePeriodLoader`); paralel hesap yoktur.

## 2) Uç

`GET /api/v1/reports/clinics-overview?from=2026-10-01&to=2026-10-31&compare=true`

| Parametre | Tür | Not |
|---|---|---|
| `from`, `to` | `yyyy-MM-dd` | İstanbul takvim günleri, ikisi dahil; en çok 366 gün (`RegisterReportFilter.RangeError`, gün sonuyla aynı kural ve kodlar) |
| `compare` | bool, varsayılan `true` | Aynı uzunlukta hemen önceki dönem (`GetPerformanceSummaryQueryHandler.PreviousPeriod`) |

**Klinik filtresi yoktur.** Aktif klinik bağlamı (JWT `clinic_id`, `X-Clinic-Id`) listeyi daraltmaz; ekranın işi tüm şubelerdir. Sınır, raporlarla aynı çözümleyicidir (`IClinicReadScopeResolver`): Admin ve Owner kiracının tüm aktif klinikleri, diğer roller atandıkları aktif klinikler. Pasif klinik listelenmez. Sorguya `clinicId` eklenip JWT kliniğiyle çelişirse ara katman her uçtaki gibi 403 `Context.ClinicConflict` döner.

```json
{ "currency": "TRY",
  "from": "2026-10-01", "to": "2026-10-31",
  "previousFrom": "2026-08-31", "previousTo": "2026-09-30",
  "clinics": [
    { "clinicId": "…", "clinicName": "Merkez",
      "current": { "netChargeAmount": 48250.00, "netCollectedAmount": 45100.00, "visitCount": 212, "examinationCount": 180,
                   "appointments": { "scheduled": 12, "completed": 160, "cancelled": 9, "noShow": 4 } },
      "previous": { "...": "current ile aynı şekil" },
      "outstanding": { "totalOutstandingAmount": 6300.00, "clientCount": 21 } }
  ],
  "total": { "current": { "...": "…" }, "previous": { "...": "…" }, "outstanding": { "totalOutstandingAmount": 9100.00, "clientCount": 30 } } }
```

- `clinics` klinik adına göre sıralıdır. Tek klinikli kiracıda tek satır döner; `total` o satırın aynısıdır (panel toplam satırını gizler).
- `total` listelenen şubelerin birlikte okunmasıdır: tutar ve sayılar satırların toplamına eşittir; `outstanding.clientCount` tekildir, iki şubede borcu olan müşteri bir kez sayılır. Atanmış klinikleri olmayan kullanıcıda `clinics` boş, `total` sıfırdır.
- `previous`, `previousFrom`, `previousTo`: `compare=false` ise `null`.

## 3) Hatalar

| Kod | HTTP | Durum |
|---|---|---|
| `Subscriptions.FeatureNotInPlan` | 403 | Pakette `ClinicsOverview` yok (`requiredPlanCode: "Premium"`) |
| `Registers.DateRangeInvalid` | 400 | `from`/`to` yok ya da `from > to` |
| `Registers.RangeTooLong` | 400 | 366 günden uzun |
| `CashSummary.TooManyRows` | 400 | Bir şubede ya da toplamda dönemde 50.000'den fazla tahsilat olayı (kasa özetiyle aynı sınır) |
| `Tenants.ContextMissing` | 400 | Kiracı yok |

Sıra: kiracı, paket, tarih aralığı, klinik kapsamı.

## 4) Maliyet

Şube başına dönem başına özetle aynı sorgular (döküm toplamı, kasa olayları, muayene, iki aşı sayımı, randevu dağılımı) ve iki geliş sorgusu; karşılaştırma bunu iki katına çıkarır, açık alacak şube başına bir kez. Toplam satırı bir şube gibi bir kez daha okunur. Sorgular sırayla çalışır (tek `DbContext`). Şube sayısı sınırı yoktur; on şubenin üzerinde süre ölçülmedi.

## 5) Testler

- Application: `TenantPlanFeatureEvaluatorTests` (paket tablosu, en küçük paket Premium).
- Entegrasyon: `ClinicsOverviewIntegrationTests` (şube satırı = aynı klinikle `performance-summary`, `daily-transactions` toplamı ve kırılımlardaki geliş; toplam = erişilebilen şubelerle açılan özet, tekil alacaklı müşteri; atanmamış ve pasif şube listelenmez; aktif klinik bağlamı listeyi daraltmaz; Profesyonel'de 403; tek şubede tek satır ve toplam = satır; izne göre boş alanlar; 366 gün sınırı).

## 6) Çalışma varsayımları, kullanıcı onayı bekliyor

1. Yeni izin açılmadı. Uç bölüm bazlı mevcut okuma izinleriyle dolar; şube satırındaki her sayı aynı kullanıcının o kliniği seçip özette görebildiği sayıdır. `Reports.Performance` (kişi performansı) gerekmez; ekranda kişi yoktur. Panelde kart ve sayfa, işletme performansıyla aynı yönetici düzeyi koşuluyla görünür.
2. Pasif klinik listelenmez ve toplama girmez (yalnız aktif şubeler; geçmiş dönemde pasif kliniğin hareketi bu ekranda görünmez, şube özetinde görünür).
3. İş yükü geliş, muayene ve randevu dağılımıdır; aşı sayısı ekranda yoktur (özette kalır).
4. Yalnız Kurumsal; deneme seçilen pakete göre davranır.
