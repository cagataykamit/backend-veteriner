# Fatura kimliği ve vergi alanları API sözleşmesi (ADR-036 Dilim 1)

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-036 (vetinity-product, kabul edildi) — Dilim 1 "veri modeli", mimari ilke 8 (kişisel veri). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme + uygulama (dal `feat/hbs-belge-d1-veri-modeli`). Migration `AddInvoicingIdentityFields` yazıldı, veritabanına uygulanmadı.
> **Kapsam dışı (sonraki dilimler):** belge kesme, entegratör sağlayıcısı/ortam/şifreli kimlik bilgisi ve "bağlantıyı sına" (Dilim 3), belge bağlantısı (Dilim 3), "belge kesilirken eksik alanı iste" akışı (Dilim 4), belge türü kuralı (Dilim 3–4), ürün kataloğunda KDV varsayılanı (FIN-008).

---

## 1) Kurallar tek yerde (domain)

| Kural | Yer |
|---|---|
| TCKN doğrulama: 11 hane, ilk hane 0 değil, 10. hane = `((d1+d3+d5+d7+d9)×7 − (d2+d4+d6+d8)) mod 10`, 11. hane = `(d1+…+d10) mod 10` | `Domain/Shared/TurkishTaxIdentity.IsValidNationalId` |
| VKN doğrulama: 10 hane; `i = 1..9` için `t = (dᵢ + 10 − i) mod 10`, `t = 9` ise `9`, değilse `(t × 2^(10−i)) mod 9` toplanır; kontrol hanesi `(10 − toplam mod 10) mod 10` | `TurkishTaxIdentity.IsValidTaxNumber` |
| Maskeleme: ilk 3 ve son 2 hane görünür, aradakiler `*` (`12345678950` → `123******50`, `1234567890` → `123*****90`); 5 karakter ve altı tamamen `*` | `TurkishTaxIdentity.Mask` |
| Oran (KDV, stopaj): yüzde, `0–100`, en çok 2 ondalık; varsayılan KDV **%20** | `Domain/Shared/TaxRatePercent` |
| Ücret satır türü ürün türünden: katalog `Service` → `Service`, diğer tüm ürün türleri → `Product` | `ChargeLineKinds.FromProductKind` |

Girdi: TCKN/VKN alanlarında baştaki/sondaki boşluk kırpılır; yalnız rakam kabul edilir (ara boşluk, tire yok).

---

## 2) Müşteri (`Client`) fatura kimliği

Tüm alanlar **isteğe bağlı** (az zorunlu alan ilkesi); belge kesilirken eksikse istenmesi Dilim 4'tür.

### Alanlar

| Alan | Tip | Kural |
|---|---|---|
| `personType` | `"Individual"` \| `"Corporate"` \| null | null = belirtilmemiş |
| `nationalId` | string? (11) | Yalnız `Individual`; doluysa geçerli TCKN |
| `taxNumber` | string? (10) | Yalnız `Corporate`; doluysa geçerli VKN |
| `taxOffice` | string? (≤100) | Yalnız `Corporate` |
| `legalName` | string? (≤300) | Unvan; yalnız `Corporate` |
| `billingProvince` | string? (≤80) | İl; kişi türünden bağımsız |
| `billingDistrict` | string? (≤80) | İlçe |
| `billingAddressLine` | string? (≤500) | Adres satırı |

Tutarlılık (ihlal `Clients.Validation`, 400): kişi türü yokken TCKN/VKN/vergi dairesi/unvan verilemez; `Individual` iken VKN/vergi dairesi/unvan verilemez; `Corporate` iken TCKN verilemez. İstemci, kişi türü değişince gizlenen alanları boşaltıp gönderir. Boş/whitespace değer `null` saklanır.

Şahıs şirketi (VKN yerine TCKN kullanan kurumsal alıcı) bu dilimde desteklenmez: `Corporate` yalnız 10 haneli VKN alır (açık soru, rapora bakınız).

### Yazma — `POST /clients` ve `PUT /clients/{id}`

Gövdeye isteğe bağlı iç içe nesne eklenir:

```json
{
  "fullName": "Ayşe Yılmaz",
  "invoicingIdentity": {
    "personType": "Individual",
    "nationalId": "10000000146",
    "taxNumber": null,
    "taxOffice": null,
    "legalName": null,
    "billingProvince": "İstanbul",
    "billingDistrict": "Kadıköy",
    "billingAddressLine": "Moda Cad. No: 1"
  }
}
```

