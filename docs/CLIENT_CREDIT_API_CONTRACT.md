# Müşteri kredisi ve mahsup (ClientCredit) API sözleşmesi — FIN-005

Backend tek doğruluk kaynağıdır. Kaynak: ADR-012 (ödeme ücrete bağlıdır; fazla ödeme ilk dilimde reddedildi), backlog FIN-005 (kullanıcı kararı 2026-10-30: P1 tamamlama turuna alındı), `PAYMENT_ALLOCATION_API_CONTRACT.md` §9 madde 1 ("avans/depozito/önceden ödeme ayrı Kredi kaydı"), `CLIENT_BALANCE_API_CONTRACT.md`, `FINANCE_CORRECTION_API_CONTRACT.md`, `DAILY_TRANSACTIONS_REPORT_API_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme (P1 Dalga 4b, dal `feat/dalga-4b`). Kapsam: fazla ödeme ve avansın **müşteri kredisine** dönüşmesi, kredinin sonraki ücretlere **mahsubu**, mahsubun geri alınması, kredinin iadesi, ödeme/ücret iptaliyle etkileşim, bakiye/portal/gün sonu dökümünde görünüm. Kapsam dışı: paraya dayanmayan "iyi niyet" kredisi (FIN-009 adayı), kısmi iade, kredinin başka müşteriye/kliniğe devri, çoklu para birimi (FIN-006), eski bağlanmamış ödemelerin krediye çevrilmesi (FIN-007).

---

## 1) Kararlar (rakip pratiğinden türetildi; ürün sahibi onayına açık)

Rakip gözlemi: DaySmart'ta müşteri hesabında "Credit" kaydı tutulur, fatura kapatılırken personel krediyi uygular, kredi iadesi ayrı bir işlemdir ([daysmart.md](../../../../Vetinity/vetinity-product/competitors/daysmart.md) Billing/Credits; sandbox gözlemi, doğrulanmadı). E-Vet'te avans/depozito müşteri bakiyesinde alacak olarak görünür. Çözülen problem: *müşteri fazla ya da peşin ödediğinde para kaybolmasın, sonraki ücrette otomatik borç çıkmasın, kasa ve bakiye tutarlı kalsın.*

- **K1 — Kredi her zaman alınan paraya dayanır.** Kredi yalnız bir **ödemeden** doğar: (a) **fazla ödeme** — ödeme tutarı tahsis edilen ücretlerin açık toplamını aşar, artan kısım krediye yazılır; (b) **avans/depozito** — ödeme hiçbir ücrete tahsis edilmez, tamamı kredidir. Elle, parasız kredi (indirim/iyi niyet) **yoktur**; o ayrı karardır. Böylece ADR-012 madde 6 korunur: para ya ücrete ya krediye bağlıdır, "bağlanmamış" yeni ödeme yine yoktur.
- **K2 — Kapsam.** Kredi **müşteri + klinik** kapsamlıdır (ücret ve ödeme gibi), yalnız TRY. Başka klinikteki ücrete mahsup edilemez.
- **K3 — Mahsup personelce açıkça yapılır; otomatik mahsup yoktur.** Rakip pratiğiyle aynı: ücret oluşunca borç normal doğar; personel ödeme alırken ya da bakiye ekranından "krediden mahsup et" der. Arayüz krediyi belirgin gösterir ve ödeme ekranında önce krediyi önerir. Mahsupta krediler **en eskisinden başlayarak** (FIFO) tüketilir; personel kredi seçmez.
- **K4 — Mahsup, ücretin tahsis sayacını kullanır.** Mahsup satırı (`ClientCreditApplication`) ödeme tahsisiyle (`PaymentAllocation`) aynı biçimdedir ve ücretin `AllocatedAmount` sayacını aynı yoldan (`Charge.ApplyAllocation`) artırır. Böylece açık tutar, müşteri bakiyesi, Bugün satırı, portal bakiyesi, borç raporu ve gün sonu açık işleri **hiçbir ek kural olmadan** tutarlı kalır. Yeni değişmez: `Charge.AllocatedAmount = Σ aktif ödeme tahsisi + Σ aktif kredi mahsubu`. Kasa (tahsilat) toplamları değişmez: mahsup para almaz, zaten alınmış parayı borca sayar.
- **K5 — Mahsup geri alınabilir** (yanlış ücrete mahsup): `Payments.Correct` + gerekçe; kredi kalanı geri gelir, ücretin sayacı düşer. Mahsup satırı silinmez, `ReversedAtUtc` ile kapanır (ödeme iptalindeki tahsis deseniyle aynı).
- **K6 — Kredi iadesi = kasa çıkışı.** Kalan kredinin **tamamı** müşteriye geri verilir (`Payments.Correct`, gerekçe, iade yöntemi). Kredi kapanır (`RefundedAtUtc`). Gün sonu dökümünde **"Kredi iadesi"** satırı negatif tahsilat olarak görünür (K-D6-1 satır = olay); kasa özetinde net tahsilattan düşer. Kısmi iade yoktur.
- **K7 — Ödeme iptali ile etkileşim.** Ödemeden doğan kredi hiç kullanılmamış ve iade edilmemişse ödeme iptalinde kredi de iptal olur (`VoidedAtUtc`). Kredi kısmen/tamamen kullanılmış ya da iade edilmişse ödeme iptali **409 `Payments.VoidBlockedByCredit`**: önce mahsuplar geri alınır (K5); iade edilmiş kredi varsa ödeme iptal edilemez (para iki kez geri verilmiş olurdu).
- **K8 — Ücret iptali/düzeltmesi.** Mahsubu olan ücret mevcut kuralla iptal edilemez (`Charges.VoidBlockedByAllocation`; önce mahsup geri alınır). Ücret tutarı mahsup dahil tahsis edilenin altına inemez (mevcut `Charges.Correct` kuralı).
- **K9 — Kredi görünürlüğü.** Müşteri bakiyesinde kalan kredi ayrı alan (`creditBalance`), açık borçtan **düşülmez**; "net" alanı yoktur (bakiye sözleşmesiyle aynı tutum: tutarlar ayrı verilir, arayüz isterse farkı gösterir). Portal bakiyesinde de kalan kredi görünür (AK-9: yalnız tutar).

## 2) Veri modeli (migration `AddClientCredits`)

| Tablo | Değişiklik |
|---|---|
| `ClientCredits` (yeni) | `Id`, `TenantId`, `ClinicId`, `ClientId`, `SourcePaymentId` (FK `Payments`, silme kısıtlı), `Source` (`Overpayment`=0, `Prepayment`=1), `Amount decimal(18,2)`, `UsedAmount decimal(18,2) DEFAULT 0`, `Currency char(3)` (TRY), `CreatedByUserId`, `CreatedAtUtc`, `RefundedAtUtc`, `RefundedByUserId`, `RefundReason nvarchar(500)`, `RefundMethod` (PaymentMethod), `VoidedAtUtc` (kaynak ödeme iptali), `MutationSequence bigint` (concurrency token). `CHECK (UsedAmount >= 0 AND UsedAmount <= Amount)`, `CHECK (Amount > 0)`. İndeks `(TenantId, ClientId, ClinicId)`, `(TenantId, SourcePaymentId)` benzersiz |
| `ClientCreditApplications` (yeni) | `Id`, `TenantId`, `ClinicId`, `CreditId` (FK), `ChargeId` (FK), `Amount decimal(18,2)`, `RequestId uniqueidentifier NULL`, `CreatedByUserId`, `CreatedAtUtc`, `ReversedAtUtc`, `ReversedByUserId`, `ReverseReason nvarchar(500)`. `CHECK (Amount > 0)`. İndeks `(TenantId, ChargeId)`, `(TenantId, CreditId)`, `(TenantId, RequestId)` (filtreli, benzersiz **değil**: bir istek birden çok satır üretir) |
| `Payments` | `CreditedAmount decimal(18,2) NOT NULL DEFAULT 0`. `CHECK (AllocatedAmount + CreditedAmount <= Amount)`. Yeni ödemelerde `AllocatedAmount + CreditedAmount = Amount`; `UnallocatedAmount = Amount − AllocatedAmount − CreditedAmount` eski ödemeler dışında 0 |

- Kalan kredi: `credit.remainingAmount = amount − usedAmount`; iade edilmiş ya da iptal edilmiş kredi kapalıdır (kalan 0 sayılır, listede `status` ile görünür).
- Kredi durumu (hesaplanır, `status`): `Open` (kalan > 0), `Used` (kalan 0, iade/iptal yok), `Refunded`, `Voided`.
- Sayaçlar tek yazma yolundan güncellenir: `ClientCredit.Apply` / `ClientCredit.ReverseApplication` / `ClientCredit.Refund` / `ClientCredit.VoidWithPayment`, `Charge.ApplyAllocation` / `Charge.ReleaseAllocation`, `Payment.AllocateFully(lines, creditAmount)`. Entegrasyon testi `Charge.AllocatedAmount = Σ PaymentAllocations(aktif) + Σ ClientCreditApplications(aktif)` ve `ClientCredit.UsedAmount = Σ ClientCreditApplications(aktif)` eşitliğini doğrular.
- Mevcut satırlar için yeni kolon varsayılan değerle dolar; veri taşıma yoktur.

## 3) `POST /api/v1/payments` (mevcut uç genişler; geriye uyumlu)

Gövdeye **opsiyonel** `creditAmount` (decimal, varsayılan 0, en fazla 2 ondalık) eklenir:

```json
{ "clinicId": "…", "clientId": "…", "amount": 1000.00, "currency": "TRY", "method": 1, "paidAtUtc": "…",
  "requestId": "…", "allocations": [ { "chargeId": "…", "amount": 600.00 } ], "creditAmount": 400.00 }
