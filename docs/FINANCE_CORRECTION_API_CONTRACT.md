# Ücret ve ödeme düzeltme, iptal, audit API sözleşmesi — Dilim 5

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-012 (kabul edildi; karar 2 ve 8), backlog FIN-009 (gevşetme ertelendi); ilgili: `CHARGES_API_CONTRACT.md` (Dilim 1), `PAYMENT_ALLOCATION_API_CONTRACT.md` (Dilim 2, bölüm 3 ve 6), `CLIENT_BALANCE_API_CONTRACT.md` (Dilim 3), `FIN_DISCOVERY.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme (inceleme bekliyor). Kapsam: **ücret düzeltme**, **ücret iptali**, **ödeme iptali**, zorunlu gerekçe, audit, eşzamanlılık. Kullanıcı kararları (2026-10-12): ödenmiş ücret önce ödeme iptal edilmeden iptal edilemez; ödeme iptali "hiç alınmamış sayılır" (ayrı iade kaydı yok); ücret tutar düzeltmesi bu dilimde, tahsis edilen tutarın altına inemez. **Kapsam dışı (ertelenmiş):** iade/geri ödeme kaydı ve kasa mutabakatı (CHECKOUT-001), müşteri kredisi/mahsup (FIN-005), çoklu para birimi (FIN-006), eski ödemeyi ücrete bağlama (FIN-007), düzeltme yetkisini gevşetme (FIN-009), kısmi ödeme iptali, iptalin geri alınması. Yeni izin **yoktur**: `Charges.Correct` ve `Payments.Correct` Dilim 1–2'de tanımlandı.

---

## 1) Kavramlar

- **İptal (void) silme değildir.** Kayıt tabloda kalır; `voidedAtUtc`, `voidedByUserId`, `voidReason` dolar. İptal edilen ücret ve ödeme tüm okuma yüzeylerinden (liste, detay, bakiye, Bugün tutarları, rapor, dashboard, müşteri ödeme özeti, Query DB) **düşer**; kimlikle istenirse `404` döner (`Charges.NotFound` / `Payments.NotFound`). İptal geçmişi `AuditLogs`'tadır (bölüm 6) ve kayıt satırındaki gerekçe/kullanıcı alanlarındadır. İptal edilmiş kayıtları listeleyen bir uç bu dilimde yoktur.
- **İptal geri alınmaz.** Yanlış iptal için yeni ücret/ödeme yazılır.
- **Ödeme iptali = hiç alınmamış sayılır.** Tahsilat toplamlarından düşer, tahsisleri geri alınır, ücretlerin açık tutarı artar. Ayrı iade/kasa çıkış kaydı üretilmez; parayı gerçekten geri veren kliniğin kasa kaydı bu üründe tutulmaz (CHECKOUT-001). Kısmi iptal yoktur; tutar yanlışsa ödeme iptal edilip doğrusu yeniden girilir.
- **Ücret iptali ödenmiş ücreti kapsamaz.** Tahsisi olan (`allocatedAmount > 0`) ücret iptal edilemez; önce ilgili ödeme(ler) iptal edilir, tahsisler çözülür, sonra ücret iptal edilir. Böylece kullanılamayan "bağlanmamış" para oluşmaz.
- **Ücret düzeltmesi** `description`, `quantity`, `unitPrice` alanlarını değiştirir; `amount` yeniden hesaplanır (kural `Charge` içinde, Dilim 1 ile aynı: `round(quantity × unitPrice, 2, AwayFromZero)`). Müşteri, hayvan, geliş, muayene, klinik ve para birimi **değişmez** (yanlışsa ücret iptal edilip yenisi yazılır). Yeni tutar tahsis edilmiş tutarın altına inemez.
- **Ödeme düzeltmesi** Dilim 2 bölüm 3'te tanımlıdır (`PUT /payments/{id}`; tutar/müşteri/klinik/para birimi için `Payments.Correct` + gerekçe, tahsisli ödemede 409) ve **değişmez**. Tahsisli ödemenin tutarı düzeltilemez; yol ödemeyi iptal edip yeniden girmektir.
- Yalnızca TRY; bu dilim para birimi ekleme yapmaz.

### Sayaç değişmezleri (iptal sonrası da geçerli)

- `Σ aktif PaymentAllocations.Amount (payment) = payment.AllocatedAmount` ve `Σ aktif PaymentAllocations.Amount (charge) = charge.AllocatedAmount`. "Aktif" = `ReversedAtUtc IS NULL`.
- Ödeme iptalinde her aktif tahsis `ReversedAtUtc` ile kapatılır (satır silinmez), ücret sayacı (`Charge.AllocatedAmount`) düşer, ödeme sayacı 0 olur. İptal edilmiş ödemenin tahsis satırları geçmişte kalır, hiçbir okuma ucunda "aktif" görünmez.
- `0 ≤ AllocatedAmount ≤ Amount` kısıtları aynen korunur.

---

## 2) Veri modeli (migration `AddFinanceVoidAndReversal`; uygulama için kullanıcı onayı gerekir)

| Tablo | Değişiklik |
|---|---|
| `Payments` | `VoidedAtUtc datetime2 NULL`, `VoidedByUserId uniqueidentifier NULL`, `VoidReason nvarchar(500) NULL`. `CHECK ((VoidedAtUtc IS NULL) = (VoidReason IS NULL) AND (VoidedAtUtc IS NULL) = (VoidedByUserId IS NULL))`; `CHECK (VoidedAtUtc IS NULL OR AllocatedAmount = 0)` |
| `Charges` | Aynı üç kolon ve aynı iki `CHECK` |
| `PaymentAllocations` | `ReversedAtUtc datetime2 NULL` (iptalde dolar; aktif tahsis sorguları `ReversedAtUtc IS NULL` süzer) |

- Mevcut satırlar için yeni kolonlar `NULL` kalır; veri taşıma yoktur.
- `Payment` ve `Charge` için **EF global query filter** `VoidedAtUtc IS NULL` tanımlanır: iptal edilenlerin dışlanması tek yerde yaşar, ayrı okuma yüzeylerinde tek tek süzme yoktur. İptal ucu ve `requestId` tekrar araması `IgnoreQueryFilters` kullanır (bölüm 4).
- Eşzamanlılık belirteci mevcut `MutationSequence`'tır (Dilim 2); iptal ve düzeltme sayacı 1 artırır. Yeni belirteç yoktur.

---

## 3) Uçlar

Tümü `api/v1`, klinik kapsamı ve kiracı bağlamı mevcut ödeme/ücret yazma kurallarıyla aynıdır (kapsam dışı klinikte 404, varlık sızdırmaz; pasif kiracı `Tenants.TenantInactive`).

### 3.1 `PUT /api/v1/charges/{id}` — ücret düzeltme

`Charges.Correct` (Admin, Owner, ClinicAdmin). İstek:

```json
{ "description": "Genel muayene", "quantity": 1, "unitPrice": 600.00, "reason": "Fiyat listesi yanlış okundu" }
```

- `reason` zorunlu, kırpıldıktan sonra 5–500 karakter (`Charges.Validation`).
- `description`/`quantity`/`unitPrice` Dilim 1 doğrulamalarıyla aynı (`Charges.Validation`). Hiçbir alan mevcut değerden farklı değilse `Charges.Validation` (400; "değişiklik yok"), kayıt ve audit "başarısız" yazılır.
- Yeni `amount < allocatedAmount` ise 409 `Charges.CorrectionBelowAllocated` (yetkili olsa da). Eşitlik serbesttir (ücret tam ödenmiş sayılır).
- Başarılı yanıt **200**, gövde güncel `ChargeDto`. Eşzamanlı tahsis/iptal/düzeltme çakışırsa 409 `Charges.ConcurrencyConflict`; istemci yeniden okur. Hiçbir şey yazılmadığı için aynı istek güvenle tekrarlanır.

### 3.2 `POST /api/v1/charges/{id}/void` — ücret iptali

`Charges.Correct`. İstek `{ "reason": "…" }` (5–500 karakter). Yanıt **204**.

| Durum | Sonuç |
|---|---|
| `allocatedAmount > 0` | 409 `Charges.VoidBlockedByAllocation` ("önce ilgili ödemeleri iptal edin"; `detail` açık tutar bilgisi vermez, tahsis ödemeleri `GET /charges/{id}/allocations` ile okunur) |
| Zaten iptal edilmiş | 409 `Charges.AlreadyVoided` (ikinci çağrı ikinci etki üretmez) |
| Eşzamanlı tahsis veya düzeltme | 409 `Charges.ConcurrencyConflict` |
| Kayıt yok / kapsam dışı | 404 `Charges.NotFound` |

### 3.3 `POST /api/v1/payments/{id}/void` — ödeme iptali

`Payments.Correct`. İstek `{ "reason": "…" }` (5–500 karakter). Yanıt **204**.

Tek işlemde (herhangi bir adım başarısız olursa hiçbir şey yazılmaz):

1. Ödeme iptal edilir (`voidedAt/By/Reason`).
2. Her aktif tahsis `ReversedAtUtc` ile kapatılır; ilgili her ücretin `allocatedAmount` değeri tahsis kadar düşer (ücretin açık tutarı artar). Ücret iptal edilmiş olamaz (tahsisli ücret iptal edilemediği için bu durum oluşmaz).
3. Ödemenin `allocatedAmount` değeri 0 olur.
4. `payment.voided.v1` olayı outbox'a yazılır (bölüm 5).

| Durum | Sonuç |
|---|---|
| Zaten iptal edilmiş | 409 `Payments.AlreadyVoided` |
| Eşzamanlı tahsis/güncelleme/iptal | 409 `Payments.ConcurrencyConflict` (yeniden okunup tekrarlanır) |
| Kayıt yok / kapsam dışı | 404 `Payments.NotFound` |

- **Eski (bağlanmamış) ödeme** de iptal edilebilir; yalnızca tahsilat toplamını etkiler (tahsis yoktur).
- İptal edilen ödemenin `requestId` değeri tüketilmiş kalır: aynı `requestId` ile `POST /payments` tekrar gelirse 409 `Payments.RequestIdVoided` (istemci yeni `requestId` üretir). Aynı kural ücret için: 409 `Charges.RequestIdVoided`.

---

## 4) Okuma yüzeyleri (iptal edilenin dışlanması)

Global query filter ile Command DB'deki tüm okuma yolları iptal edileni dışlar; ayrıca kod eklenmez. Etkilenenler:

| Yüzey | Etki |
|---|---|
| `GET /charges`, `GET /charges/{id}`, `GET /charges/{id}/allocations` | İptal edilmiş ücret görünmez; ücretin tahsis listesi yalnızca **aktif** tahsisleri verir |
| `GET /payments`, `/payments/{id}`, `/payments/{id}/allocations`, ödeme raporu ve export | İptal edilmiş ödeme görünmez; tahsis listesi yalnızca aktif tahsisleri verir |
| `GET /clients/{id}/balance` | Ücret iptalinde `chargedAmount` düşer; ödeme iptalinde `allocatedAmount` düşer, `outstandingAmount` artar. Tahsissiz eski ödeme listesinde iptal edilen görünmez |
| `GET /visits/today` | `chargedAmount/allocatedAmount/outstandingAmount/clientOutstandingAmount` yeniden hesaplanır; `paymentIndicator` iptal edilen ödemeyi saymaz |
| Dashboard finans, son ödemeler, müşteri ödeme özeti, hayvan/muayene ödeme özetleri | Tahsilat toplamlarından iptal edilen düşer |

DTO değişikliği: yeni alan yoktur (iptal edilenler dönmez). Tahsis DTO'larında (`PaymentAllocationItemDto`, `ChargeAllocationItemDto`) değişiklik yoktur.

`IgnoreQueryFilters` yalnızca iki yerde kullanılır: iptal uçları (zaten iptal edilmiş kaydı `AlreadyVoided` ile ayırt etmek için) ve `requestId` tekrar araması (`RequestIdVoided`).

---

## 5) Query DB, rapor ve olaylar

- Yeni outbox olayı **`payment.voided.v1`**: `PaymentProjectionSnapshot` (mevcut şema) taşır. Create/updated olayları değişmez. Olay tipi `PaymentIntegrationEventTypes.All` ve Infrastructure tip kaydına eklenir (bilinmeyen tip dead-letter olur).
- `PaymentProjectionProcessor` olayı işleyince ödemenin `PaymentReadModels` satırını ve `PaymentDailyContributionReadModels` kaydını siler, günlük özeti (`ClinicDailyPaymentStats`) mevcut recompute yoluyla günceller. Aynı `EventId` tekrar gelirse mevcut idempotency (`ProcessedProjectionEvents`) çalışır; stale koruması contribution `LastEventOccurredAtUtc` üzerinden devam eder. Önce `voided`, sonra gelen eski `updated` olayı yok sayılır (satır yeniden doğmaz).
- Backfill/parity servisleri Command DB'den okuduğu için global filtre iptal edilenleri zaten dışlar.
- Ücret iptali/düzeltmesi olay üretmez (ücret Query DB'ye taşınmaz).
- Query DB bayrakları her ortamda kapalıdır; açıldığında iptal edilen ödemenin gecikmeyle düşmesi (eventual consistency) beklenir. Bakiye ve Bugün tutarları Command DB'den okunur, gecikmesizdir.

---

## 6) Audit

- Tüm iptal/düzeltme komutları `IAuditableRequest`'tir; başarılı ve başarısız her çağrı `AuditLogs`'a yazılır (Dilim 1'deki `Charge.Create` ile aynı mekanizma; kullanıcı, IP, korelasyon, sonuç, hata kodu).

| Eylem (`Action`) | Hedef (`TargetId`) | Payload'da |
|---|---|---|
| `Charge.Correct` | `ChargeId=…` | yeni `description/quantity/unitPrice` ve `reason` |
| `Charge.Void` | `ChargeId=…` | `reason` |
| `Payment.Void` | `PaymentId=…` | `reason` |
| `Payment.Update` (mevcut) | `PaymentId=…` | düzeltmede `reason` |

- Önceki değer ayrı kolonda tutulmaz; ücret geçmişi `Charge.Create` ve sonraki `Charge.Correct` kayıtlarının sırasıyla çıkarılır. İptal kaydının kendisi satırda da durur (`voidedAtUtc/voidedByUserId/voidReason`).
- `reason` ham metin olarak audit payload'ında saklanır; sır içermemelidir (istemci uyarısı).

---

## 7) Yetkiler

| İşlem | Policy | Roller (varsayılan seed; **değişiklik yok**) |
|---|---|---|
| Ücret düzeltme / iptal | `Charges.Correct` | Admin, Owner, ClinicAdmin |
| Ödeme iptali (ve tutar düzeltme) | `Payments.Correct` | Admin, Owner, ClinicAdmin |

Sekreter ve Veteriner iptal/düzeltme yapamaz (403, policy). Seeder yeniden çalıştırılması gerekmez (izinler Dilim 1–2'de eklendi).

---

## 8) Hata kodları

| Kod | HTTP |
|---|---|
| `Charges.Validation`, `Payments.Validation` (gerekçe 5–500, değişiklik yok, alan sınırları) | 400 |
| `Charges.NotFound`, `Payments.NotFound` | 404 |
| `Charges.VoidBlockedByAllocation`, `Charges.CorrectionBelowAllocated` | 409 |
| `Charges.AlreadyVoided`, `Payments.AlreadyVoided` | 409 |
| `Charges.RequestIdVoided`, `Payments.RequestIdVoided` | 409 |
| `Charges.ConcurrencyConflict`, `Payments.ConcurrencyConflict` | 409 |
| `Clinics.AccessDenied`, `Tenants.TenantInactive` | 403 |
| `Tenants.ContextMissing` | 400 |

HTTP eşlemesi `ResultExtensions.MapStatusCode` içindedir (tek yer); yeni kodlar için gereken 409 satırları eklenir.

---

## 9) Test odağı

- **Domain:** ücret düzeltme (yeniden hesap, tahsis altına inme, no-op), ücret iptali (tahsisli reddi, çift iptal), ödeme iptali (tahsis geri alma, sayaç değişmezleri, eski ödeme), gerekçe sınırları.
- **Uygulama:** yetki (Sekreter/Veteriner 403), gerekçe eksik, çift iptal, eşzamanlılık çakışması, kapsam dışı klinik 404, olay üretimi (`payment.voided.v1` yalnız ödeme iptalinde).
- **Entegrasyon (LocalDB):** 1.000 TL ücret / 400 + 600 ödeme → 600 TL ödeme iptali → ücret açık tutarı 600, bakiye ve Bugün alanları doğru; ücret tahsisliyken iptal 409; ödeme iptali sonrası ücret iptali 204 ve bakiyeden düşme; `Σ aktif tahsis = sayaç`; iptal edilen ödemenin liste/rapor/dashboard/özetten düşmesi; `requestId` tekrarı `RequestIdVoided`; iki kullanıcı aynı anda iptal/tahsis (biri 409); Query DB projeksiyonunda `voided` sonrası satırın ve günlük özetin düşmesi (parity).
- Mevcut finans parity testlerinin yeşil kalması.

## 10) Açık / sonraki işler

- İptal edilen kayıtları görme (yönetici raporu / `includeVoided`) ve arayüzde "iptal edildi" gösterimi: gerekirse ayrı iş.
- İade/geri ödeme kaydı ve kasa mutabakatı: CHECKOUT-001 (iptal kaydı ileride iade kaydına bağlanabilir).
- Migration yerel DB'de uygulanmadan önce kullanıcı onayı gerekir; bu dilimde uygulanmaz.
