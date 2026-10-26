# Satış ortağı ve indirim kodu — API sözleşmesi (ADR-037)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-037-satis-ortagi-indirim-kodu.md`; bağlam ADR-035 (paketler, yalnız aylık fiyat). Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feat/satis-ortagi-indirim-kodu`. **Migration var, uygulanmadı** (`AddSalesPartnerDiscountCodes`). **Yeni izin yok** (platform uçları `platform_admin` claim'iyle korunur), seed gerekmez.

---

## 0. Keşif (kod yazılmadan önce, 2026-10-22)

Mevcut abonelik/ödeme akışı koddan okundu; tasarım bu bulgulara göre en küçük hâliyle seçildi.

| Konu | Koddaki gerçek durum |
|---|---|
| Canlı kart çekimi | `IyzicoBillingCheckoutProvider` iyzico **Checkout Form** ile **tek seferlik** ödeme başlatır (`PaymentGroup.PRODUCT`, tek taksit). Kart saklanmaz, **otomatik yenileme/tekrarlayan çekim yok**. Üretim yapılandırması `DefaultCheckoutProvider = Iyzico`; geliştirme/test `Manual` (gerçek para yok). |
| Tahsilat olayı | Ödeme sonucu **imzası doğrulanan webhook** ile gelir (`ProcessBillingWebhookCommandHandler` → `PaymentSucceeded` → `SubscriptionCheckoutActivationService.TryActivateAsync(source: Webhook)`). iyzico callback yalnız yönlendirme köprüsüdür. Panel `finalize` ucu (`source: Manual`) ödemeyi sağlayıcıdan doğrulamaz; bu yüzden yalnız `Manual` (geliştirme/test) sağlayıcı oturumunu etkinleştirir, iyzico/Stripe oturumunda **409** `Billing.ManualFinalizeNotAllowed` döner (BILL-002). |
| Tahsilat tutarı | `BillingCheckoutSession` tutar **saklamıyordu**; tutar checkout başlarken hesaplanıp sağlayıcıya geçiliyordu. |
| Plan fiyatı | Kodda değil, yapılandırmada: `Billing:PlanPricesMinor` veya `Billing:Iyzico:PlanPricesTry`. Ödeme tutarına **KDV eklenmez**; yapılandırılan fiyat olduğu gibi çekilir. |
| Dönem | Yalnız **aylık**: `ActivatedAtUtc` + 1 ay (`TenantSubscriptionPeriodCalculator`). Yıllık dönem kavramı yoktu (BILL-001 ile eklendi: `SUBSCRIPTION_YEARLY_BILLING_API_CONTRACT.md`). Süre dolunca abonelik salt okunura düşer; kiracı yeniden checkout ile öder (aylık manuel yenileme). |
| Deneme → ücretli | 14 gün deneme (`StartTrial`), kart yok. Deneme bitince kiracı checkout başlatır; ödeme webhook'u gelince `ActivatePaidPlan`. Deneme sırasında plan değişimi ücretsiz (Model A). |
| İade / iptal | **Yok.** Webhook ayrıştırıcı yalnız `PaymentSucceeded/PaymentFailed` üretir; iade olayı, abonelik iptal komutu ya da iade API çağrısı bulunmuyor. |
| Platform yöneticisi | `PlatformAdmin` OperationClaim'i olan kullanıcının JWT'sinde `platform_admin = True` claim'i var (login/refresh/select-clinic). Bu claim'le korunan **hiçbir uç yoktu**; `PlatformAdmin` claim'i tüm izinlere bağlı olduğundan izin tabanlı kontrol klinik yöneticisini ayırmaz. |
| Kiracılar arası okuma | `AppDbContext`'te kiracı sorgu filtresi yok; kiracı kapsamı el ile (handler) uygulanıyor. |