```

| Durum | Kural |
|---|---|
| `creditAmount` yok / 0 | Bugünkü davranış: `Σ allocations == amount`, `allocations` boş olamaz |
| `creditAmount` > 0 | `Σ allocations + creditAmount == amount` → `Payments.AllocationSumMismatch` (400). `allocations` **boş olabilir** (avans: `creditAmount == amount`, kaynak `Prepayment`); doluysa kaynak `Overpayment` |
| `creditAmount` < 0 ya da 2 ondalıktan fazla | `Payments.Validation` (400) |

- Ödeme, tahsisler, kredi ve sayaçlar **tek işlemde** yazılır; `requestId` parmak izine `creditAmount` da girer (aynı `requestId` farklı `creditAmount` → `Payments.DuplicateRequestMismatch`).
- Kredi kaydı: `ClinicId`/`ClientId` ödemeyle aynı, `Amount = creditAmount`, `CreatedByUserId` ödemeyi alan, `CreatedAtUtc` ödeme anı (`paidAtUtc` değil, işlem anı).
- Yanıt değişmez (201, `Guid`). `payment.created.v1` olayı değişmez (kredi olaya girmez; Query DB kredi bilmez).
- `GET /payments/{id}`: `PaymentDetailDto`'ya `creditedAmount` eklenir; `unallocatedAmount` artık `amount − allocatedAmount − creditedAmount`.

## 4) Mahsup — `POST /api/v1/clients/{clientId}/credits/apply`

`Payments.Create` (tahsilat alan herkes mahsup yapabilir). Klinik context-first (`clinicId` JWT/başlıkla uyuşmazsa `Payments.ClinicContextMismatch` 400).

```json
{ "clinicId": "…", "requestId": "…", "allocations": [ { "chargeId": "…", "amount": 250.00 } ] }
```

Kural: `Σ allocations ≤ müşterinin bu klinikteki kalan kredisi`; her satır ücretin açık tutarını aşamaz; ücretler aynı kiracı/klinik/müşteri/TRY. Krediler oluşturma sırasına göre (FIFO) tüketilir; bir satır birden çok krediden karşılanabilir, her kredi–ücret çifti ayrı mahsup satırıdır.

Yanıt **200**:

```json
{ "clientId": "…", "clinicId": "…", "appliedAmount": 250.00, "remainingCreditBalance": 150.00,
  "applications": [ { "id": "…", "creditId": "…", "chargeId": "…", "amount": 250.00, "createdAtUtc": "…" } ] }
