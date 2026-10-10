# Tahsilat–ücret ilişkilendirme (PaymentAllocation) API sözleşmesi — Dilim 2

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-012 (kabul edildi), backlog FIN-002; ilgili: `CHARGES_API_CONTRACT.md` (Dilim 1), `FIN_DISCOVERY.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme, **uygulama onayı bekliyor** (kod yok). Kapsam: tahsilatı ücrete bağlama, fazla ödeme reddi, çift istek ve eşzamanlılık koruması, `PUT /payments/{id}` koruması. Bakiye ucu (Dilim 3), iptal/iade/düzeltme (Dilim 5), eski ödemeyi sonradan bağlama (FIN-007), kredi/mahsup (FIN-005) ve çoklu para birimi (FIN-006) **bu sözleşmede yoktur**.

---

## 1) Kavramlar ve model

- **Tahsilat (`Payment`)** alınan paradır ve değişmez bir olgudur; ücrete bağlanması onu çoğaltmaz. **Tahsis (`PaymentAllocation`)**, bir tahsilatın hangi ücretin ne kadarını karşıladığını açık kaydeder. Aynı para iki borcu kapatamaz: bir tahsilatın tahsisleri toplamı tutarını, bir ücretin tahsisleri toplamı tutarını aşamaz.
- **Kalan (açık) tutar:** `charge.outstandingAmount = charge.amount − charge.allocatedAmount`. **Bağlanmamış tahsilat:** `payment.unallocatedAmount = payment.amount − payment.allocatedAmount`.
- **Tahsis yalnızca tahsilat oluşturulurken** yapılır (tek komut, tek işlem). Sonradan bağlama ucu yoktur (eski ödemeler için FIN-007).
- **Eski ödemeler** (bu dilimden önce kayıtlı) ve tahsissiz oluşturulan yeni ödemeler `allocatedAmount = 0` ile "bağlanmamış" kalır; hayali borç türetilmez. Bakiyede ayrı satırdır (Dilim 3).
- Yalnızca TRY. Tahsis için `payment.currency == charge.currency` (şimdilik hep TRY).

### Veri modeli (migration uygulamada; bu dilim için)

| Tablo | Değişiklik |
|---|---|
| `PaymentAllocations` (yeni) | `Id`, `TenantId`, `ClinicId`, `PaymentId`, `ChargeId`, `Amount decimal(18,2)`, `CreatedByUserId`, `CreatedAtUtc`. FK `PaymentId→Payments`, `ChargeId→Charges` (silme kısıtlı, cascade yok). `CHECK (Amount > 0)`. İndeks `(TenantId, PaymentId)`, `(TenantId, ChargeId)`. Aynı çift için birden çok satır mümkündür (benzersiz değil; tekrar koruması `requestId` iledir) |
| `Payments` | `AllocatedAmount decimal(18,2) NOT NULL DEFAULT 0`, `MutationSequence bigint NOT NULL DEFAULT 0` (concurrency token, Visit deseni), `RequestId uniqueidentifier NULL`, `RequestHash nvarchar(64) NULL`. `CHECK (AllocatedAmount >= 0 AND AllocatedAmount <= Amount)`. Filtreli benzersiz indeks `(TenantId, RequestId) WHERE RequestId IS NOT NULL` |
| `Charges` | `AllocatedAmount decimal(18,2) NOT NULL DEFAULT 0`, `MutationSequence bigint NOT NULL DEFAULT 0` (concurrency token). `CHECK (AllocatedAmount >= 0 AND AllocatedAmount <= Amount)` |

- Sayaçlar (`AllocatedAmount`) tek yazma yolundan (domain metotları `Payment.ApplyAllocation`, `Charge.ApplyAllocation`) güncellenir; `Σ PaymentAllocations.Amount` ile sayaç eşitliği testle ve sağlık sorgusuyla doğrulanır. Müşteri/klinik eşleşmesi (ödeme ve ücret aynı müşteri ve klinik) CHECK ile ifade edilemez; domain/handler kuralıdır, entegrasyon testiyle korunur.
- Mevcut satırlar için tüm yeni kolonlar varsayılan değerle dolar; veri taşıma yoktur.

---

## 2) `POST /api/v1/payments` (mevcut uç genişler)

`Payments.Create`. Mevcut alanlar aynen kalır; **eklenenler:**

```json
{
  "clinicId": "guid", "clientId": "guid", "petId": "guid?", "appointmentId": "guid?", "examinationId": "guid?",
  "amount": 600.00, "currency": "TRY", "method": 0, "paidAtUtc": "…", "notes": "…",
  "requestId": "guid",
  "allocations": [ { "chargeId": "guid", "amount": 600.00 } ]
}
```

- **`requestId` zorunludur** (çift istek koruması). Eksik/boş → `Payments.RequestIdRequired` (400). Bu, mevcut istemciler için **kırıcı** bir değişikliktir; frontend önce uyarlanmalıdır.
- **`allocations` opsiyoneldir.** Verilmezse tahsilat bağlanmamış kaydedilir (bugünkü davranış + `requestId`). Verilirse:
  - Boş olmayan dizi; `chargeId`'ler tekil (`Payments.Validation`); her `amount` > 0, en fazla 2 ondalık.
  - **`Σ allocations.amount == amount`** olmalı; tahsilat ücret(ler)in açık tutarından fazla olamaz, bağlanmamış artık bırakılmaz (`Payments.AllocationSumMismatch`, 400). Yani fazla ödeme **reddedilir**; kullanıcı ödemeyi açık tutar kadar kaydeder.
  - Her ücret aynı kiracıda (`Charges.NotFound`), aynı klinikte (`PaymentAllocations.ChargeClinicMismatch`), aynı müşteride (`PaymentAllocations.ChargeClientMismatch`) olmalı.
  - Her tahsis, işlem anındaki açık tutarı aşamaz: `PaymentAllocations.ExceedsOutstanding` (409; yanıtın `extensions` alanında `chargeId` ve güncel `outstandingAmount`).
- Yeni ödemelerde `currency` yalnızca `TRY` (`Payments.Validation`). Eski kayıtlar etkilenmez.
- **Yanıt (geriye uyumlu):** 201, gövde bugünkü gibi yalnızca `Guid`, `Location` başlığı. Tekrar isteği 200, aynı `Guid` ve `Idempotency-Replayed: true` başlığı.
- Başarılı oluşturma bugünkü gibi `payment.created.v1` olayını üretir (tutar ve tarih değişmez; tahsis bilgisi olaya girmez).

### Çift istek (`requestId`)

| Durum | Sonuç |
|---|---|
| İlk istek | Ödeme + tahsisler tek işlemde yazılır; `RequestId` ve `RequestHash` (tutar, müşteri, klinik, yöntem, tarih, tahsis listesi üzerinden SHA-256) ödemeye yazılır |
| Aynı `requestId`, aynı içerik | 200, ilk ödemenin `Guid`'i; ikinci kayıt ve ikinci sayım yok |
| Aynı `requestId`, farklı içerik | 409 `Payments.DuplicateRequestMismatch` |
| Eşzamanlı aynı `requestId` | Filtreli benzersiz indeks kaybedene çarpar; kaybeden kazananı okuyup yukarıdaki kurala göre 200/409 döner |
| Başarısız istek (400/409) | Hiçbir şey yazılmaz; `requestId` tüketilmez, düzeltilmiş istekte aynı anahtar kullanılabilir |

### İki kullanıcı çakışması

`Payment` ve `Charge` `MutationSequence` ile iyimser eşzamanlılık kullanır. Aynı ücrete aynı anda iki tahsil gelirse biri kaydı yazar, diğeri `DbUpdateConcurrencyException` alır → **409 `Payments.ConcurrencyConflict`** (kaybedenin işlemi geri alınır). İstemci açık tutarı yeniden okuyup (aynı veya yeni `requestId` ile; hiçbir kayıt yazılmadığı için aynı anahtar güvenlidir) tekrar dener. Sunucu otomatik yeniden deneme yapmaz. Açık tutar bu arada düştüyse tekrar `PaymentAllocations.ExceedsOutstanding` döner. Son savunma `CHECK (AllocatedAmount <= Amount)` kısıtlarıdır.

---

## 3) `PUT /api/v1/payments/{id}` koruması

`Payments.Update` (değişmez). Tahsisi olan (`allocatedAmount > 0`) ödemede, bütünlük bozulmasın diye:

| Alan | Tahsisliyken |
|---|---|
| `clinicId`, `clientId`, `currency` | **Değiştirilemez** → `Payments.UpdateBlockedByAllocation` (409) |
| `amount` | **Değiştirilemez** (aynı değerle gönderim serbest) → `Payments.UpdateBlockedByAllocation` (409) |
| `method`, `paidAtUtc` (mevcut 7 gün/2 yıl penceresi), `notes`, `petId`, `appointmentId`, `examinationId` | Değiştirilebilir (doğrulamalar bugünkü gibi; `petId`/randevu/muayene hâlâ aynı müşteriye ait olmalı) |

- Tahsisi olmayan (eski/bağlanmamış) ödemelerde PUT bugünkü gibi çalışır; bu dilimde yeni kısıt yoktur. Tutar/müşteri düzeltmesinin yöneticiye, gerekçeye ve audit'e bağlanması **Dilim 5**tedir (`Payments.Correct`).
- `Payment` artık `MutationSequence` taşır: PUT, eşzamanlı tahsisle çakışırsa 409 `Payments.ConcurrencyConflict` döner; istemci yeniden okur.
- Başarılı PUT mevcut `payment.updated.v1` olayını üretir; tahsis alanları olaya girmez.

## 4) Okuma uçları (eklenen alanlar ve uçlar)

| Uç | Değişiklik | Yetki |
|---|---|---|
| `GET /payments/{id}` | `PaymentDetailDto`'ya `allocatedAmount`, `unallocatedAmount` | `Payments.Read` |
| `GET /payments` | `PaymentListItemDto`'ya `allocatedAmount` | `Payments.Read` |
| `GET /payments/{id}/allocations` | `[ { id, chargeId, chargeDescription, amount, createdAtUtc, createdByUserId } ]` | `Payments.Read` |
| `GET /charges/{id}` ve `GET /charges` | `ChargeDto`'ya `allocatedAmount`, `outstandingAmount` | `Charges.Read` |
| `GET /charges/{id}/allocations` | `[ { id, paymentId, paidAtUtc, method, amount, createdAtUtc, createdByUserId } ]` | `Charges.Read` |

Tümü Command DB'den okunur; klinik kapsamı mevcut ödeme/ücret kuralıyla aynıdır (kapsam dışı klinikte `*.NotFound`/403, varlık sızdırmaz). Eklenen alanlar yalnızca ekleme olduğundan mevcut yanıtlar geriye uyumludur. Query DB tabanlı yollar (bayrak açıkken liste/detay) bu alanları **döndürmez** (`null`/yok) — bkz. madde 6.

## 5) Yetkiler

Yeni izin **yoktur**. Tahsisli ödeme oluşturma `Payments.Create` ister (Admin, Owner, ClinicAdmin, Sekreter); Veteriner tahsilat alamaz. Sunucu ücretleri kendi okur, ayrıca `Charges.Read` aranmaz. Tahsis listeleri `Payments.Read` / `Charges.Read` ile okunur. `Payments.Correct` ve tahsis iptali Dilim 5'tedir.

## 6) Query DB, rapor ve dashboard etkisi

- **Tahsilat toplamları değişmez.** Dashboard finans, ödeme raporu/export, müşteri ödeme özeti, son ödemeler ve Bugün'ün `PaymentIndicator` alanı "alınan tahsilat"ı sayar; bir ödemenin bağlı olup olmaması bu toplamları etkilemez (aynı para iki kez sayılmaz, çünkü tahsis para yaratmaz). Bu yüzden mevcut projeksiyon, parity testleri ve bayraklar bu dilimde dokunulmadan kalır.
- **Tahsis ve sayaçlar Query DB'ye taşınmaz** (`payment.created.v1`/`updated.v1` değişmez; `PaymentReadModel` değişmez). Bakiye ve açık tutar yalnızca Command DB'den okunur (Dilim 3). Query DB yolu bayrakla açıkken liste/detay `allocatedAmount` içermez; bu bilinen bir sınırdır ve bakiye için bu yollar kullanılmaz.
- **İptal edilen / bağlanmamış ödeme (Dilim 5 için önceden bağlayıcı):** iptal edilen ödeme tahsilat toplamlarından ve Query DB'den düşer (yeni `payment.voided.v1`); tahsisleri geri alınır, ücretin açık tutarı artar. Bağlanmamış ödeme iptali yalnızca tahsilat toplamını etkiler. Bu dilim iptal getirmez.

## 7) Hata kodları

| Kod | HTTP |
|---|---|
| `Payments.RequestIdRequired`, `Payments.Validation`, `Payments.AllocationSumMismatch` | 400 |
| `PaymentAllocations.ChargeClinicMismatch`, `PaymentAllocations.ChargeClientMismatch` | 400 |
| `Charges.NotFound`, `Payments.NotFound` | 404 |
| `PaymentAllocations.ExceedsOutstanding`, `Payments.UpdateBlockedByAllocation` | 409 |
| `Payments.ConcurrencyConflict`, `Payments.DuplicateRequestMismatch` | 409 |
| Mevcut ödeme kodları (`Payments.PetClientMismatch`, `Payments.AppointmentClinicMismatch`, `Payments.PaidTooFarInPast` vb.) | değişmez |

Not: HTTP eşlemesi `ResultExtensions.MapStatusCode` içindedir. `…ConcurrencyConflict` ve `…Duplicate…` mevcut alt dizgelerle 409'a düşer; `ExceedsOutstanding` ve `UpdateBlockedByAllocation` için aynı yerde (tek yer) 409 satırı eklenir; `*Mismatch`/`*Required` mevcut varsayılanla 400'dür.

## 8) Uygulama notları ve test odağı

- Tek işlem: ödeme + tahsis satırları + iki sayaç + outbox olayı aynı `SaveChanges`. Kural tek yerde: domain metotları (`ApplyAllocation` sınır kontrolü), handler yalnızca yükler/doğrular.
- Test odağı: 1.000 TL ücret / 400 + 600 kabul senaryosu; fazla ödeme reddi (601); aynı `requestId` iki kez ve 8 eşzamanlı çağrı (tek ödeme, tek tahsis seti); aynı `requestId` farklı içerik (409); iki kullanıcı aynı ücrete aynı anda (biri 409, toplam ≤ ücret); `Σ allocations == sayaç`; PUT'ta kilitli alanlar ve serbest alanlar; müşteri/klinik uyuşmazlığı; tenant/klinik izolasyonu; yetki (Veteriner 403); eski (tahsissiz) ödemenin PUT ve tahsilat raporlarında değişmemesi; dashboard/rapor parity testlerinin yeşil kalması.
- Migration yerel DB'de uygulanmadan önce kullanıcı onayı gerekir.

## 9) Ürün kararı gerektirenler

1. **Tahsissiz ödeme sürsün mü?** Öneri: bu dilimde evet (geriye uyum; "bağlanmamış tahsilat"), frontend tahsisli akışa (FIN-004) geçince tahsisi zorunlu kılma ayrı karar. Aksi halde bugünkü her `POST /payments` istemcisi kırılır.
2. **`requestId` zorunlu ve kırıcı:** Öneri gövde alanı `requestId` (Charge ile aynı kavram). İstenirse standart `Idempotency-Key` başlığı da kabul edilebilir; yalnızca biri seçilmeli. Frontend aynı sürümde uyarlanmalı.
3. **Tahsilat = tahsis toplamı** (artık bırakılmaz) kuralı, karar 4'ün ("ödeme borç kadar kaydedilir") yorumudur; kısmi tahsis + bağlanmamış artık kredi olurdu (FIN-005, kapsam dışı).
4. **Yeni ödemeler yalnızca TRY.** Karar 3'ün ödemeye uygulanmasıdır; bugün herhangi bir ISO para birimi kabul ediliyor.
5. **Tahsissiz ödemede PUT serbest kalır** (Sekreter tutar/müşteri değiştirebilir) ta ki Dilim 5 yönetici-yetkisi + gerekçe + audit getirene kadar. Hemen sıkılaştırmak isteniyorsa Dilim 5'in `Payments.Correct` kısmı öne alınır.
