# İşletme performansı özeti — API sözleşmesi (REPORT-008 dilim 1)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `ux/ISLETME-PERFORMANS-PANOSU-2026-10.md` (dilim 1), backlog REPORT-008, ADR-003 (Rapor Merkezi). Hata zarfı standart `ProblemDetails` + `extensions.code` (`BACKEND-CONTRACT-STANDARD.md` §9).

> **Durum:** uygulandı, dal `feat/isletme-performansi`. Migration yok, yeni izin yok, yeni hesap kuralı yok.

## 1) Uç

`GET /api/v1/reports/performance-summary?from=2026-10-01&to=2026-10-31&clinicId=…&compare=true`

| Parametre | Tür | Not |
|---|---|---|
| `from`, `to` | `yyyy-MM-dd` | İstanbul takvim günleri, ikisi dahil; en çok 366 gün (gün sonu raporlarıyla aynı süzgeç, `DailyTransactionsReportFilter`) |
| `clinicId` | GUID, opsiyonel | Boşsa aktif klinik bağlamı; o da yoksa kullanıcının erişebildiği klinikler (Admin/Owner için kiracı geneli). Kural `IClinicReadScopeResolver` |
| `compare` | bool, varsayılan `true` | `true` ise aynı uzunlukta, hemen önceki dönem de döner (bu ay 1–31 ↔ önceki 31 gün; son 30 gün ↔ önceki 30 gün) |

Kimlik: personel JWT, `[Authorize]`. Uç tek izne bağlı değildir: her bölüm, aynı veriyi gösteren raporun izniyle dolar; izin yoksa `null` döner. Böylece pano yeni veri açmaz.

| Bölüm | İzin | Kaynak (hesap tek yerde) |
|---|---|---|
| `charges` | `Payments.Read` **ve** `Charges.Read` | Gün sonu dökümünün `totals` alanı (`IDailyTransactionsReader`, §DAILY_TRANSACTIONS) |
| `cash` | `Payments.Read` | Kasa özeti (`CashSummaryLoader`): `totals`, `byMethod`, `byDay`, `byUser` (iptal edilen ödeme listesi taşınmaz) |
| `examinationCount` | `Examinations.Read` | Muayene raporunun sayımı (`ExaminationsReportFilteredCountSpec`, muayene tarihi aralıkta) |
| `vaccinations` | `Vaccinations.Read` | Aşı raporunun sayımı: `applied` uygulama tarihi, `scheduled` vade tarihi aralıkta |
| `appointments` | `Appointments.Read` | Randevu raporunun durum dağılımı (`AppointmentStatusCounts`) |
| `outstanding` | `Charges.Read` | Borç raporunun toplamı; **bugünkü** açık alacak, tarih aralığından bağımsız, karşılaştırmada yer almaz |

## 2) Yanıt (200)

```json
{
  "currency": "TRY",
  "current": {
    "from": "2026-10-01", "to": "2026-10-31",
    "charges": { "grossChargeAmount": 0, "grossChargeCount": 0, "salesChargeAmount": 0, "voidedChargeAmount": 0, "voidedChargeCount": 0,
                 "netChargeAmount": 0, "collectedAmount": 0, "collectedCount": 0, "salesCollectedAmount": 0,
                 "voidedCollectionAmount": 0, "voidedCollectionCount": 0, "netCollectedAmount": 0, "correctionCount": 0 },
    "cash": { "totals": { "collectedAmount": 0, "collectedCount": 0, "voidedAmount": 0, "voidedCount": 0, "netAmount": 0, "salesCollectedAmount": 0 },
              "byMethod": [ { "paymentMethod": "Cash", "...": "…" } ],
              "byDay": [ { "date": "2026-10-02", "netAmount": 0, "cashNetAmount": 0, "cardNetAmount": 0, "transferNetAmount": 0, "...": "…" } ],
              "byUser": [ { "userId": "…", "userName": "…", "netAmount": 0, "...": "…" } ] },
    "examinationCount": 0,
    "vaccinations": { "applied": 0, "scheduled": 0 },
    "appointments": { "scheduled": 0, "completed": 0, "cancelled": 0, "noShow": 0 }
  },
  "previous": { "from": "2026-08-31", "to": "2026-09-30", "...": "current ile aynı şekil" },
  "outstanding": { "totalOutstandingAmount": 0, "clientCount": 0 }
}
```

- Alan şekilleri ilgili raporlarla aynıdır (`DailyTransactionTotalsDto`, `CashSummary*Dto`, `AppointmentReportStatusCountsDto`); istemci aynı modeli kullanabilir.
- `cash.byDay` yalnız hareketli günleri içerir; boş günleri istemci 0 ile doldurur.
- Ciro = `charges.netChargeAmount`, net tahsilat = `cash.totals.netAmount`. "Tahsilat / ciro" oranı istemcide türetilir (önceki dönem ücretinin tahsilatı da tahsilata girer).
- `previous`: `compare=false` ise `null`.

## 3) Hatalar

Gün sonu raporlarıyla aynı kodlar:

| Kod | HTTP | Durum |
|---|---|---|
| `Registers.DateRangeInvalid` | 400 | `from`/`to` yok ya da `from > to` |
| `Registers.RangeTooLong` | 400 | 366 günden uzun |
| `Registers.ClinicContextMismatch` | 400 | `clinicId` aktif klinik bağlamından farklı |
| `Clinics.AccessDenied` | 403 | Kullanıcı kliniğe atanmamış |
| `Clinics.NotFound` | 404 | Klinik kiracıda yok |
| `CashSummary.TooManyRows` | 400 | Dönemde 50.000'den fazla tahsilat olayı (kasa özetiyle aynı sınır) |
| `Tenants.ContextMissing` | 400 | Kiracı yok |

## 4) Kabul (entegrasyon testi `PerformanceSummaryIntegrationTests`)

- `charges` = aynı süzgeçle `daily-transactions` `totals`; `cash.totals`/`byUser` = `cash-summary`; `outstanding` = `outstanding-balances` toplamı.
- İzni olmayan bölüm `null`.
- Önceki dönem aynı uzunlukta; aralık hataları gün sonu raporlarıyla aynı.
