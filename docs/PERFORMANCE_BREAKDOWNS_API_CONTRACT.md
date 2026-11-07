# Gelişmiş raporlar (işletme performansı kırılımları) API sözleşmesi — TIER-004

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-004`, `REPORT-008` (dilim 2–3), `ux/ISLETME-PERFORMANS-PANOSU-2026-10.md` (H2, H3, K5, K7; S1 kullanıcı kararı). İlgili: `PERFORMANCE_SUMMARY_API_CONTRACT.md`, `DAILY_TRANSACTIONS_REPORT_API_CONTRACT.md`, `PLAN_FEATURES_API_CONTRACT.md`.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-004-gelismis-raporlar`. Migration yok. **Yeni izin** `Reports.Performance` (Admin, Owner, ClinicAdmin). §6'daki maddeler **çalışma varsayımıdır, kullanıcı onayı bekliyor.**

---

## 1) Kapsam

Paket: Profesyonel ve Kurumsal (`PlanFeature.AdvancedReports`). Bugünkü raporlar, yasal kayıt defterleri ve işletme performansı özeti (`performance-summary`, önceki dönemle) her pakette aynen kalır.

| Bölüm | İçerik | İzin |
|---|---|---|
| `byVeterinarian` | Hekime göre muayene sayısı ve net ciro (H2, H3) | `Reports.Performance`; sayı `Examinations.Read`, ciro `Payments.Read` + `Charges.Read` |
| `byLineKind` | Hizmet / ürün net cirosu (`Charge.LineKind`) | `Payments.Read` + `Charges.Read` |
| `topItems` | Net ciroya göre ilk 10 kalem; ücret açıklamasına göre | `Payments.Read` + `Charges.Read` |
| `bySpecies` | Hayvan türüne göre muayene sayısı ve net ciro | sayı `Examinations.Read`, ciro `Payments.Read` + `Charges.Read` |
| `activity` | Geliş sayısı (K5) ve gelen tekil hasta sahibi (K7, gelişe göre) | `Visits.Read` |

İzni olmayan bölüm ya da alan `null` döner (özetle aynı desen).

## 2) Uç

`GET /api/v1/reports/performance-breakdowns?from=2026-10-01&to=2026-10-31&clinicId=…&compare=true`

Parametreler, klinik kapsamı, 366 gün sınırı ve önceki dönem kuralı `performance-summary` ile aynıdır (`DailyTransactionsReportFilter`, `GetPerformanceSummaryQueryHandler.PreviousPeriod`).

```json
{ "currency": "TRY",
  "current": {
    "from": "2026-10-01", "to": "2026-10-31",
    "byVeterinarian": [ { "veterinarianUserId": "…", "name": "Dr. Ayşe", "examinationCount": 42, "netChargeAmount": 18500.00 },
                        { "veterinarianUserId": null, "name": "Atanmamış", "examinationCount": 0, "netChargeAmount": 2400.00 } ],
    "byLineKind": [ { "lineKind": "Service", "netChargeAmount": 15000.00, "chargeCount": 60 }, { "lineKind": "Product", "netChargeAmount": 5900.00, "chargeCount": 31 } ],
    "topItems": [ { "description": "Genel muayene", "lineKind": "Service", "netChargeAmount": 6000.00, "chargeCount": 20 } ],
    "bySpecies": [ { "speciesId": "…", "name": "Kedi", "examinationCount": 30, "netChargeAmount": 12000.00 },
                   { "speciesId": null, "name": "Hayvan yok", "examinationCount": 0, "netChargeAmount": 900.00 } ],
    "activity": { "visitCount": 55, "clientCount": 38 } },
  "previous": { "...": "current ile aynı şekil" } }
```

Sıralama: hekim ve kalem net ciroya göre azalan; tür muayene sayısına göre azalan. `previous`: `compare=false` ise `null`.

## 3) Hesap kuralları

| Konu | Kural |
|---|---|
| Net ciro | Gün sonu dökümünün mali olay kaynağı (`FinanceEvents`): dönemde oluşan ücret + , dönemde iptal edilen ücret −. Bu yüzden her kırılımın toplamı `daily-transactions` `totals.netChargeAmount`'a eşittir (entegrasyon testiyle sabit) |
| Hekim ataması | Ücret: bağlı ziyaretin sorumlu hekimi; ziyaret yoksa bağlı muayenenin kuralı; ikisi de yoksa **atanmamış** (muayenesiz satış dahil). Muayene: ziyaretin sorumlu hekimi; ziyaretsiz muayenede kesinleştiren kullanıcı; kesinleşmemişse atanmamış. Veri modeli değişmedi |
| Muayene sayısı | Muayene raporuyla aynı: muayene tarihi aralıkta, durumdan bağımsız |
| Kalem | Ücret satırı ürün/hizmet kimliği taşımaz; gruplama açıklama metni ve satır türüyle yapılır. Aynı hizmet farklı yazılmışsa ayrı satır olur |
| Tür | Ücretteki ya da muayenedeki hayvanın türü; hayvansız ücret "Hayvan yok" |
| Geliş | İptal edilmemiş, geliş zamanı aralıkta; tekil hasta sahibi bu gelişlerin hayvanlarının sahipleridir |

## 4) Hatalar

`performance-summary` ile aynı kodlar, ek olarak:

| Kod | HTTP | Durum |
|---|---|---|
| `Subscriptions.FeatureNotInPlan` | 403 | Pakette `AdvancedReports` yok (`requiredPlanCode: "Pro"`) |

## 5) Veri modelinin desteklemediği, alınmayanlar

- **Ürün bazlı satış:** ücret satırında ürün kimliği yok; yalnız muayenesiz satış satırında (`SaleLine.ProductId`) var. Kısmi ürün listesi yanıltacağı için alınmadı; kalem kırılımı açıklamaya göre.
- **Yeni müşteri (K6):** müşteri kaydında içe aktarma işareti yok; içe aktarılanlar ayrılamadığı için alınmadı.
- **Geçen yılın aynı dönemi:** bu dilimde yok; önceki dönem karşılaştırması var.
- **Hekim filtresi:** yok; hekim kırılımı tablo olarak döner.

## 6) Çalışma varsayımları, kullanıcı onayı bekliyor

1. Bugünkü özetteki önceki dönem karşılaştırması Temel'de kalır; Profesyonel'in farkı kırılımlardır.
2. Hekim ataması §3'teki sıra; atanmamış satır gösterilir, gizlenmez.
3. Kalem listesi ilk 10.
4. `Reports.Performance` yalnız Admin, Owner ve ClinicAdmin'de (UX taslağı S1 kararı). Hekimin kendi satırını görmesi yok.

## 7) Testler

- Application: `TenantPlanFeatureEvaluatorTests` (paket tablosu), `ReportsPerformancePermissionBindingsTests` (izin rolleri).
- Entegrasyon: `PerformanceBreakdownsIntegrationTests` (Temel'de kilit, özet açık; kırılım toplamı = gün sonu net cirosu; hekim ataması; izne göre boş bölümler; önceki dönem).
