# Paket kataloğu ve liste fiyatı API sözleşmesi — WEB-007

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `WEB-007`, ADR-034 karar 4 (fiyat belirlenince paket başına aylık TL; kullanıcı sınırlarının tek doğrusu backend kataloğu), ADR-035 (paketler), fiyat kararı (kullanıcı/koordinatör, 2026-11-05, geçici). İlgili: `SUBSCRIPTION_YEARLY_BILLING_API_CONTRACT.md` (yıllık = 10 ay), `SALES_PARTNER_DISCOUNT_API_CONTRACT.md` (fiyat KDV hariç), `BACKEND-CONTRACT-STANDARD.md` §20–24 (abonelik özeti ve checkout).

> **Durum:** Uygulandı (kod + test), dal `feat/web-007-paketler`. Migration yok, yeni izin yok, **1 yeni anonim uç**; `subscription-summary` plan seçeneklerine iki alan eklendi (geriye uyumlu).

---

## 1) Kural: fiyat tek yerde

| Konu | Kural |
|---|---|
| Fiyatın yeri | Yapılandırma `Billing:PlanPricesMinor` (aylık, kuruş, **KDV hariç**). Değer `appsettings.json`'dadır; ortam değişkeniyle (`Billing__PlanPricesMinor__Pro`) ezilebilir. Yoksa ya da `0` ise `Billing:Iyzico:PlanPricesTry` (TL) kuruşa çevrilerek okunur; o da yoksa "fiyat yapılandırılmamış". |
| Okuyan kod | Yalnız `SubscriptionPlanPrices` (Application `Common/Billing`). Checkout tutarı (`StartSubscriptionCheckoutCommandHandler`, iyzico tutar verilmediğindeki geri dönüşü dahil), panel özeti ve bu uç aynı kuralı çağırır; sitede gösterilen fiyat ile çekilen tutar ayrışmaz. |
| Yıllık | `SubscriptionPricing.PeriodPriceMinor` = aylık × `SubscriptionPricing.YearlyBilledMonths` (10). İkinci bir yıllık sayı tutulmaz. |
| Kullanıcı sınırı | `SubscriptionPlanCatalog.MaxUsers` (Basic 1, Pro 5, Premium 15). |
| Klinik sınırı | Backend'de yok; uç döndürmez. |
| Ödemenin açılması | Fiyat ödemeyi **açmaz**. iyzico ancak anahtarlar ve `Billing:Iyzico:PlanPricesTry` tüm planlar için dolu olduğunda etkindir (`IyzicoCheckoutPrerequisites`); canlıda `PlanPricesTry` `0` kaldığı sürece ücretli checkout başlamaz (`Billing.IyzicoConfigurationIncomplete`, 503). |

Fiyat değişince yalnız `appsettings.json` `Billing:PlanPricesMinor` (ya da ortam değişkeni) değişir; site, panel ve checkout yeniden başlatmadan sonra yeni değeri okur.

Fiyat (2026-11-06, kullanıcı onayı; ilk geçici değerlerin yerine): Basic `89000` (890 TL), Pro `179000` (1.790 TL), Premium `379000` (3.790 TL) / ay, KDV hariç. Yıllık: 8.900 / 17.900 / 37.900 TL.

## 2) `GET /api/v1/public/subscription-plans` — anonim

Yetki yok (`AllowAnonymous`), hız sınırı yok (statik okuma; genel sınır geçerli). Her zaman **200**.

```json
{
  "currency": "TRY",
  "vatIncluded": false,
  "yearlyBilledMonths": 10,
  "plans": [
    { "planCode": "Basic", "name": "Basic", "maxUsers": 1, "monthlyPriceMinor": 89000, "yearlyPriceMinor": 890000 },
    { "planCode": "Pro", "name": "Pro", "maxUsers": 5, "monthlyPriceMinor": 179000, "yearlyPriceMinor": 1790000 },
    { "planCode": "Premium", "name": "Premium", "maxUsers": 15, "monthlyPriceMinor": 379000, "yearlyPriceMinor": 3790000 }
  ]
}
```

| Alan | Açıklama |
|---|---|
| `currency` | `Billing:PlanPriceCurrency` (boşsa `TRY`). |
| `vatIncluded` | Her zaman `false`: tutarlar KDV hariçtir, ödemeye KDV eklenmez. |
| `yearlyBilledMonths` | Yıllık ödemede ücretlendirilen ay sayısı (12 − bu = hediye ay). |
| `plans[]` | Katalog sırası (Basic, Pro, Premium). |
| `planCode` | API kodu (`owner-signup` `planCode`, checkout `targetPlanCode` ile aynı). |
| `name` | Katalog adı (`subscription-summary` `planName` ile aynı). Tanıtım sitesi görünen paket adını kendi metninden alır (Temel/Profesyonel/Kurumsal). |
| `maxUsers` | Kullanıcı sınırı. |
| `monthlyPriceMinor`, `yearlyPriceMinor` | Kuruş, KDV hariç, kod indirimi öncesi. Fiyat yapılandırılmamışsa ikisi de `null`; site bu durumda tutar yazmaz ("Fiyat için bize yazın"). |

## 3) `GET /api/v1/tenants/{tenantId}/subscription-summary` (değişti, geriye uyumlu)

`availablePlans[]` (`SubscriptionPlanOptionDto`) iki yeni alan: `monthlyPriceMinor`, `yearlyPriceMinor` (aynı kural, aynı anlam, yapılandırılmamışsa `null`). Panel abonelik kartı fiyatı buradan gösterir.

## 4) Davranış değişiklikleri

- Fiyatlar `appsettings.json`'a girdiği için her ortamda (canlı dahil) fiyat yapılandırılmış sayılır. Canlıda iyzico `PlanPricesTry` `0` olduğundan süresi dolmuş dönemde yenileme/yükseltme isteği artık `Billing.PlanPriceNotConfigured` yerine sağlayıcı aşamasında `Billing.IyzicoConfigurationIncomplete` (503) ile reddedilir; ödeme yine başlamaz.
- `PlanPricesMinor` değeri `0` olan plan artık "yapılandırılmamış" sayılır (önce `0` okunup dönem hesabında hata veriyordu).
- iyzico sağlayıcısı tutar verilmeyen nadir durumda `PlanPricesTry` yerine aynı kuralı (`PlanPricesMinor` öncelikli) kullanır.

## 5) Testler

- Application: `SubscriptionPlanPricesTests` (öncelik, TL→kuruş, sıfır/eksik, yıllık kural, para birimi), `GetPublicSubscriptionPlansQueryHandlerTests` (katalog sınırları ve fiyatlar, fiyatsız durum), `GetTenantSubscriptionSummaryQueryHandlerTests.Handle_Should_Return_PlanPrices_From_Billing_Configuration`.
- Entegrasyon: `PublicSubscriptionPlansIntegrationTests` (anonim uç katalog ve yapılandırmayla aynı; süresi dolmuş dönemde aylık ve yıllık yenilemede checkout tutarı = sitedeki fiyat = panel özetindeki fiyat).

## 6) Bilinen sınırlar

- Stripe akışı kendi fiyat kimliğinin tutarını çeker (BILL-001 öncesinden); Stripe açılırsa fiyat orada ayrıca eşlenmelidir.
- Plan adı backend'de İngilizce kod adıdır (Basic/Pro/Premium); site ve panel Türkçe adları kendi metninden gösterir.