**Seçilen tasarım (ADR-037'den sapmalar ve kararlar):**

1. **İndirim checkout'ta uygulanır:** `StartSubscriptionCheckout` brüt tutarı (yenileme fiyatı ya da yükseltme farkı) hesapladıktan sonra kiracının kod kullanımı penceredeyse indirimi düşer; sağlayıcıya **net** tutar gider. Brüt ve indirim checkout oturumuna yazılır (`GrossAmountMinor`, `DiscountAmountMinor`).
2. **Komisyon tahsilat olayında işlenir:** checkout oturumu **webhook ile** tamamlandığında (`BillingActivationSource.Webhook`) kullanımına bir tahsilat satırı yazılır. Panel `finalize` (doğrulanmamış) ve `Manual` sağlayıcı komisyon üretmez.
3. **12 ay penceresi ilk gerçek tahsilatla başlar** (deneme süresi sayılmaz): pencere = ilk tahsilat anı + 12 ay. Aylık: fiyatlandırma anı pencere içindeyse indirim/komisyon uygulanır (aylık yenilemede bu en çok 12 tahsilat demektir; pencere içindeki plan yükseltme fark tahsilatları da indirimlidir). Yıllık: yalnız etkin (iptal edilmemiş) tahsilatı olmayan kullanımın ilk tahsilatı indirimlidir. Yıllık ödeme ürün kararıdır (kullanıcı, 2026-10-22 "B": yıllık fiyat = 10 ay fiyatı, tek tahsilat; kod indirimi yıllık indirimin üzerine kalan tutara uygulanır, komisyon tüm indirimler sonrası net üzerinden ve tek seferlik) ve BILL-001 ile uygulandı: checkout `billingPeriod` alır, oturum dönemi saklanır ve indirim/komisyon oturumun dönemiyle hesaplanır (`SUBSCRIPTION_YEARLY_BILLING_API_CONTRACT.md`).
4. **Uygunluk fiyatlandırma anına göre** (checkout oturumunun oluşma anı) belirlenir; aynı kural indirim ve komisyonda tek yerde (`DiscountRedemption`) yaşar.
5. **Net tutar = çekilen tutar** (yapılandırılan fiyat KDV hariç kabul edilir; ödemeye KDV eklenmiyor). Fiyata KDV eklenirse komisyon tabanı KDV'siz net olarak kalmalıdır (açık nokta).
6. **İade/iptal elle işaretlenir:** sağlayıcıdan iade olayı gelmediği için platform yöneticisi tahsilat satırını "İptal" yapar (gerekçe zorunlu). Komisyonu henüz ödenmemiş satır hak edişten düşer; ödenmiş satır "geri alınacak" toplamına girer.
7. **Kod girişi:** kayıtta (`owner-signup`) ya da kayıttan sonra **ilk ücretli tahsilattan önce** abonelik sayfasında. Kiracı başına tek kod (benzersiz indeks).
8. **Kendi kodunu kullanma engeli:** ortak kaydında isteğe bağlı `linkedTenantId` (ortağın kendi kiracısı) ve e-posta; kodu kullanan kiracı ortağın kiracısıysa ya da kodu giren kullanıcının e-postası ortağın e-postasıysa kod geçersiz sayılır (tek tip hata).
9. **Yetki:** platform uçları yeni izin yerine `platform_admin` claim'ine bakan `PlatformAdmin` politikasıyla korunur; klinik rolleri (yönetici dahil) **403**.

Mevcut akışta görülen riskler (ilk uygulamada düzeltilmemişti; BILL-002/003 ile düzeltildi):
- ~~Panel `finalize` ucu ödemeyi sağlayıcıdan doğrulamadan aboneliği etkinleştirir~~ — düzeltildi (BILL-002): finalize yalnız `Manual` sağlayıcı oturumunda çalışır; `ISubscriptionCheckoutActivationService.TryActivateAsync` `source: Manual` + `session.Provider != Manual` ise 409 `Billing.ManualFinalizeNotAllowed` döner, kayıt değişmez. Iyzico/Stripe yalnız imzası doğrulanan webhook ile etkinleşir.
- ~~Süresi dolmuş denemede üst plana geçişte fark oranlanıp 1 kuruşa iniyordu~~ — düzeltildi (BILL-003): `StartSubscriptionCheckoutCommandHandler` süresi dolmuş deneme/ücretli dönemde (effective ReadOnly) plan yönü ne olursa olsun hedef planın **tam fiyatını** (oran 1.0) çeker; kod indirimi bu tutara ADR-037 sırasıyla uygulanır. Oranlama yalnız etkin ücretli dönem içindeki yükseltmede kalır.

---

## 1. Kavramlar

- **Satış ortağı** (`SalesPartner`): ad, isteğe bağlı e-posta/telefon, isteğe bağlı bağlı kiracı (`linkedTenantId`), aktif/pasif.
- **İndirim kodu** (`DiscountCode`): ortağa bağlı; kod, indirim %, komisyon %, geçerlilik başlangıç/bitiş (isteğe bağlı), kullanım limiti (isteğe bağlı), aktif/pasif. Yüzdeler kod başına zorunlu girilir (varsayılan yok).
- **Kod kullanımı** (`DiscountRedemption`): kiracıya bağlı (kiracı başına bir), kod/ortak, kullanım anındaki yüzdelerin kopyası, kullanım tarihi, pencere başlangıç/bitişi ve **tahsilat satırları** (`DiscountRedemptionCollection`: checkout oturumu, dönem, brüt, indirim, net, komisyon, durum `Accrued`/`Cancelled`/`Paid`, ödendi tarihi ve notu, iptal tarihi ve gerekçesi).
- Tutarlar **kuruş** (`…Minor`, `long`), para birimi `TRY`.

### Kod normalizasyonu

Kırp; boşlukları at; Türkçe harfleri ASCII'ye indir (`ı/i/İ→I`, `ş→S`, `ğ→G`, `ü→U`, `ö→O`, `ç→C`); büyük harfe çevir. Geçerli biçim: `A-Z`, `0-9`, `-`; 3–32 karakter. Karşılaştırma normalize kod üzerinden (benzersiz). Örn. `" ışık 10 "` → `ISIK10`.

### Kodun geçerli sayılması (tek yer: `DiscountCode.IsRedeemable` + ortak aktifliği)

Kod aktif **ve** ortak aktif **ve** `validFromUtc ≤ şimdi` (doluysa) **ve** `şimdi < validUntilUtc` (doluysa) **ve** limit doluysa kullanım sayısı `< maxRedemptions`.

### Hesap (tek yer: `SubscriptionPricing` + `DiscountRedemption`)

- Sıra: plan fiyatı (aylık) → **yıllık indirim** (dönem yıllıksa liste 12 × aylık, çekilen 10 × aylık; "2 ay hediye") → **kod indirimi** (yüzde, kalan tutara) → net. Örn. Temel ₺999: yıllık ₺9.990; %10 kodla ₺8.991 (listeye göre ≈ %25).
- "Brüt" (`grossAmountMinor`) = kod indirimi öncesi tutar (yıllıkta yıllık indirim sonrası); yükseltme farkı gibi plan fiyatından türemeyen tutarda dönem indirimi yoktur.
- `indirim = round(brüt × indirim% / 100)` (uzaklaşarak yuvarlama), çekilen `net = max(1, brüt − indirim)`.
- `komisyon = round(net × komisyon% / 100)`.
- Uygunluk (fiyatlandırma anı `t`): pencere başlamamışsa uygun; aylıkta `t < windowEndsAtUtc`; yıllıkta etkin tahsilat yok ve (pencere yoksa ya da `t < windowEndsAtUtc`).
- Aynı checkout oturumu iki kez yazılmaz (idempotent).

---

## 2. Anonim kod doğrulama

### `POST /api/v1/public/discount-codes/validate`

- Anonim; hız sınırı `discount-code-validate` (IP başına dakikada 10; aşımda **429** `RateLimit.Exceeded`).
- Gövde: `{ "code": "ISIK10" }`.
- **Her zaman 200.** Geçersiz/süresi dolmuş/pasif/limiti dolmuş/olmayan kod ve boş gövde **aynı** yanıtı verir (numaralandırmaya karşı). Ortak adı/kimliği **dönmez**.

```json
{ "valid": true, "code": "ISIK10", "discountPercent": 10, "discountMonths": 12 }
```

```json
{ "valid": false, "code": null, "discountPercent": null, "discountMonths": null }
```

## 3. Kayıtta kod

### `POST /api/v1/public/owner-signup` (mevcut uç, yeni isteğe bağlı alan)

- Gövdeye `discountCode` (isteğe bağlı) eklendi. Doluysa sunucu yeniden doğrular; geçersizse (kendi kodu dahil) kayıt yapılmaz: **400 `DiscountCodes.Invalid`** ("Ortak kodu geçersiz.").
- Geçerliyse kiracıyla birlikte kod kullanımı yazılır. Yanıta `discount` eklendi: `{ "code": "ISIK10", "discountPercent": 10, "discountMonths": 12 }` ya da `null`.

## 4. Kiracı: abonelikte kod

### `GET /api/v1/tenants/{tenantId}/subscription-discount` — `Subscriptions.Read`

```json
{
  "applied": true, "code": "ISIK10", "discountPercent": 10, "discountMonths": 12,
  "windowStartsAtUtc": "2026-11-05T10:00:00Z", "windowEndsAtUtc": "2027-11-05T10:00:00Z",
  "canApply": false
}
```

`applied=false` iken `code/discountPercent/window…` `null`. `canApply`: kiracının kodu yok **ve** henüz ücretli tahsilatı yok (`ActivatedAtUtc` boş). Ortak adı/kimliği ve komisyon **dönmez**.

### `POST /api/v1/tenants/{tenantId}/subscription-discount` — `Subscriptions.Manage`

- Gövde `{ "code": "…" }`. JWT kiracısı route ile aynı olmalı (`Tenants.AccessDenied` 403).
- Hatalar: **400 `DiscountCodes.Invalid`** (tek tip; kendi kodu dahil), **409 `DiscountCodes.DuplicateRedemption`** (kiracının zaten kodu var), **409 `DiscountCodes.ApplyNotOpen`** (ilk ücretli tahsilat yapılmış). Salt okunur abonelikte de çalışır (ödeme öncesi kod girilebilsin).
- Başarı: GET ile aynı gövde.

### Checkout yanıtı

`POST /tenants/{tenantId}/subscription-checkout` yanıtında `proratedChargeMinor` artık **indirimli (çekilen)** tutardır; yeni `discountAmountMinor` alanı uygulanan indirimi verir (`null` = indirim yok).

## 5. Platform yöneticisi uçları

Tümü `Authorize(Policy = "Platform_Admin")`: JWT'de `platform_admin = true` gerekir; yoksa **403** (klinik yöneticisi dahil), oturum yoksa **401**. Komutlar abonelik yazma korumasından muaftır (kiracı verisi değil).

| Yöntem | Yol | Açıklama |
|---|---|---|
| GET | `/api/v1/admin/sales-partners` | Ortak listesi + ortak başına özet |
| POST | `/api/v1/admin/sales-partners` | Ortak oluştur → 201 |
| GET | `/api/v1/admin/sales-partners/{id}` | Ortak, özet, kodlar |
| PUT | `/api/v1/admin/sales-partners/{id}` | Ortak güncelle (aktif/pasif dahil) |
| POST | `/api/v1/admin/sales-partners/{id}/codes` | Kod oluştur → 201 |
| PUT | `/api/v1/admin/sales-partners/{id}/codes/{codeId}` | Kod yüzde/geçerlilik/limit düzenle |
| POST | `/api/v1/admin/sales-partners/{id}/codes/{codeId}/deactivate` | Kodu kapat |
| POST | `/api/v1/admin/sales-partners/{id}/codes/{codeId}/activate` | Kodu yeniden aç |
| GET | `/api/v1/admin/sales-partners/{id}/redemptions` | Kullanım satırları + tahsilatlar |
| POST | `/api/v1/admin/sales-partners/{id}/commissions/mark-paid` | Komisyon ödendi işaretle |
| POST | `/api/v1/admin/sales-partners/{id}/collections/{collectionId}/cancel` | Tahsilatı iade/iptal işaretle (komisyonu geri al) |
| GET | `/api/v1/admin/sales-partners/export.csv?partnerId=` | Tahsilat satırları CSV (partnerId yoksa tümü) |

### Ortak gövdesi (POST/PUT)

```json
{ "name": "Ali Veli", "email": "ali@example.com", "phone": "05551112233", "linkedTenantId": null, "isActive": true }
```

`name` 2–200 zorunlu; `email` geçerli e-posta ≤200; `phone` ≤30; `linkedTenantId` verilirse kiracı var olmalı (**404 `Tenants.NotFound`**). POST'ta `isActive` yok sayılır (aktif doğar).

### Ortak yanıtı ve özet

```json
{
  "id": "guid", "name": "Ali Veli", "email": "ali@example.com", "phone": "05551112233",
  "linkedTenantId": null, "isActive": true, "createdAtUtc": "…", "codeCount": 2,
  "summary": {
    "redemptionCount": 5, "trialCustomerCount": 3, "paidCustomerCount": 2,
    "revenueNetMinor": 398000, "commissionEarnedMinor": 79600,
    "commissionPaidMinor": 39800, "commissionPendingMinor": 39800, "commissionClawbackMinor": 0
  }
}
```

- `trialCustomerCount`: etkin tahsilatı olmayan kullanım; `paidCustomerCount`: en az bir etkin (`Accrued`/`Paid`) tahsilatı olan.
- `revenueNetMinor`: etkin tahsilatların net toplamı (program kapsamındaki ciro).
- `commissionEarnedMinor` = bekleyen + ödenen; `commissionClawbackMinor`: komisyonu ödendikten sonra iptal edilen satırların komisyonu (ortaktan geri alınacak).

Detay (`GET …/{id}`): `{ "partner": { …yukarıdaki ortak yanıtı… }, "codes": [ …kod yanıtları… ] }`.

### Kod gövdesi ve yanıtı

POST: `{ "code": "ISIK10", "discountPercent": 10, "commissionPercent": 20, "validFromUtc": null, "validUntilUtc": null, "maxRedemptions": null }`. PUT: aynı alanlar, `code` hariç (kod metni değişmez).

- `discountPercent` 0 ≤ x < 100, `commissionPercent` 0 ≤ x ≤ 100 (en çok 2 ondalık); `validUntilUtc > validFromUtc`; `maxRedemptions ≥ 1`.
- Kod biçimi geçersizse **400 `DiscountCodes.Validation`**, aynı normalize kod varsa **409 `DiscountCodes.DuplicateCode`**. Var olan kullanımlar kullanım anındaki yüzdeleri korur.

```json
{
  "id": "guid", "salesPartnerId": "guid", "code": "ISIK10", "discountPercent": 10, "commissionPercent": 20,
  "validFromUtc": null, "validUntilUtc": null, "maxRedemptions": null, "redemptionCount": 3,
  "isActive": true, "createdAtUtc": "…", "deactivatedAtUtc": null
}
```

### Kullanım satırları

```json
[
  {
    "redemptionId": "guid", "tenantId": "guid", "tenantName": "Pati Klinik", "code": "ISIK10",
    "discountPercent": 10, "commissionPercent": 20, "redeemedAtUtc": "…",
    "windowStartsAtUtc": "…", "windowEndsAtUtc": "…", "customerStatus": "Paid",
    "collections": [
      {
        "id": "guid", "checkoutSessionId": "guid", "billingPeriod": "Monthly", "collectedAtUtc": "…",
        "grossAmountMinor": 99900, "discountAmountMinor": 9990, "netAmountMinor": 89910, "commissionAmountMinor": 17982,
        "status": "Accrued", "paidAtUtc": null, "paidNote": null, "cancelledAtUtc": null, "cancelReason": null
      }
    ]
  }
]
```

`customerStatus`: `Trial` | `Paid` (özetle aynı tanım). Enum alanları metin döner.

### Komisyon ödendi işaretleme

`POST …/{id}/commissions/mark-paid` gövde `{ "collectionIds": ["guid"], "paidAtUtc": "2026-11-30T00:00:00Z", "note": "Havale 30.11" }`.
- Tüm satırlar bu ortağın ve `Accrued` olmalı; aksi hâlde hiçbiri değişmez: **404 `DiscountRedemptions.CollectionNotFound`** ya da **409 `DiscountRedemptions.CommissionNotAccrued`** (zaten ödenmiş/iptal). `note` ≤500. Yanıt: `{ "updatedCount": 1 }`.

### Tahsilat iptali (iade)

`POST …/{id}/collections/{collectionId}/cancel` gövde `{ "reason": "iyzico iadesi" }` (zorunlu, ≤500). Zaten iptal ise **409 `DiscountRedemptions.CollectionAlreadyCancelled`**. Yanıt: güncel tahsilat satırı.

### CSV

UTF-8 BOM, `;` ayırıcı, tarih Europe/Istanbul `dd.MM.yyyy`, tutar `0,00` TL; formül enjeksiyonuna karşı hücre koruması. Sütunlar: `Ortak;Kod;Klinik;Tahsilat tarihi;Dönem;Brüt (TL);İndirim (TL);Net (TL);Komisyon (TL);Durum;Ödendi tarihi;Not`. Dosya adı `vetinity-satis-ortagi-komisyon.csv`.

## 6. Denetim kaydı

`IAuditableRequest` ile: `SalesPartner.Create`, `SalesPartner.Update`, `DiscountCode.Create`, `DiscountCode.Update`, `DiscountCode.Deactivate`, `DiscountCode.Activate`, `DiscountCommission.MarkPaid`, `DiscountCollection.Cancel`, `DiscountCode.ApplyToSubscription`. Kayıtta kod kullanımı `owner-signup` akışının parçasıdır (ayrı audit yok).

## 7. Kalıcılık

Tablolar: `SalesPartners`, `DiscountCodes` (benzersiz `NormalizedCode`), `DiscountRedemptions` (benzersiz `TenantId`), `DiscountRedemptionCollections` (benzersiz `CheckoutSessionId`); `BillingCheckoutSessions`'a `GrossAmountMinor`, `DiscountAmountMinor` (boş olabilir). Migration `AddSalesPartnerDiscountCodes` **uygulanmadı**.

## 8. Açık noktalar

- Sağlayıcıdan iade/iptal olayı yok; iade elle işaretlenir.
- Yıllık ödeme (ürün kararı var) faturalandırmada yok: dönem (1 yıl), yıllık fiyat (10 × aylık, `SubscriptionPricing` hazır), checkout'ta dönem seçimi ve `ActivatePaidPlan`'da yıllık pencere ayrı iş; checkout dönemi `Yearly` geçince indirim/komisyon kuralı hazır.
- KDV ödeme tutarına eklenirse komisyon tabanı KDV'siz kalmalı.
- Ortağın kendi satışını görmesi sonraki sürüm (ADR-037 madde 7).