```

| Kod | HTTP | Durum |
|---|---|---|
| `ClientCredits.AllocationsRequired`, `ClientCredits.Validation` | 400 | Boş liste; tutar ≤ 0 ya da 2 ondalıktan fazla; aynı ücret iki kez; `requestId` boş |
| `ClientCredits.ChargeClinicMismatch`, `ClientCredits.ChargeClientMismatch` | 400 | Ücret başka klinik/müşteri |
| `Clients.NotFound`, `Charges.NotFound` | 404 | |
| `ClientCredits.InsufficientBalance` | 409 | Kalan kredi yetmez (`detail` kalanı yazar) |
| `PaymentAllocations.ExceedsOutstanding` | 409 | Satır ücretin açık tutarını aşıyor (ödeme tahsisiyle aynı kod) |
| `ClientCredits.ConcurrencyConflict` | 409 | Kredi ya da ücret aynı anda değişti; yeniden okuyup tekrar deneyin |
| `Clinics.AccessDenied` | 403 | Kliniğe atanmamış kullanıcı |

Tekrar isteği (`requestId`): aynı kiracıda aynı `requestId` ile daha önce yazılmış mahsup satırları varsa 200 ve o satırlar döner, ikinci kez yazılmaz (parmak izi tutulmaz; `requestId` istemci tarafında istek başına üretilir). Eşzamanlı aynı `requestId` iki kez yazarsa `ClientCredits.ConcurrencyConflict` döner; ücret sayaçları iyimser eşzamanlılıkla korunur, en kötü durumda `CHECK` kısıtı son savunmadır.

Denetim: `ClientCredit.Apply`, hedef `ClientId=…;ClinicId=…`.

## 5) Mahsubu geri alma — `POST /api/v1/clients/{clientId}/credits/applications/{applicationId}/reverse`

`Payments.Correct`. Gövde `{ "reason": "…" }` (5–500 karakter, `ClientCredits.Validation`). **204**. Kredi kalanı artar, ücretin sayacı düşer (`Charge.ReleaseAllocation`), satır `ReversedAtUtc`/`ReversedByUserId`/`ReverseReason` alır.

| Kod | HTTP |
|---|---|
| `ClientCredits.ApplicationNotFound` | 404 |
| `ClientCredits.ApplicationAlreadyReversed` | 409 |
| `ClientCredits.CreditClosed` (kredi iade/iptal edilmiş) | 409 |
| `ClientCredits.ConcurrencyConflict` | 409 |
| `Payments.CorrectionForbidden` | 403 |

Denetim: `ClientCredit.ReverseApplication`, hedef `ApplicationId=…;ChargeId=…`, gerekçe yükte.

## 6) Kredi iadesi — `POST /api/v1/clients/{clientId}/credits/{creditId}/refund`

`Payments.Correct`. Gövde `{ "reason": "…", "method": "Cash|Card|Transfer" }`. Kalan kredinin tamamı iade edilir; kredi kapanır. Yanıt **200** `ClientCreditDto`.

| Kod | HTTP |
|---|---|
| `ClientCredits.NotFound` | 404 |
| `ClientCredits.NothingToRefund` (kalan 0, iade/iptal edilmiş) | 409 |
| `ClientCredits.ConcurrencyConflict` | 409 |
| `Payments.CorrectionForbidden` | 403 |

Denetim: `ClientCredit.Refund`, hedef `CreditId=…`, gerekçe ve iade tutarı yükte. Gün sonu dökümüne "Kredi iadesi" olayı olarak girer (§9).

## 7) Okuma uçları

### `GET /api/v1/clients/{clientId}/credits?clinicId&includeClosed=false`

`Charges.Read`. Klinik kapsamı bakiye ucuyla aynı çözümleyici (bağlam yoksa erişilebilir tüm klinikler). Oluşturma zamanı azalan.

```json
[ { "id": "…", "clinicId": "…", "clientId": "…", "sourcePaymentId": "…", "source": "Overpayment", "status": "Open",
    "amount": 400.00, "usedAmount": 250.00, "remainingAmount": 150.00, "currency": "TRY",
    "createdAtUtc": "…", "createdByUserId": "…", "refundedAtUtc": null, "refundMethod": null, "refundReason": null, "voidedAtUtc": null,
    "applications": [ { "id": "…", "chargeId": "…", "chargeDescription": "Muayene", "amount": 250.00, "createdAtUtc": "…", "reversedAtUtc": null, "reverseReason": null } ] } ]
