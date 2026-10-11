# Müşteri bakiyesi API sözleşmesi — Dilim 3

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-012 (kabul edildi), backlog FIN-003; ilgili: `CHARGES_API_CONTRACT.md` (Dilim 1), `PAYMENT_ALLOCATION_API_CONTRACT.md` (Dilim 2), `FIN_DISCOVERY.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme (uygulandı: bakiye ucu ve Bugün alanları). Salt okuma; **migration yok**, yeni izin yok.

---

## 1) Kavramlar

- **Bakiye müşteri (sahip) bazındadır ve klinik kapsamlıdır.** Üç tutar ayrı verilir: `chargedAmount` (ücret toplamı), `allocatedAmount` (ücretlere tahsis edilen), `outstandingAmount` (açık kalan = ücret − tahsis).
- **Kaynak mevcut sayaçlardır:** `Charge.Amount`, `Charge.AllocatedAmount`, `Charge.OutstandingAmount`. Bakiye için tahsis satırları yeniden toplanmaz; `Σ PaymentAllocations` ile sayaç eşitliği entegrasyon testinde doğrulanır (Dilim 2 değişmezi).
- **Yalnızca TRY.** Ücretler yalnızca TRY'dir; yanıtta `currency: "TRY"` döner.
- **Bağlanmamış (eski) ödemeler hayali borç üretmez ve bakiyeden düşülmez.** Tahsissiz kalan eski ödemeler `unallocatedPayments` altında **ayrı satır** olarak görünür (FIN-007 bunları ücretlere bağlayacak). `outstandingAmount` bu tutarla **mahsup edilmez**; net bakiye alanı yoktur.
- Müşterinin ücreti yoksa tüm tutarlar 0, `pets` boş döner (404 değil).
- Bakım durumu (`Visit.careStatus`) bakiyeyi etkilemez, bakiye bakım durumunu etkilemez.

## 2) Yetki ve kapsam

- `GET /api/v1/clients/{id}/balance` — policy **`Charges.Read`** (Admin, Owner, ClinicAdmin, Sekreter, Veteriner). Gerekçe: yanıt ücret/borç verisidir; `Clients.Read` ücret görme hakkı vermemeli, ayrıca `Charges.Read` zaten ücret listesini aynı kişilere açar. Yeni izin yoktur.
- Klinik kapsamı `payment-summary` ile aynı çözümleyiciyi kullanır:
  - Aktif klinik bağlamı (JWT/header `clinic_id`) varsa yalnızca o kliniğin ücret ve ödemeleri sayılır.
  - Bağlam yoksa kullanıcının erişebildiği tüm klinikler (Admin/Owner: kiracı geneli) sayılır; `clinicId` yanıtta `null` döner.
  - Atanmış klinik yoksa tüm tutarlar 0 ve listeler boş döner.
- Kiracıya ait olmayan müşteri → `Clients.NotFound` (404). Kiracı bağlamı yok → `Tenants.ContextMissing` (400). Diğer kiracının verisi görünmez.
- Okuma Command DB'dedir; Query DB bayrakları bu uç için geçerli değildir (para verisi eventual-consistent projeksiyondan okunmaz).

## 3) `GET /api/v1/clients/{id}/balance`

### `ClientBalanceDto`

| Alan | Tip | Not |
|---|---|---|
| `clientId` | guid | |
| `clientName` | string | |
| `clinicId` | guid? | Aktif klinik bağlamı varsa o klinik; yoksa `null` (erişilebilir tüm klinikler) |
| `currency` | string | Her zaman `TRY` |
| `chargedAmount` | decimal | Kapsamdaki tüm ücretlerin `amount` toplamı |
| `allocatedAmount` | decimal | Ücretlere tahsis edilen toplam |
| `outstandingAmount` | decimal | `chargedAmount − allocatedAmount` (≥ 0) |
| `openChargeCount` | int | Açık tutarı > 0 olan ücret sayısı |
| `pets` | `ClientBalancePetDto[]` | Yalnızca **açık** ücretler; hayvan bazında. Açık tutara göre azalan, sonra hayvan adı |
| `unallocatedPayments` | `ClientBalanceUnallocatedPaymentsDto` | Eski ödemeler (FIN-007); bakiyeden ayrı |

### `ClientBalancePetDto`

| Alan | Tip | Not |
|---|---|---|
| `petId` | guid? | Hayvansız ücretlerde `null` |
| `petName` | string? | |
| `outstandingAmount` | decimal | Bu hayvanın açık toplamı |
| `visits` | `ClientBalanceVisitDto[]` | Ziyaret bazında; ziyareti olmayanlar tek `visitId = null` grubunda |

### `ClientBalanceVisitDto`

| Alan | Tip | Not |
|---|---|---|
| `visitId` | guid? | Ziyaretsiz ücretler için `null` |
| `visitArrivedAtUtc` | datetime? | Visit'in geliş zamanı; `visitId` null ise null |
| `outstandingAmount` | decimal | Bu ziyaretin açık toplamı |
| `charges` | `ClientBalanceChargeDto[]` | `createdAtUtc` artan, `id` |

### `ClientBalanceChargeDto`

`chargeId`, `clinicId`, `examinationId?`, `description`, `amount`, `allocatedAmount`, `outstandingAmount` (> 0), `createdAtUtc`.

Gruplama: ücretin `petId`'si (Dilim 1 kuralıyla Visit/Muayene'den türetilmiş olabilir) ve `visitId`'si kullanılır. Ziyaretler kendi içinde `visitArrivedAtUtc` azalan (ziyaretsiz grup sonda).

### `ClientBalanceUnallocatedPaymentsDto`

| Alan | Tip | Not |
|---|---|---|
| `totalAmount` | decimal | Tahsissiz kalan **TRY** ödemelerin `unallocatedAmount` toplamı |
| `paymentCount` | int | Tahsissiz tutarı > 0 olan TRY ödeme sayısı |
| `hasOtherCurrency` | bool | Tahsissiz, TRY olmayan eski ödeme var mı (toplama **katılmaz**, para birimleri karıştırılmaz) |
| `items` | `ClientBalanceUnallocatedPaymentDto[]` | En yeni 50 ödeme (`paidAtUtc` azalan): `paymentId`, `clinicId`, `paidAtUtc`, `method`, `amount`, `unallocatedAmount`, `currency`, `notes` |

### Örnek

1.000 TL ücret, 400 TL tahsilat → `chargedAmount 1000`, `allocatedAmount 400`, `outstandingAmount 600`, `openChargeCount 1`; ikinci 600 TL sonrası `outstandingAmount 0`, `pets` boş. Tahsissiz 120 TL eski ödeme: `unallocatedPayments.totalAmount 120`, `outstandingAmount` değişmez.

## 4) Hata kodları

| Kod | HTTP |
|---|---|
| `Tenants.ContextMissing` | 400 |
| `Auth.Unauthorized.UserContextMissing` | 401 |
| `Clients.NotFound` | 404 |
| `Clinics.AccessDenied` | 403 |
| `Clinics.NotFound` | 404 |

`Clinics.*` kodları `clinic_id` bağlamı atanmamış/yanlış kliniği işaret ederse çözümleyiciden gelir (`payment-summary` ile aynı). Yetkisiz kullanıcı 403.

## 5) Uygulama notları

- Tek ucun handler'ı `Clients/Queries/GetBalance` altındadır; kural tek yerde: tutarlar `Charge` sayaçlarından okunur, handler toplar ve gruplar, hesap kuralı eklemez.
- Satırlar tek sorguyla (kiracı + müşteri + klinik kapsamı) okunur; müşteri başına ücret sayısı sınırlı varsayılır. Büyüyen müşteri için sayfalama sonraki iştir.
- Sıralama deterministiktir (id ile bağlanır).
- Eşzamanlılık: okuma anlık görüntüdür; tahsis sırasında açık tutar değişirse istemci yeniden okur (`Payments.ConcurrencyConflict` yönergesi Dilim 2'dedir).

## 6) Test odağı

Domain/uygulama: toplamlar, hayvan/ziyaret gruplama, ziyaretsiz/hayvansız ücret, kapalı ücretin listeden çıkması, eski ödemenin borç üretmemesi, TRY dışı eski ödeme, klinik kapsamı (tek/çoklu/boş), müşteri yok. Entegrasyon (LocalDB): 1000/400/600 kabul senaryosu, `Σ PaymentAllocations` = sayaç doğrulaması, kiracı ve klinik izolasyonu, `Charges.Read` olmadan 403, Veteriner okuyabilir.

## 7) Bugün satırında ücret/ödenen/kalan (Dilim 4 hazırlığı; kullanıcı kararlarıyla kesinleşti)

`GET /api/v1/visits/today` yanıtındaki her `TodayItemDto` satırına dört **eklenen** alan gelir; `paymentIndicator` ve diğer alanlar aynen kalır (geriye uyumlu, kırıcı değişiklik yok). Yetki politikası değişmez (`Visits.Read`); tutarlar ayrıca `Charges.Read` ister.

| Alan | Tip | Anlam |
|---|---|---|
| `chargedAmount` | decimal? | Bu **ziyarete** (`visitId`) bağlı ücretlerin `amount` toplamı |
| `allocatedAmount` | decimal? | Bu ziyaretin ücretlerine tahsis edilen toplam |
| `outstandingAmount` | decimal? | `chargedAmount − allocatedAmount` (ziyaret bazında kalan) |
| `clientOutstandingAmount` | decimal? | Satırdaki **müşterinin** bu klinikteki toplam açık borcu (ziyaretten bağımsız; muayenesiz/gelişsiz ücretler dahil) |

Kurallar:

- **`null` ne zaman:** kullanıcıda `Charges.Read` yoksa dört alan da `null`. Ziyaretsiz (yalnız randevulu, `visitId = null`) satırda ziyaret alanları (`chargedAmount`, `allocatedAmount`, `outstandingAmount`) `null`; `clientOutstandingAmount` yine dolu. `Charges.Read` varsa ve ziyaretin ücreti yoksa ziyaret alanları `0`'dır (null değil): "ücret yazılmamış" ile "yetkisiz" ayrılır.
- Kapsam Bugün'ün kliniğidir (tek klinik). `clientOutstandingAmount` yalnızca o klinikteki ücretleri sayar; `GET /clients/{id}/balance` bağlam yoksa erişilebilir tüm klinikleri toplar, bu yüzden iki değer farklı olabilir. Yalnızca TRY. Diğer kiracı ve klinikler görünmez.
- Bağlanmamış (eski) ödemeler bu alanları etkilemez; `outstandingAmount` ücret sayaçlarından gelir (bölüm 1).
- Yanlış işaretli (`isVoided`) gelişlerde de ziyaret tutarları döner; ücret yine borçtur (ücret iptali Dilim 5).
- Hesap `Charge` sayaçlarından yapılır. Tek `Charges` sorgusu satırlardaki müşteri kümesiyle (`ClientId IN …`, en çok 500 satır) çekilir; ziyaret toplamı aynı satırlardan `VisitId` ile ayrılır (N+1 yok).
- `paymentIndicator` hâlâ "ödeme kaydı var/yok" bilgisidir ve bu alanlardan türetilmez.
- Müşteri listesinde (`GET /clients`) borç alanı veya "borcu olanlar" filtresi **bu sözleşmede yoktur** (kullanıcı kararı).