- `invoicingIdentity` **verilmezse (null) mevcut değerlere dokunulmaz** (alanı bilmeyen eski istemci veriyi silmesin). Verilirse **nesnenin tamamı** yazılır (tam değiştirme); temizlemek için tüm alanları null gönderin.
- Doğrulama hataları FluentValidation biçiminde alan yolu `InvoicingIdentity.NationalId` vb. ile döner (`Validation.FluentValidation`, 400); domain ayrıca aynı kuralı korur (`Clients.Validation`).
- İzin: mevcut (`Clients.Create` / `Clients.Update`). Peşin müşteri düzenlenemez (mevcut kural).
- Denetim: `Client.Create` / `Client.Update` audit'e yazılır; `nationalId` ve `taxNumber` istek kaydında **maskeli** (§6).

### Okuma — maskeli

`GET /clients/{id}` (`ClientDetailDto`) iç içe `invoicing` nesnesi (`ClientInvoicingSummaryDto`) ekler; **tam numara dönmez**:

```json
"invoicing": {
  "personType": "Individual",
  "nationalIdMasked": "100******46",
  "taxNumberMasked": null,
  "taxOffice": null,
  "legalName": null,
  "billingProvince": "İstanbul",
  "billingDistrict": "Kadıköy",
  "billingAddressLine": "Moda Cad. No: 1"
}
```

Liste (`GET /clients`), arama (`/search`, hızlı arama) ve liste dışa aktarma bu alanları **hiç taşımaz**. Müşteri veri kopyası (`GET /clients/{id}/data-export`) `client.invoicing` altında aynı maskeli nesneyi taşır.

### Düzenleme için tam değer — `GET /clients/{id}/invoicing-identity`

`Clients.Update` (Admin, Owner, ClinicAdmin, Sekreter; **Veteriner yok** → 403). Yalnız düzenleme formunu doldurmak içindir.

```json
{
  "personType": "Individual",
  "nationalId": "10000000146",
  "taxNumber": null,
  "taxOffice": null,
  "legalName": null,
  "billingProvince": "İstanbul",
  "billingDistrict": "Kadıköy",
  "billingAddressLine": "Moda Cad. No: 1"
}
```

Hatalar: `Clients.NotFound` (404), `Tenants.ContextMissing` (400). Her çağrı audit'e yazılır (`Client.InvoicingIdentity.Read`, hedef `ClientId=…`; istek kaydında numara yoktur).

---

## 3) Klinik mükellef ayarları (`ClinicTaxpayerSettings`)

Klinik başına (Tenant + Clinic benzersiz), randevu varsayılanları deseni. Kayıt yoksa varsayılan döner (`isConfigured: false`, `defaultVatRate: 20`, diğerleri null).

### `GET /clinics/{id}/taxpayer-settings`

`Clinics.Read` (Admin, Owner, ClinicAdmin). Yanıt `ClinicTaxpayerSettingsDto`:

| Alan | Tip | Kural |
|---|---|---|
| `clinicId` | guid | |
| `isConfigured` | bool | Kayıt var mı |
| `taxpayerType` | `"SelfEmployed"` \| `"Company"` \| `"Both"` \| null | Serbest meslek / Şirket / İkisi (AK-3 karma) |
| `taxIdentityNumber` | string? | 10 hane → VKN, 11 hane → TCKN kuralı; tam değer döner (yalnız klinik yöneticileri okur) |
| `taxOffice` | string? (≤100) | |
| `taxpayerName` | string? (≤300) | Unvan / ad soyad |
| `province`, `district` | string? (≤80) | |
| `addressLine` | string? (≤500) | |
| `defaultVatRate` | decimal | Varsayılan KDV (%); boş ücret satırı bunu kullanır |
| `withholdingRate` | decimal? | Stopaj (%); serbest meslek için, varsayılan boş |
| `updatedAtUtc` | datetime? | |

### `PUT /clinics/{id}/taxpayer-settings`

`Clinics.Update` (Admin, Owner, ClinicAdmin). Gövde tam değiştirme (yukarıdaki yazılabilir alanlar; `defaultVatRate` zorunlu). Yanıt 200 + güncel `ClinicTaxpayerSettingsDto`.