```

`includeClosed=false` (varsayılan) yalnız `Open` kredileri döner; `true` tüm kredileri (kullanılmış, iade, iptal) döner. Enum alanları metin.

### `GET /api/v1/clients/{clientId}/balance` (eklenen alanlar)

`creditBalance` (decimal, kapsamdaki açık kredilerin kalan toplamı), `openCreditCount` (int). `outstandingAmount` değişmez (K9). `ClientBalanceChargeDto`'ya `creditAppliedAmount` eklenir (ücrete mahsup edilen aktif toplam; `allocatedAmount` zaten mahsubu içerir, bu alan kırılım içindir).

### `GET /api/v1/charges/{id}/allocations` (eklenen satır türü)

Mevcut ödeme tahsisi satırlarına ek olarak mahsup satırları `{ id, paymentId: null, creditId, paidAtUtc: null, method: null, amount, createdAtUtc, createdByUserId, kind: "Credit" }` döner; ödeme satırları `kind: "Payment"`, `creditId: null`. Geriye uyumlu alan eklemesidir.

### Portal `GET /api/portal/v1/links/{linkId}/balance` (eklenen alan)

`creditBalance` (kalan kredi, bağın kliniği). `PORTAL_API_CONTRACT.md` §6.5 güncellenir. Kredi hareketleri portalda listelenmez (AK-9: yalnız tutar).

### Bugün satırı (`GET /visits/today`)

Değişmez: `clientOutstandingAmount` ücret sayaçlarından gelir ve mahsup zaten sayaçta yansır. Kredi tutarı Bugün satırına eklenmez (ödeme ekranı gösterir).

## 8) Ödeme iptali (`POST /payments/{id}/void`, mevcut uç)

Ödemenin kredisi (`ClientCredits.SourcePaymentId = id`) varsa:

| Kredi durumu | Sonuç |
|---|---|
| `Open` ve `UsedAmount = 0` | Ödeme ve kredi birlikte iptal (`VoidedAtUtc`), tek işlem |
| `UsedAmount > 0` (aktif mahsup var) | 409 `Payments.VoidBlockedByCredit` — "Bu ödemeden doğan kredi ücretlere mahsup edilmiş; önce mahsupları geri alın." |
| `Refunded` | 409 `Payments.VoidBlockedByCredit` — iade edilmiş kredinin ödemesi iptal edilemez |

Tüm mahsuplar geri alınmışsa (`UsedAmount` tekrar 0) ödeme iptal edilebilir. `payment.voided.v1` olayı değişmez.

## 9) Gün sonu dökümü ve kasa özeti (`DAILY_TRANSACTIONS_REPORT_API_CONTRACT.md` eki)

- Yeni olay türü **`CreditRefund`** ("Kredi iadesi"): zaman `refundedAtUtc`, tutar iade edilen kredi (pozitif sayı, `direction: "Out"` / kasadan çıkış), işlemi yapan `refundedByUserId`, ilişkili kayıt `creditId` ve `sourcePaymentId`, gerekçe. Satır biçimi mevcut döküm satırıyla aynıdır; `kind` yeni değer alır.
- Kasa özeti: `netCollected = alınan − iptal edilen − iade edilen kredi`; kullanıcı kırılımında iade, iadeyi yapana yazılır. Kasa özeti DTO'suna `creditRefundedAmount` eklenir.
- Ödeme satırında tutar değişmez (fazla ödeme de tahsilattır; kasa gerçek alınan parayı gösterir). Mahsup kasa olayı değildir, dökümde satır üretmez.
- Ödeme raporu/export, dashboard finans, Query DB projeksiyonu değişmez.

## 10) Hata kodları (özet)

| Kod | HTTP |
|---|---|
| `ClientCredits.Validation`, `ClientCredits.AllocationsRequired`, `ClientCredits.ChargeClinicMismatch`, `ClientCredits.ChargeClientMismatch`, `Payments.Validation`, `Payments.AllocationSumMismatch` | 400 |
| `Payments.CorrectionForbidden`, `Clinics.AccessDenied` | 403 |
| `ClientCredits.NotFound`, `ClientCredits.ApplicationNotFound`, `Clients.NotFound`, `Charges.NotFound` | 404 |
| `ClientCredits.InsufficientBalance`, `ClientCredits.ApplicationAlreadyReversed`, `ClientCredits.CreditClosed`, `ClientCredits.NothingToRefund`, `ClientCredits.ConcurrencyConflict`, `PaymentAllocations.ExceedsOutstanding`, `Payments.VoidBlockedByCredit` | 409 |

HTTP eşlemesi `ResultExtensions.MapStatusCode` içindedir (tek yer); yeni 409 kodları oraya eklenir.

## 11) Yetkiler

Yeni izin **yoktur**: oluşturma `Payments.Create` ile ödeme içinde; mahsup `Payments.Create`; geri alma ve iade `Payments.Correct` (Admin, Owner, ClinicAdmin); okuma `Charges.Read`.

## 12) Uygulama notları ve test odağı

- Kural tek yerde: `Payment.AllocateFully(lines, creditAmount, …)` (ödeme bölüşümü), `ClientCredit` (mahsup, geri alma, iade, iptal), `Charge.ApplyAllocation/ReleaseAllocation` (sayaç sınırı). Handler yalnız yükler, FIFO sırasını verir ve doğrular. Mahsup ve iade komutları `ITransactionalRollbackOnFailureRequest`.
- Test odağı (domain): 1.000 TL ödeme / 600 ücret → 400 kredi; avans (tahsis yok); `Σ + credit ≠ amount` reddi; FIFO iki krediden tek ücrete mahsup; mahsup açık tutarı aşamaz; kalan yetmez; geri alma sayaçları; iade yalnız kalanın tamamı ve kapalı kredide red; ödeme iptali kullanılmış/iade edilmiş krediyle 409, kullanılmamışla birlikte iptal; ücret iptali mahsupla engelli.
- Entegrasyon (LocalDB): uçtan uca ödeme→kredi→mahsup→bakiye (`creditBalance`, `outstandingAmount`); portal bakiyesinde `creditBalance`; sayaç = Σ satırlar değişmezi; `requestId` tekrarı; kiracı ve klinik izolasyonu; Sekreter mahsup yapabilir, Veteriner 403; iade yalnız `Payments.Correct`; gün sonu dökümünde `CreditRefund` satırı ve kasa netinden düşmesi.
- Migration yalnız yerel DB'ye uygulanır; canlı listesi ürün deposu `operations/CANLI-ISLETIM-KILAVUZU.md` §11.1.
