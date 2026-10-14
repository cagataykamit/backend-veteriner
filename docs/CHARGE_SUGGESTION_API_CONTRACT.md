# Ücret önerisi ve hizmet kataloğu API sözleşmesi — ADR-018

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-018-charge-suggestion-and-service-catalog.md` (taslak), `ux/HEDEF-IS-AKISLARI-2026-10.md` bölüm 1.4 / dilim D4; `ADMINISTRATIONS_API_CONTRACT.md`, `CHARGES_API_CONTRACT.md`, `FIN_DISCOVERY.md`. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme (kod bu sözleşmeye göre yazıldı). Migration dosyası üretildi, **hiçbir veritabanına uygulanmadı**. Yeni izin için seed gerekir (bölüm 6).

---

## 1) Kavramlar ve kurallar

- **Ücret önerisi ("ücret bekliyor" kalemi) kalıcı kayıt değildir, hesaplanır.** Kalem = klinikte kaydedilmiş, **iptal edilmemiş** bir işlem kaydı (`sourceType = "Administration"`: stoktan düşen uygulama; aşı kaydıyla birlikte yazılanlar dahil) ki (a) kaynağı olarak gösteren **hiçbir ücret** yoktur (iptal edilmiş ücret dahil) ve (b) **ücretsiz sayılmamıştır**. İşlem kaydı tek doğruluk kaynağıdır; ayrı öneri satırı yoktur.
- **Kaynak izi:** `Charge` iki yeni alan taşır: `sourceType` (string?, şimdilik yalnız `"Administration"`), `sourceId` (guid?). Kiracıda `(sourceType, sourceId)` **benzersizdir** (iptal edilmiş ücretler dahil; DB düzeyinde). Elle yazılan ücrette ikisi `null`.
- **Çift ücret olmaz:** bir işlemden en çok bir ücret doğar. Dönüştürme `requestId` ile idempotenttir; eşzamanlı iki dönüştürmede biri kazanır, diğeri `ChargeSuggestions.AlreadyConverted` (409) alır (aynı `requestId` ile tekrar = ilk ücret, 200).
- **Net kurallar:**
  1. Dönüştürülen kalem listeden düşer.
  2. **Uygulama iptal edilirse** kalem düşer. Ücreti **zaten kesilmişse** ücret kendiliğinden değişmez (ADMINISTRATIONS D5); yönetici ücreti `Charges.Correct` ile iptal/düzeltir.
  3. **Ücret iptal edilirse kalem geri "bekliyor" OLMAZ** (kaynak izi iptal edilmiş ücrette de durur). Yanlış ücret düzeltmesi elle serbest ücretle yapılır.
  4. **Ücretsiz say** gerekçelidir (5–500), audit'lenir, **geri alınmaz**; kalem düşer; sonradan dönüştürülemez.
  5. Hekim kalemleri **okur**, ücret ekleyemez/dönüştüremez/ücretsiz sayamaz (ADR-012 K2).
- **Kapsam:** yalnız `Administration` kaynağı. Tedavi, yatış günü, lab ve hizmet seçimi **bu sözleşmede yok**: bu kayıtlarda ürün/hizmet bağı yok, dolayısıyla fiyat kaynağı yok (ADR-018 karar 8). Aşı kaydı ürünsüzse uygulama yoktur, kalem de yoktur.
- **Eşzamanlılık:** koruma, kaynak benzersiz indeksi (DB) ve `requestId` replay'idir. Uygulamanın `RowVersion`'ı yalnız iptal eşzamanlılığı içindir ve dönüştürmede **kullanılmaz**; dönüştürmeyle eşzamanlı iptal yarışı çok dar bir pencerede kesilmiş ücret + iptal edilmiş uygulama bırakabilir (kural 2 yönetici düzeltmesi).

### `ChargeSuggestionDto`

| Alan | Tip | Not |
|---|---|---|
| `sourceType` | string | `"Administration"` |
| `sourceId` | guid | Uygulama kimliği |
| `clinicId` | guid | |
| `clientId` | guid | Hayvanın sahibi; dönüştürmede ücretin müşterisi |
| `clientName` | string | |
| `petId` | guid | |
| `petName` | string | |
| `examinationId` | guid? | |
| `visitId` | guid? | Muayenenin geliş kaydı |
| `vaccinationId` | guid? | Aşı kaydıyla birlikte yazıldıysa |
| `productId` | guid | |
| `description` | string | Ürün/hizmet **adı** (ücret açıklaması önerisi) |
| `quantity` | decimal | Uygulama miktarı |
| `unit` | string | Ürün stok birimi (miktarın yanında gösterilir) |
| `suggestedUnitPrice` | decimal? | Ürünün güncel birim fiyatı; fiyat ≤ 0 veya para birimi TRY değilse `null` (resepsiyon girer) |
| `currency` | string | `"TRY"` |
| `occurredAtUtc` | datetime | Uygulama zamanı |
| `addedAfterFinalization` | bool | Kesinleşmiş muayeneye sonradan eklenmiş |

---

## 2) `GET /api/v1/charge-suggestions`

`ChargeSuggestions.Read`. Sayfalı (`page`, `pageSize` 1–200, varsayılan 1/20) `PagedResult<ChargeSuggestionDto>`. Klinik kapsamı zorunlu: `clinicId` veya aktif klinik bağlamı (`ChargeSuggestions.ClinicScopeRequired`, 400; uyuşmazlık `ChargeSuggestions.ClinicContextMismatch` 400; atanmamış klinik `Clinics.AccessDenied` 403). Süzgeçler: `petId`, `examinationId`, `clientId`. Sıralama: `occurredAtUtc` azalan. Liste kalemin **şu anki** durumunu verir; fiyat güncel ürün fiyatıdır.

## 3) `POST /api/v1/charge-suggestions/convert`

`Charges.Create` (resepsiyon, yönetici düzeyi; hekimde yok). İstek:

```json
{
  "clinicId": "guid?",
  "sourceType": "Administration",
  "sourceId": "guid",
  "description": "string?",
  "quantity": 1,
  "unitPrice": 250.00,
  "requestId": "guid?"
}
```

`description`, `quantity`, `unitPrice` **opsiyonel**: verilmezse kalemdeki değer (ad, miktar, `suggestedUnitPrice`) kullanılır; verilirse düzenlenmiş değerdir (ücret kuralları `CHARGES_API_CONTRACT.md` ile aynı). Fiyat ne kalemde ne istekte yoksa `ChargeSuggestions.Validation` (400, "birim fiyat gerekli").

Yanıt: yeni ücret **201** + `ChargeCreateResultDto` (`{ created: true, charge }`); aynı `requestId` tekrarında **200**, `created: false`. Etki: `Charge` normal ücret kurallarıyla yazılır (müşteri = hayvanın sahibi, `petId`, `examinationId`, muayenenin `visitId`'si, kaynak izi). Mevcut ücret ucu (`POST /charges`) aynen kalır ve kaynak almaz.

Hatalar: `ChargeSuggestions.Validation` (400; desteklenmeyen `sourceType`, fiyat yok), `ChargeSuggestions.NotFound` (404; kaynak yok/kiracı-klinik dışı), `ChargeSuggestions.SourceAlreadyVoided` (409; uygulama iptal edilmiş), `ChargeSuggestions.AlreadyConverted` (409; başka `requestId`/isteksiz ikinci dönüştürme), `ChargeSuggestions.AlreadyWaived` (409), ücret uçlarının kodları (`Charges.Validation`, `Pets.NotFound`, `Clients.NotFound`, `Tenants.TenantInactive`, `Clinics.AccessDenied`).

## 4) `POST /api/v1/charge-suggestions/waive`

`Charges.Create`. İstek: `{ "clinicId": "guid?", "sourceType": "Administration", "sourceId": "guid", "reason": "string" }` (`reason` 5–500). Yanıt **200** `{ sourceType, sourceId, waivedAtUtc, reason }`. Aynı kalem ikinci kez: `ChargeSuggestions.AlreadyWaived` (409); ücreti varsa `AlreadyConverted` (409); iptal edilmiş uygulama `SourceAlreadyVoided` (409). Audit: `ChargeSuggestion.Waive`.

## 5) Muayene özetinde bekleyen sayı

`GET /api/v1/examinations/{id}/related-summary` yanıtına **`pendingChargeCount`** (`int?`) eklenir: o muayeneye bağlı kalem sayısı (bölüm 1 kuralı; blok başına 10 sınırı yalnız listelere uygulanır, sayıya değil). Çağıranda `ChargeSuggestions.Read` yoksa `null`. Hekim, resepsiyon ve yönetici aynı sayıyı görür.

## 6) Yetkiler

| İzin | Açıklama | Roller |
|---|---|---|
| `ChargeSuggestions.Read` (**yeni**) | Bekleyen kalemleri okuma | Admin, Owner, ClinicAdmin, **Veteriner**, Sekreter |
| `Charges.Create` (mevcut) | Dönüştürme ve ücretsiz sayma | Admin, Owner, ClinicAdmin, Sekreter (Veteriner'de yok) |

**Seed gerekir:** yeni izin `PermissionCatalog`'a ve `RolePermissionBindings`'e eklendi; yerel/paylaşılan veritabanına **kullanıcı çalıştırır** (izin seed komutu). Mevcut özel roller elle almalı.

## 7) Hizmet kataloğu (`Product` genişlemesi)

- `ProductKind` değerine **`Service`** eklendi (`kind: "Service"`); mevcut `POST/PUT /products` aynı: `name`, `unit` (örn. `"Adet"`), `unitPrice`, `currency`. Hizmette `controlledClass` `None` olmalıdır (`Products.Validation`, 400).
- Hizmet **stok takipli değildir**: uygulama, stok hareketi, doğrudan satış ve reçete satırında kullanılamaz → `Products.NotStockTracked` (400). `ProductDto` değişmez (`kind` yeni değer alır).
- Tür değişimi: bir ürün `Service`'e veya `Service`'ten çevrilemez (`Products.KindChangeNotAllowed`, 400); yanlış kayıt pasif yapılır, yenisi açılır.
- Pilot: ücret önerisi kaynağı yalnız uygulama olduğundan hizmet bugün **fiyat kataloğudur**; resepsiyon elle ücret yazarken katalogdan (`GET /products`, `kind = Service` istemcide ayıklanır) ad ve fiyatı doldurur. Hekimin işlemde hizmet seçmesi sonraki dilim. KDV, birim dönüşümü, paket yok.
- `GET /products` listesine `kind` süzgeci **eklenmedi** (pilot kataloğu küçük).

---

## 8) Frontend için değişenler

1. **Yeni uçlar:** `GET /charge-suggestions` (Ücretlendir paneli, Bugün rozeti), `POST /charge-suggestions/convert` (tek tık; alanlar önceden dolu, düzenlenirse üç alan gönderilir), `POST /charge-suggestions/waive` (gerekçe zorunlu). Dönüştürme/ücretsiz sayma için `requestId` üretip yeniden denemede aynısını gönder.
2. **Hekim:** bekleyen kalemi görür (`ChargeSuggestions.Read`); dönüştür/ücretsiz say düğmeleri yalnız `Charges.Create` olanda görünür.
3. **related-summary:** `pendingChargeCount` (`int?`; `null` = yetki yok). Muayene/kesinleştirme kontrolünde "n ücret bekliyor" göstergesi için.
4. **Ürün formu:** `kind` seçeneklerine `Service` ("Hizmet"); seçilince stok, kontrollü sınıf, parti alanlarını gizle; stok/uygulama/satış ürün seçicilerinden hizmetleri çıkar (sunucu da reddeder: `Products.NotStockTracked`).
5. **Ücret DTO'su değişmedi** (kaynak izi iç kayıt, `ChargeDto`'ya eklenmedi).
6. **Yeni hata kodları:** `ChargeSuggestions.*` (bölüm 3, 4), `Products.NotStockTracked`, `Products.KindChangeNotAllowed`. 409'larda listeyi yenile.
7. **Davranış:** ücret iptal edilirse kalem geri gelmez; uygulama iptalinde kalem kaybolur; liste her zaman sunucudan okunur (istemci hesaplamaz).