- Doğrulama: `taxIdentityNumber` doluysa 10 hane geçerli VKN veya 11 hane geçerli TCKN; oranlar §1; uzunluklar tabloya göre. İhlal 400 (`Validation.FluentValidation` / `Clinics.TaxpayerSettings.Validation`).
- `Clinics.NotFound` (404), `Clinics.AccessDenied` (403, atanmamış klinik), `Auth.PermissionDenied` (403).
- Audit: `Clinic.TaxpayerSettings.Update`, hedef `ClinicId=…`; `taxIdentityNumber` maskeli.
- Bu dilimde mükellef türü ile numara türü arasında zorunluluk yoktur (serbest meslekte TCKN veya VKN, şirkette şahıs şirketi TCKN'si olabilir).

Not: Kurum sayfasındaki "Firma / Fatura Bilgileri" (`/organization/billing-profile`, kiracı düzeyi, Vetinity aboneliği için) ayrıdır ve değişmedi.

---

## 4) Hasta (`Pet`)

| Alan | Kural |
|---|---|
| `hbsAnimalId` | HBS hayvan kimlik no; isteğe bağlı metin, ≤50. `POST /pets`: verilirse yazılır. `PUT /pets/{id}`: **null = dokunma**, `""` = temizle, dolu = yaz (alanı bilmeyen eski istemci silmesin). |
| `microchipNumber` | Değişmedi (≤50, biçim zorlanmaz). 15 haneli rakam dışı değer için uyarı **yalnız arayüzde** gösterilir; API engellemez. |

`GET /pets/{id}` (`PetDetailDto`) ve veri kopyası `pets` bölümü `hbsAnimalId` taşır.

---

## 5) Ücret (`Charge`)

| Alan | Tip | Kural |
|---|---|---|
| `lineKind` | `"Service"` \| `"Product"` | Varsayılan `Service`; migration öncesi kayıtlar `Service` |
| `vatRate` | decimal? | KDV (%); null = klinik varsayılanı (`ClinicTaxpayerSettings.defaultVatRate`, kayıt yoksa %20). Etkin oran belge kesilirken çözülür (Dilim 4) |

Yazma, iç içe isteğe bağlı nesne `tax`:

```json
"tax": { "lineKind": "Product", "vatRate": 10 }
```

- `POST /charges`: `tax` null → `Service` + `vatRate` null.
- `PUT /charges/{id}` (düzeltme): `tax` **null = dokunma**; verilirse ikisi birlikte yazılır. "En az bir alan değişmeli" kuralı `tax` değişikliğini de sayar.
- `POST /charge-suggestions/{id}/convert`: satır türü kalemin ürününden türetilir (§1, `ChargeLineKinds.FromProductKind`); `vatRate` null.
- Geçersiz oran / tür: `Charges.Validation` (400).
- `ChargeDto` yanıtına `lineKind` ve `vatRate` eklenir; liste, detay ve veri kopyası (`charges` bölümü) taşır.
- Belge bağlantısı (`documentId`, okunur) Dilim 3'te eklenecek; bu dilimde alan **yok**.

---

## 6) Denetim kaydı maskelemesi

`AuditBehavior` istek kaydında `nationalId`, `taxNumber`, `taxIdentityNumber` adlı özelliklerin değerini (iç içe nesnelerde de) `TurkishTaxIdentity.Mask` ile yazar. Mevcut tam gizleme (`password`, `token` vb.) değişmedi.

## 7) Uygulama notları (sözleşme dışı bilgi)

- Aydınlatma metni varsayılanı (`ClinicKvkkFormTemplateDefaults.DisclosureText`) fatura/e-belge amacı, TCKN/VKN veri kategorisi ve e-belge entegratörü alıcı grubuyla güncellendi (ürün deposu `legal/aydinlatma-metni.md` v0.2). Yalnız kaydı olmayan klinikte döner; kayıtlı klinik metinleri değişmez. Aydınlatma metninde sürüm kaydı yoktur; varsayılan metin `textVersion: 0` kalır, özet (`textHash`) değişir.
- Migration: `AddInvoicingIdentityFields` — `Clients` (8 sütun), `Pets.HbsAnimalId`, `Charges.LineKind` (int, varsayılan 0 = Service) ve `Charges.VatRate` (`decimal(5,2)`), yeni tablo `ClinicTaxpayerSettings` (`TenantId, ClinicId` benzersiz; `DefaultVatRate decimal(5,2)`, `WithholdingRate decimal(5,2)`).
- Query DB okuma modelleri ve outbox anlık görüntüleri bu alanları **taşımaz** (liste/arama yüzeyinde tam değer olmaması bilinçli).
