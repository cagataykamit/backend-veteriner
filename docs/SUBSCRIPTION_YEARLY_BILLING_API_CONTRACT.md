# Yıllık abonelik ödemesi — API sözleşmesi (BILL-001)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `BILL-001`, ADR-035 (paketler), ADR-037 ve kullanıcı kararı 2026-10-22 "B" (yıllık fiyat = 10 aylık tutar, "2 ay hediye"). İlgili: `SALES_PARTNER_DISCOUNT_API_CONTRACT.md` (kod indirimi ve komisyon). Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feat/bill-001-yearly`. **Migration var, uygulanmadı** (`AddBillingPeriod`: `TenantSubscriptions.BillingPeriod`, `BillingCheckoutSessions.BillingPeriod`, `int`, varsayılan `1` = aylık; mevcut satırlar aylık kalır). Yeni izin yok.

## 1. Kurallar (kullanıcı kararları 2026-10-26)

| Konu | Kural |
|---|---|
| Fiyat | Yıllık = aylık plan fiyatı × 10 (`SubscriptionPricing.PeriodPriceMinor`). Satış ortağı kodu indirimi bunun üzerine uygulanır; komisyon yıllıkta tek seferlik (ADR-037). |
| Dönem | Yıllık dönem ödeme anından itibaren 1 yıl (`ActivatedAtUtc + 1 yıl`). Otomatik yenileme yok: dönem bitince abonelik salt okunura düşer, kiracı yeniden ödeme başlatır (aylıktaki gibi). |
| Aylıktan yıllığa | Etkin aylık dönemde **hemen** geçilir: çekilen = yıllık fiyat − mevcut aylık dönemin kullanılmamış kısmı (aylık fiyat × kalan oran). Yeni yıllık dönem ödeme anında başlar. Aynı anda plan yükseltmesi de seçilebilir (hedef planın yıllık fiyatı). |
| Yıllıktan aylığa | Etkin yıllık dönemde **yapılamaz** (`409`/`400`, aşağıda); dönem bitince yenilemede aylık seçilir. |
| Yıllık dönemde yükseltme | Mevcut yıllık dönem içinde, yıllık fiyat farkı × kalan oran çekilir; dönem başlangıcı değişmez. Düşürme aylıktaki gibi dönem sonuna planlanır. |
| Deneme | Deneme sırasında plan değişimi ücretsizdir (Model A); dönem seçimi ilk ücretli ödemede yapılır. |
| Sağlayıcı | iyzico (tek seferlik Checkout Form, hesaplanan tutar çekilir) ve Manual. **Stripe yıllık desteklemez** (tekrarlayan fiyat kimliği aylık). |

## 2. Uçlar

### `POST /api/v1/tenants/{tenantId}/subscription-checkout` (değişti)

İstek gövdesine isteğe bağlı alan:

```json
{ "targetPlanCode": "Pro", "billingPeriod": "Yearly" }
```

- `billingPeriod`: `"Monthly"` | `"Yearly"` (büyük/küçük harf duyarsız). Boş/yoksa: ücretli abonelikte **mevcut dönem**, hiç ödeme yapılmadıysa `Monthly`. Sayısal değer (`"1"`) kabul edilmez.
- Yanıt (`SubscriptionCheckoutSessionDto`) yeni alan: `billingPeriod` (`"Monthly"` | `"Yearly"`). `proratedChargeMinor` çekilecek net tutardır (dönem fiyatı, oranlama ve kod indirimi sonrası).
- Açık bir ödeme oturumu yalnız aynı hedef plan **ve aynı dönem** için yeniden kullanılır; dönem farklıysa eski oturum iptal edilip yenisi açılır.

Yeni hata kodları:

| HTTP | `code` | Ne zaman |
|---|---|---|
| 400 | `Subscriptions.Checkout.Validation.BillingPeriodInvalid` | `billingPeriod` tanınmayan değer. |
| 400 | `Subscriptions.PeriodChangeAtPeriodEnd` | Etkin yıllık dönemde `Monthly` istendi. |
| 400 | `Billing.YearlyNotSupportedByProvider` | Etkin sağlayıcı Stripe iken `Yearly` istendi. |

`Subscriptions.SamePlanAlreadyActive` artık yalnız **aynı plan ve aynı dönem** için döner; etkin aylık dönemde aynı plan + `Yearly` geçerli bir istektir.

### `GET /api/v1/tenants/{tenantId}/subscription-checkout/{checkoutSessionId}` (değişti)

Yanıtta `billingPeriod` (oturumun dönemi).

### `GET /api/v1/tenants/{tenantId}/subscription-summary` (değişti)

Yanıtta `billingPeriod` (`"Monthly"` | `"Yearly"`; hiç ödeme yoksa `"Monthly"`). `currentPeriodEndUtc` / `nextBillingAtUtc` yıllıkta dönem başlangıcı + 1 yıldır.

## 3. Testler

- Domain: `TenantSubscriptionBillingPeriodTests` (dönem sonu, aylıktan yıllığa yeniden başlatma, yıllıkta yükseltme anchor'ı, dönem bitince aylığa dönüş, oturum dönemi, 10 ay fiyatı).
- Application: `StartSubscriptionCheckoutCommandHandlerTests` (yıllık yenileme, dönem verilmeden yıllık yenileme, aylıktan yıllığa oranlama, yıllıkta yükseltme, yıllıktan aylığa red, Stripe red, dönem farkında oturum yeniden kullanılmaz), `TenantSubscriptionEffectiveWriteEvaluatorTests`, `TenantSubscriptionPeriodCalculatorTests`.
- Entegrasyon: `YearlyBillingIntegrationTests` (aylık ücretli kiracı → yıllık ödeme → sonuçlandırma → özet 1 yıllık dönem; geçersiz dönem 400).

## 4. Bilinen sınırlar

- Stripe akışı hesaplanan tutarı çekmiyor (tekrarlayan fiyat kimliği); bu BILL-001 öncesinden kalan bir durum, yıllıkta Stripe açıkça reddedilir.
- Yıllık dönemde kısmi iade/iptal yok (iade akışı yok; ADR-037 §0).
