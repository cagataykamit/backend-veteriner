# Charge (ücret) API sözleşmesi — Dilim 1

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-012 (vetinity-product, kabul edildi), backlog FIN-001, keşif: `docs/FIN_DISCOVERY.md`. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme (inceleme bekliyor). Kapsam: ücret satırı **ekleme ve okuma**. Tahsilat ilişkilendirme/bakiye (Dilim 2–3) ve düzeltme/iptal uçları (Dilim 5) bu sözleşmede **yoktur**; `Charges.Correct` izni şimdiden tanımlanır, ucu sonra gelir.

> **Ek (ADR-036 Dilim 1):** `ChargeDto` → `lineKind` (`Service`/`Product`, varsayılan `Service`) ve `vatRate` (KDV %, null = klinik varsayılanı). `POST /charges` ve `PUT /charges/{id}` isteğe bağlı `tax: { lineKind, vatRate }` alır; ücrete çevirmede satır türü ürün türünden türer. Ayrıntı: `INVOICING_IDENTITY_API_CONTRACT.md` §5. Aşağıdaki "KDV modellenmez" notu bu ekle geçersizdir: `unitPrice` yine tek tutardır; KDV dahil/hariç yorumu belge dilimine (Dilim 4) bırakıldı.

---

## 1) Kavramlar

- **Charge**, müşteriye yazılan borç satırıdır: açıklama, miktar, birim fiyat, tutar. Tahsilattan (`Payment`) ayrıdır; hiçbir şeyi ödenmiş saymaz. Bakım durumunu (`Visit.careStatus`) etkilemez.
- **Müşteri zorunludur** (`clientId`). `visitId` ve `examinationId` **opsiyoneldir**; muayenesiz/gelişsiz ücret girilebilir.
- **Para birimi yalnızca TRY.** İstek para birimi taşımaz; yanıtta `currency: "TRY"` döner. Çoklu para birimi sonraki kapsamdır (FIN-006).
- **Tutar tek yerde hesaplanır** (domain): `amount = round(quantity × unitPrice, 2, AwayFromZero)`. İstemci tutar göndermez. KDV modellenmez; `unitPrice` tek tutardır.
- Ücret bu dilimde **değiştirilemez ve silinemez**; düzeltme/iptal Dilim 5'tedir (yetki + gerekçe + audit).

### `ChargeDto`

| Alan | Tip | Not |
|---|---|---|
| `id` | guid | |
| `clinicId` | guid | |
| `clientId` | guid | |
| `clientName` | string | |
| `petId` | guid? | `petId` verilmediyse Visit.in (yoksa Muayene.nin) hayvanı yazılır |
| `petName` | string? | |
| `visitId` | guid? | |
| `examinationId` | guid? | |
| `description` | string | 1–500 karakter |
| `quantity` | decimal | > 0, en fazla 3 ondalık, ≤ 100000 |
| `unitPrice` | decimal | > 0, en fazla 2 ondalık, ≤ 10000000 |
| `amount` | decimal | Hesaplanır; > 0 |
| `currency` | string | Her zaman `TRY` |
| `createdByUserId` | guid | |
| `createdAtUtc` | datetime (UTC) | Sunucu saatiyle atanır |

`tenantId` yanıtta döndürülmez.

---

## 2) Yetkiler

| İşlem | Policy | Rol bağları (varsayılan seed) |
|---|---|---|
| Ücret ekleme | `Charges.Create` | Admin, Owner, ClinicAdmin, Sekreter (**Veteriner verilmez**) |
| Okuma (detay, liste) | `Charges.Read` | Admin, Owner, ClinicAdmin, Sekreter, Veteriner |
| Düzeltme/iptal (Dilim 5) | `Charges.Correct` | Admin, Owner, ClinicAdmin |

- Klinik kapsamı mevcut ödeme/geliş kuralıyla aynıdır: JWT/header `clinic_id` ile istek `clinicId` uyumsuzsa `Charges.ClinicContextMismatch`; atanmamış klinik → `Clinics.AccessDenied`.
- Kiracı bağlamı yoksa `Tenants.ContextMissing`; pasif kiracı yazması `Tenants.TenantInactive` (403).

---

## 3) `POST /api/v1/charges`

`Charges.Create`. İstek:

```json
{
  "clinicId": "guid?",
  "clientId": "guid",
  "petId": "guid?",
  "visitId": "guid?",
  "examinationId": "guid?",
  "description": "Genel muayene",
  "quantity": 1,
  "unitPrice": 750.00,
  "requestId": "guid?"
}
```

Yanıt: yeni kayıt **201** + `Location`, gövde `ChargeCreateResultDto { created: true, charge: ChargeDto }`. Aynı `requestId` ile tekrar istek **200**, `created: false` ve ilk kaydı döndürür (çift tıklama/yeniden deneme ikinci ücret üretmez). `requestId` verilmezse tekrar koruması yoktur.

