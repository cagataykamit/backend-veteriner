# Tahsilat–ücret ilişkilendirme (PaymentAllocation) API sözleşmesi — Dilim 2

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-012 (kabul edildi), backlog FIN-002; ilgili: `CHARGES_API_CONTRACT.md` (Dilim 1), `FIN_DISCOVERY.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme, kullanıcı kararlarıyla kesinleşti (uygulanıyor). Kapsam: tahsilatı ücrete bağlama, fazla ödeme reddi, çift istek ve eşzamanlılık koruması, tutar/müşteri/klinik/para birimi düzeltmesinin yönetici düzeyine alınması. Bakiye ucu (Dilim 3), ödeme/ücret iptali ve iade (Dilim 5), eski ödemeyi sonradan bağlama (FIN-007), avans/depozito/kredi (FIN-005, **ayrı "Kredi" kaydı**) ve çoklu para birimi (FIN-006) **bu sözleşmede yoktur**.

---

## 1) Kavramlar ve model

- **Tahsilat (`Payment`)** alınan paradır; **tahsis (`PaymentAllocation`)** onun hangi ücretin ne kadarını karşıladığını açık kaydeder. Aynı para iki borcu kapatamaz: bir tahsilatın tahsisleri toplamı tutarını, bir ücretin tahsisleri toplamı tutarını aşamaz.
- **Kalan (açık) tutar:** `charge.outstandingAmount = charge.amount − charge.allocatedAmount`. **Bağlanmamış tahsilat:** `payment.unallocatedAmount = payment.amount − payment.allocatedAmount` (yalnız eski ödemelerde sıfırdan büyüktür).
- **Her yeni ödeme en az bir ücrete tahsis edilmek zorundadır ve tahsis toplamı ödeme tutarına tam eşittir.** Tahsissiz/artıklı yeni ödeme yoktur; avans, depozito ve önceden ödeme ileride ayrı "Kredi" kaydıdır (FIN-005).
- Tahsis yalnızca tahsilat oluşturulurken, tek işlemde yapılır; sonradan bağlama ucu yoktur.
- **Eski ödemeler** (bu dilimden önce kayıtlı) `allocatedAmount = 0` ile "bağlanmamış" kalır; hayali borç türetilmez. Bakiyede ayrı satırdır (Dilim 3).
- Yalnızca TRY. Tahsis için `payment.currency == charge.currency`.

### Veri modeli (migration `AddPaymentAllocations`)

| Tablo | Değişiklik |
|---|---|
| `PaymentAllocations` (yeni) | `Id`, `TenantId`, `ClinicId`, `PaymentId`, `ChargeId`, `Amount decimal(18,2)`, `CreatedByUserId`, `CreatedAtUtc`. FK `PaymentId→Payments`, `ChargeId→Charges` (silme kısıtlı, cascade yok). `CHECK (Amount > 0)`. İndeks `(TenantId, PaymentId)`, `(TenantId, ChargeId)`. Aynı çift için birden çok satır mümkündür (tekrar koruması `requestId` iledir) |
| `Payments` | `AllocatedAmount decimal(18,2) NOT NULL DEFAULT 0`, `MutationSequence bigint NOT NULL DEFAULT 0` (concurrency token, Visit deseni), `RequestId uniqueidentifier NULL`, `RequestHash nvarchar(64) NULL`. `CHECK (AllocatedAmount >= 0 AND AllocatedAmount <= Amount)`. Filtreli benzersiz indeks `(TenantId, RequestId) WHERE RequestId IS NOT NULL` |
| `Charges` | `AllocatedAmount decimal(18,2) NOT NULL DEFAULT 0`, `MutationSequence bigint NOT NULL DEFAULT 0` (concurrency token). `CHECK (AllocatedAmount >= 0 AND AllocatedAmount <= Amount)` |

- Sayaçlar tek yazma yolundan (`Payment.ApplyAllocation`, `Charge.ApplyAllocation`) güncellenir; `Σ PaymentAllocations.Amount` ile sayaç eşitliği testle doğrulanır. Müşteri/klinik eşleşmesi CHECK ile ifade edilemez; domain kuralıdır, entegrasyon testiyle korunur.
- Mevcut satırlar için yeni kolonlar varsayılan değerle dolar; veri taşıma yoktur.

---

## 2) `POST /api/v1/payments` (mevcut uç genişler; kırıcı değişiklik)

`Payments.Create`. Mevcut alanlar aynen kalır; **eklenenler zorunludur:**

```json
{
  "clinicId": "guid", "clientId": "guid", "petId": "guid?", "appointmentId": "guid?", "examinationId": "guid?",
  "amount": 600.00, "currency": "TRY", "method": 0, "paidAtUtc": "…", "notes": "…",
  "requestId": "guid",
  "allocations": [ { "chargeId": "guid", "amount": 600.00 } ]
}
```

- **`requestId` zorunludur** (çift istek koruması). Eksik/boş → `Payments.RequestIdRequired` (400).
- **`allocations` zorunludur** ve boş olamaz → `Payments.AllocationsRequired` (400). Her satır: `chargeId` tekil (`Payments.Validation`), `amount` > 0 ve en fazla 2 ondalık.
- **`Σ allocations.amount == amount`**; fazla ödeme reddedilir, artık bırakılmaz → `Payments.AllocationSumMismatch` (400). Kullanıcı ödemeyi açık tutar kadar kaydeder.
- Her ücret aynı kiracıda (`Charges.NotFound`), aynı klinikte (`PaymentAllocations.ChargeClinicMismatch`), aynı müşteride (`PaymentAllocations.ChargeClientMismatch`) olmalı.
- Her tahsis işlem anındaki açık tutarı aşamaz → `PaymentAllocations.ExceedsOutstanding` (409; ilgili ücret ve açık tutar `detail` metnindedir).
- `currency` yalnızca `TRY` (`Payments.Validation`). Eski kayıtlar etkilenmez.
- **Hayvan / muayene bağlamı (türetme):** `petId` veya `examinationId` istekte verilmezse, tahsis edilen ücretlerin **tamamı aynı** `petId` (`examinationId`) değerini taşıyorsa ödeme o değerle kaydedilir; ücretler farklı ya da bağlamsızsa (ör. çoklu hayvan, hayvansız ücret) alan `null` kalır, tek hayvana indirgenmez. Açıkça verilen değer her zaman önceliklidir. Böylece `GET /payments` (liste, `petId` süzgeci, `petName`), `GET /payments/{id}` ve hayvan detayındaki "Son ödemeler" tahsis edilen ücretin hayvanını/muayenesini gösterir. Şema ve yanıt biçimi değişmez; bu davranıştan önce kaydedilmiş ödemeler etkilenmez.
- **Yanıt (geriye uyumlu):** 201, gövde yalnızca `Guid`, `Location` başlığı. Tekrar isteği 200, aynı `Guid` ve `Idempotency-Replayed: true` başlığı.
- Başarılı oluşturma bugünkü gibi `payment.created.v1` olayını üretir (tahsis bilgisi olaya girmez). Komut tek veritabanı işleminde çalışır; herhangi bir adım başarısız olursa hiçbir şey yazılmaz.

### Çift istek (`requestId`)

| Durum | Sonuç |
|---|---|
| İlk istek | Ödeme + tahsisler tek işlemde yazılır; `RequestId` ve `RequestHash` (klinik, müşteri, hayvan, randevu, muayene, tutar, para birimi, yöntem, tarih, not ve tahsis listesi üzerinden SHA-256) ödemeye yazılır |
| Aynı `requestId`, aynı içerik | 200, ilk ödemenin `Guid`'i; ikinci kayıt ve ikinci sayım yok |
| Aynı `requestId`, farklı içerik | 409 `Payments.DuplicateRequestMismatch` |
| Eşzamanlı aynı `requestId` | Filtreli benzersiz indeks kaybedene çarpar; kaybeden kazananı okuyup yukarıdaki kurala göre 200/409 döner |
| Başarısız istek (400/409) | Hiçbir şey yazılmaz; `requestId` tüketilmez, düzeltilmiş istekte aynı anahtar kullanılabilir |

### İki kullanıcı çakışması

`Payment` ve `Charge` `MutationSequence` ile iyimser eşzamanlılık kullanır. Aynı ücrete aynı anda iki tahsil gelirse biri kaydı yazar, diğeri `DbUpdateConcurrencyException` alır → **409 `Payments.ConcurrencyConflict`** (kaybedenin işlemi geri alınır). İstemci açık tutarı yeniden okuyup tekrar dener; hiçbir kayıt yazılmadığı için aynı `requestId` güvenlidir. Sunucu otomatik yeniden deneme yapmaz. Açık tutar bu arada düştüyse tekrar `PaymentAllocations.ExceedsOutstanding` döner. Son savunma `CHECK (AllocatedAmount <= Amount)` kısıtlarıdır.

---

## 3) `PUT /api/v1/payments/{id}` ve düzeltme yetkisi

`Payments.Update` (değişmez) yöntem, ödeme zamanı, not ve ilişkili hayvan/randevu/muayene gibi **sıradan** alanlar içindir. **Düzeltme alanları** (`amount`, `clientId`, `clinicId`, `currency`) mevcut değerden farklı gönderilirse bu bir **düzeltme**dir:

| Kural | Sonuç |
|---|---|
| Kullanıcıda `Payments.Correct` yok | 403 `Payments.CorrectionForbidden` (Sekreter ve Veteriner yapamaz) |
| Gövdede `reason` yok / 5–500 karakter dışı | 400 `Payments.Validation` |
| Ödemenin tahsisi var (`allocatedAmount > 0`) | 409 `Payments.UpdateBlockedByAllocation` (yetkili olsa da) |
| Aksi halde | Güncellenir; `reason` ile birlikte audit'e yazılır |

- Düzeltme alanları aynı değerle gönderilirse (mevcut istemci tam gövde yollar) düzeltme sayılmaz; `Payments.Update` yeterlidir ve `reason` gerekmez.
- Gövdeye opsiyonel `reason` eklenir. Her `PUT` audit'e yazılır (`Payment.Update`, hedef `PaymentId=…`); düzeltmede gerekçe payload'dadır.
- Tahsisli ödemede sıradan alanlar değiştirilebilir (doğrulamalar bugünkü gibi; hayvan/randevu/muayene aynı müşteriye ait kalmalı).
- `Payment` `MutationSequence` taşır: eşzamanlı tahsis/güncelleme çakışırsa 409 `Payments.ConcurrencyConflict`; istemci yeniden okur.
- Başarılı PUT mevcut `payment.updated.v1` olayını üretir; tahsis alanları olaya girmez.

## 4) Okuma uçları (eklenen alanlar ve uçlar)

| Uç | Değişiklik | Yetki |
|---|---|---|
| `GET /payments/{id}` | `PaymentDetailDto`'ya `allocatedAmount`, `unallocatedAmount` (Query DB yolunda `null`) | `Payments.Read` |
| `GET /payments/{id}/allocations` | `[ { id, chargeId, chargeDescription, amount, createdAtUtc, createdByUserId } ]` | `Payments.Read` |
| `GET /charges/{id}` ve `GET /charges` | `ChargeDto`'ya `allocatedAmount`, `outstandingAmount` | `Charges.Read` |
| `GET /charges/{id}/allocations` | `[ { id, paymentId, paidAtUtc, method, amount, createdAtUtc, createdByUserId } ]` | `Charges.Read` |

Command DB'den okunur; klinik kapsamı mevcut ödeme/ücret kuralıyla aynıdır (kapsam dışı klinikte 404, varlık sızdırmaz). Alanlar eklemedir; ödeme listesi (`GET /payments`) bu dilimde değişmez.

## 5) Yetkiler

Yeni izin: **`Payments.Correct`** (Admin, Owner, ClinicAdmin; Sekreter ve Veteriner'de yok). Tahsisli ödeme oluşturma `Payments.Create` ister (Admin, Owner, ClinicAdmin, Sekreter); Veteriner tahsilat alamaz. Sunucu ücretleri kendi okur, ayrıca `Charges.Read` aranmaz. Tahsis listeleri `Payments.Read` / `Charges.Read` ile okunur.

## 6) Query DB, rapor ve dashboard etkisi

- **Tahsilat toplamları değişmez.** Dashboard finans, ödeme raporu/export, müşteri ödeme özeti, son ödemeler ve Bugün'ün `PaymentIndicator` alanı "alınan tahsilat"ı sayar; tahsis para yaratmaz, bu yüzden mevcut projeksiyon, parity testleri ve bayraklar bu dilimde dokunulmadan kalır.
- **Tahsis ve sayaçlar Query DB'ye taşınmaz** (`payment.created.v1`/`updated.v1` değişmez; `PaymentReadModel` değişmez). Bakiye ve açık tutar yalnızca Command DB'den okunur (Dilim 3). Query DB yolu bayrakla açıkken detay `allocatedAmount` içermez; bu bilinen bir sınırdır.
- **İptal edilen ödeme (Dilim 5 için önceden bağlayıcı):** iptal edilen ödeme tahsilat toplamlarından ve Query DB'den düşer (`payment.voided.v1`); tahsisleri geri alınır, ücretin açık tutarı artar. Eski (bağlanmamış) ödeme iptali yalnızca tahsilat toplamını etkiler. Bu dilim iptal getirmez.

## 7) Hata kodları

| Kod | HTTP |
|---|---|
| `Payments.RequestIdRequired`, `Payments.AllocationsRequired`, `Payments.AllocationSumMismatch`, `Payments.Validation` | 400 |
| `PaymentAllocations.ChargeClinicMismatch`, `PaymentAllocations.ChargeClientMismatch` | 400 |
| `Payments.CorrectionForbidden` | 403 |
| `Charges.NotFound`, `Payments.NotFound` | 404 |
| `PaymentAllocations.ExceedsOutstanding`, `Payments.UpdateBlockedByAllocation` | 409 |
| `Payments.ConcurrencyConflict`, `Payments.DuplicateRequestMismatch` | 409 |
| Mevcut ödeme kodları (`Payments.PetClientMismatch`, `Payments.AppointmentClinicMismatch`, `Payments.PaidTooFarInPast` vb.) | değişmez |

HTTP eşlemesi `ResultExtensions.MapStatusCode` içindedir (tek yer). `…ConcurrencyConflict`, `…Duplicate…` ve `…Forbidden` mevcut alt dizgelerle eşlenir; `ExceedsOutstanding` ve `UpdateBlockedByAllocation` için 409 satırı eklenir; `*Mismatch`/`*Required` varsayılanla 400'dür.

## 8) Uygulama notları ve test odağı

- Tek işlem (`ITransactionalRequest` + hata durumunda geri alma): ödeme + tahsis satırları + iki sayaç + outbox olayı. Kural tek yerde: `Payment.AllocateFully` (tahsis kuralları) ve `Charge.ApplyAllocation` / `Payment.ApplyAllocation` (sınır kontrolü); handler yalnızca yükler ve doğrular.
- Test odağı: 1.000 TL ücret / 400 + 600 kabul senaryosu; fazla ödeme reddi (601); toplam eşitliği; aynı `requestId` iki kez ve eşzamanlı çağrılar; aynı `requestId` farklı içerik (409); iki kullanıcı aynı ücrete aynı anda (biri 409, toplam ≤ ücret); `Σ allocations == sayaç`; DB `CHECK`; PUT'ta düzeltme yetkisi/gerekçe/tahsis kilidi; müşteri/klinik uyuşmazlığı; tenant/klinik izolasyonu; Veteriner 403; dashboard/rapor parity testlerinin yeşil kalması.
- Migration yerel DB'de uygulanmadan önce kullanıcı onayı gerekir.

## 9) Kullanıcı kararları (kesinleşti)

1. Tahsissiz yeni ödeme yok; avans/depozito/önceden ödeme ileride ayrı "Kredi" kaydı (FIN-005). `POST /payments` kırıcı değişiklik kabul edildi (ürün canlıda değil).
2. `requestId` zorunlu gövde alanı (Charge ile aynı).
3. Tutar/müşteri/klinik/para birimi düzeltmesi yalnız yönetici düzeyi (`Payments.Correct`), gerekçe zorunlu, audit'li; tahsisli ödemede tutar/müşteri değişikliği 409. Yöntem/tarih/not için `Payments.Update` yeterli.
4. Tahsis toplamı ödeme tutarına tam eşit; yeni ödemeler yalnızca TRY.