Kurallar:

- `clinicId`: aktif klinik bağlamı varsa o; yoksa istekteki değer zorunlu (`Charges.Validation`).
- `clientId` zorunlu ve kiracıya ait olmalı (`Clients.NotFound`). Boş/eksik → `Charges.Validation` (400).
- `petId` verilirse hayvan kiracıya ait (`Pets.NotFound`) ve **bu müşterinin** olmalı (`Charges.PetClientMismatch`).
- `visitId` verilirse: Visit kiracıya ait (`Visits.NotFound`), yanlış geliş değil (`Charges.VisitVoided`), aynı klinikte (`Charges.VisitClinicMismatch`), hayvanı bu müşterinin (`Charges.VisitClientMismatch`); `petId` de verildiyse Visit'in hayvanıyla aynı (`Charges.VisitPetMismatch`).
- `examinationId` verilirse: muayene kiracıya ait (`Examinations.NotFound`), aynı klinikte (`Charges.ExaminationClinicMismatch`), hayvanı bu müşterinin (`Charges.ExaminationClientMismatch`); `petId` verildiyse (veya Visit'ten türediyse) muayenenin hayvanıyla aynı (`Charges.ExaminationPetMismatch`); `petId` yoksa muayenenin hayvanı yazılır; `visitId` de verildiyse ve muayene başka bir Visit'e bağlıysa (`Examination.VisitId` dolu ve farklı) `Charges.ExaminationVisitMismatch`. Visit'e bağlı olmayan (eski) muayene kabul edilir.
- `quantity`, `unitPrice`, `description` sınırları yukarıdaki tabloya göre; ihlal `Charges.Validation` (400). Tutar sıfıra yuvarlanıyorsa (`amount ≤ 0`) `Charges.Validation`.
- Başarılı ve başarısız her çağrı audit'e yazılır (`Charge.Create`, hedef `ClientId=…`).

## 4) `GET /api/v1/charges/{id}`

`Charges.Read`. `ChargeDto` döner. Kayıt yok, başka kiracıya ait veya atanmamış klinikte ise `Charges.NotFound` (404; varlık sızdırmaz).

## 5) `GET /api/v1/charges`

`Charges.Read`. Query: `page` (varsayılan 1), `pageSize` (1–200, varsayılan 20), `clinicId`, `clientId`, `petId`, `visitId`, `examinationId`. Sıralama: `createdAtUtc` azalan, `id` azalan. Yanıt `PagedResult<ChargeDto>`.

- Klinik kapsamı zorunludur: aktif klinik bağlamı veya `clinicId` yoksa `Charges.ClinicScopeRequired` (400) — kapsamsız kiracı geneli liste yoktur (ödeme listesiyle aynı kural).
- Atanmamış klinik → `Clinics.AccessDenied` (403).

---

## 6) Hata kodları

| Kod | HTTP |
|---|---|
| `Tenants.ContextMissing` / `Auth.UserContextMissing` | 400 / 401 |
| `Tenants.NotFound` | 404 |
| `Tenants.TenantInactive` | 403 |
| `Charges.Validation`, `Charges.ClinicScopeRequired` | 400 |
| `Charges.ClinicContextMismatch` | 400 |
| `Clinics.AccessDenied` | 403 |
| `Clinics.NotFound`, `Clients.NotFound`, `Pets.NotFound`, `Visits.NotFound`, `Examinations.NotFound`, `Charges.NotFound` | 404 |
| `Charges.PetClientMismatch`, `Charges.VisitVoided`, `Charges.VisitClinicMismatch`, `Charges.VisitClientMismatch`, `Charges.VisitPetMismatch`, `Charges.ExaminationClinicMismatch`, `Charges.ExaminationClientMismatch`, `Charges.ExaminationPetMismatch`, `Charges.ExaminationVisitMismatch` | 400 |

## 7) Uygulama notları (sözleşme dışı bilgi)

- Okuma/yazma Command DB'dedir. Query DB projeksiyonu, outbox olayı ve rapor entegrasyonu **yoktur** (bakiye Command DB'den okunacak; ADR-012, keşif madde 3).
- Veri modeli: `Charges` tablosu; `(TenantId, ClientId)`, `(TenantId, ClinicId, CreatedAtUtc)`, `(TenantId, VisitId)`, `(TenantId, ExaminationId)` indeksleri; `(TenantId, RequestId)` filtreli benzersiz indeks (`RequestId IS NOT NULL`). Tutar `decimal(18,2)`, miktar `decimal(18,3)`.
- Dilim 2 `Charge`'a ödenen tutar sayacı ve eşzamanlılık belirteci ekler (migration o dilimde).
