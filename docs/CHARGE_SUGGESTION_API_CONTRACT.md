# Ücret önerisi ve hizmet kataloğu API sözleşmesi — ADR-018

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-018-charge-suggestion-and-service-catalog.md` (taslak), `ux/HEDEF-IS-AKISLARI-2026-10.md` bölüm 1.4 / dilim D4; `ADMINISTRATIONS_API_CONTRACT.md`, `CHARGES_API_CONTRACT.md`, `FIN_DISCOVERY.md`. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test). Migration `20261014043646_AddChargeSuggestions` dosyası üretildi, **paylaşılan/yerel dev veritabanına uygulanmadı**. Yeni izin **yok** (seed gerekmez; bölüm 6).

---

## 1) Kavramlar ve kurallar

- **"Ücret bekliyor" kalemi kalıcı bir kayıttır (`ChargeSuggestion`), ücret değildir.** Hekimin ücretlendirilebilir işlemi kaydedildiği **aynı transaction'da** doğar. Kaynaklar: **uygulama** (`sourceType = "Administration"`: stoktan düşen uygulama; aşı kaydıyla birlikte yazılan uygulama dahil) ve **muayenede yapılan hizmet** (`sourceType = "ExaminationService"`, ADR-020 S4: bugün yalnız muayene şablonu uygulamasından; ayrı işlem kaydı yoktur, `sourceId` = kalemin kendi `id`'si; bkz. `EXAMINATION_TEMPLATE_API_CONTRACT.md`).
- **Kalem doğar:** ürünün katalog fiyatı `> 0` ise. Katalogda bedelsiz (fiyat 0) tanımlı ürün kalem doğurmaz (bilinçli ücretsiz). Hizmet (`Service`) uygulama kaydı olamaz; hizmet kalemi yalnız şablon uygulamasıyla doğar (bölüm 7). `ExaminationService` kaleminin iptal edilecek kaynak işlemi yoktur: istenmiyorsa **ücretsiz say** ile kapatılır.
- **Ön değerler (kalem doğduğu andaki):** açıklama = ürün adı, miktar = uygulama miktarı, birim = ürün birimi, `suggestedUnitPrice` = katalog fiyatı. Fiyat TRY değilse veya ücret kuralına uymuyorsa (> 2 ondalık, üst sınır) `null`: resepsiyon girer. Sonraki katalog fiyat değişikliği bekleyen kalemi **değiştirmez**.
- **Durum makinesi:** `Pending` → `Converted` | `Waived` | `Cancelled`. Yalnız `Pending` listede görünür ve karar alır; diğerleri **son durumdur, geri dönmez**.
- **Kaynak izi:** kiracıda `(sourceType, sourceId)` benzersizdir (bir işlemden en çok bir kalem); `chargeId` benzersizdir (bir ücret en çok bir kalemi kapatır). `Charge` değişmedi; bağ kalemdedir.
- **Net kurallar:**
  1. **Çevrilen kalem** listeden düşer (`Converted`, `chargeId` dolu).
  2. **Çift ücret olmaz:** (a) yalnız `Pending` çevrilir; (b) kalemin `RowVersion`'ı eşzamanlı iki karardan yalnız birini kabul eder, kaybedenin yazdığı ücret aynı transaction'da geri alınır; (c) `requestId` tekrarı ilk ücreti döndürür.
  3. **Uygulama iptal edilirse** bekleyen kalem aynı transaction'da düşer (`Cancelled`). Ücreti **zaten kesilmişse** kalem ve ücret değişmez (ADMINISTRATIONS D5); yönetici ücreti `Charges.Correct` ile düzeltir/iptal eder. İptal ile eşzamanlı çevirme yarışında tek kazanan olur: çevirme kazanırsa iptal `Administrations.ConcurrencyConflict` (409) alır ve tekrar denenince kalem artık `Converted` olduğu için iptal başarılı olur (ücret kalır, kural 3).
  4. **Ücret iptal edilirse kalem geri "bekliyor" OLMAZ** (`Converted` kalır). Yeniden ücretlendirme gerekirse elle serbest ücret yazılır.
  5. **Ücretsiz say** gerekçelidir (5–500), audit'lenir (`ChargeSuggestion.Waive`), **geri alınmaz**.
  6. **Hekim** kalemleri okur (`Charges.Read`), çeviremez/ücretsiz sayamaz (`Charges.Create` yok; ADR-012 K2).
- **Geçmiş veri:** migration öncesi uygulamalar için kalem **üretilmez** (backfill yok); liste yalnız bu sürümden sonraki işlemleri gösterir.

### `ChargeSuggestionDto`

| Alan | Tip | Not |
|---|---|---|
| `id` | guid | Kalem kimliği (çevirme/ücretsiz sayma uçları bununla) |
| `sourceType` | string | `"Administration"` \| `"ExaminationService"` |
| `sourceId` | guid | Uygulama kimliği; `ExaminationService` için kalemin kendi `id`'si |
| `clinicId` | guid | |
| `clientId` | guid | Hayvanın **güncel** sahibi (ücretin müşterisi) |
| `clientName` | string | |
| `petId` | guid | |
| `petName` | string | |
| `examinationId` | guid? | |
| `visitId` | guid? | Muayenenin kalem doğduğu andaki geliş kaydı |
| `productId` | guid | |
| `description` | string | Ürün/hizmet adı |
| `quantity` | decimal | |
| `unit` | string | Ürün birimi |
| `suggestedUnitPrice` | decimal? | Katalog fiyatı; `null` = resepsiyon girer |
| `currency` | string | `"TRY"` |
| `occurredAtUtc` | datetime | Uygulama zamanı |
| `status` | string | `Pending` \| `Converted` \| `Waived` \| `Cancelled` |
| `chargeId` | guid? | `Converted` iken |
| `resolvedAtUtc` | datetime? | Karar zamanı |
| `waiveReason` | string? | `Waived` iken |

---

## 2) `GET /api/v1/charge-suggestions`

`Charges.Read` (hekim dahil). Yalnız `Pending` kalemler. Sayfalı (`page` ≥ 1, `pageSize` 1–200; varsayılan 1/20) `PagedResult<ChargeSuggestionDto>`. Klinik kapsamı zorunlu: `clinicId` veya aktif klinik bağlamı (`ChargeSuggestions.ClinicScopeRequired` 400; uyuşmazlık `ChargeSuggestions.ClinicContextMismatch` 400; atanmamış klinik `Clinics.AccessDenied` 403). Süzgeçler: `petId`, `examinationId`, `visitId`, `clientId` (müşterinin güncel hayvanlarının kalemleri; ADR-020 S5, bkz. `PET_STATUS_CLIENT_CONTACT_API_CONTRACT.md` §3). Sıralama: `occurredAtUtc` azalan.

## 3) `POST /api/v1/charge-suggestions/{id}/convert`

`Charges.Create` (resepsiyon, yönetici; hekimde yok). Gövde opsiyoneldir (boş gövde = kalemdeki değerlerle tek tık):

```json
{ "description": "string?", "quantity": 1, "unitPrice": 250.00, "requestId": "guid?" }
```

Verilmeyen alan kalemden gelir; verilen alan düzenlenmiş değerdir (kurallar `CHARGES_API_CONTRACT.md` ile aynı). Etki: ücret mevcut ücret kurallarıyla yazılır — müşteri = hayvanın güncel sahibi, `petId`, `examinationId`, muayenenin **güncel** `visitId`'si; kalem `Converted` olur. İkisi tek transaction.

Yanıt: yeni ücret **201** + `ChargeCreateResultDto` (`{ created: true, charge }`, `Location` = `/charges/{id}`); aynı `requestId` tekrarında **200**, `created: false`, aynı ücret.

| Kod | HTTP | Durum |
|---|---|---|
| `ChargeSuggestions.NotFound` | 404 | Kalem yok / başka kiracı / aktif klinik dışı |
| `ChargeSuggestions.Validation` | 400 | Kalemde fiyat yok ve `unitPrice` verilmedi |
| `ChargeSuggestions.AlreadyConverted` | 409 | Kalem zaten ücrete çevrilmiş (başka `requestId` ile ya da istek kimliksiz) |
| `ChargeSuggestions.AlreadyWaived` | 409 | Ücretsiz sayılmış |
| `ChargeSuggestions.SourceAlreadyVoided` | 409 | Uygulama iptal edilmiş |
| `ChargeSuggestions.DuplicateRequestId` | 409 | `requestId` başka bir ücrete ait |
| `ChargeSuggestions.ConcurrencyConflict` | 409 | Kalem aynı anda değişti (nadir); listeyi yenile |
| `Charges.RequestIdVoided` | 409 | Bu `requestId`'nin ücreti sonradan iptal edilmiş (tekrar istek) |
| `Charges.Validation`, `Charges.VisitVoided`, `Pets.NotFound`, `Tenants.TenantInactive`, `Clinics.AccessDenied` | 400/404/403 | Ücret kuralları |

Mevcut `POST /charges` (serbest ücret) aynen kalır.

## 4) `POST /api/v1/charge-suggestions/{id}/waive`

`Charges.Create`. Gövde `{ "reason": "string" }` (5–500). Yanıt **200** `ChargeSuggestionDto` (`status: "Waived"`). Hatalar: `ChargeSuggestions.Validation` (400, gerekçe), `NotFound` (404), `AlreadyWaived` / `AlreadyConverted` / `SourceAlreadyVoided` / `ConcurrencyConflict` (409). Audit: `ChargeSuggestion.Waive`.

## 5) Sayılar (muayene ve Bugün)

- `GET /api/v1/examinations/{id}/related-summary` → **`pendingChargeCount`** (`int?`): muayeneye bağlı `Pending` kalem sayısı (klinik bağlamı varsa onunla sınırlı; blok başına 10 sınırı sayıya uygulanmaz). `Charges.Read` yoksa `null`.
- `GET /api/v1/visits/today` satırları → **`pendingChargeCount`** (`int?`): ziyarete bağlı `Pending` kalem sayısı (kalemin `visitId`'si). Diğer tutar alanlarıyla aynı kural: `Charges.Read` yoksa `null`, ziyaretsiz (planlı) satırda `null`, ziyaret varsa `0` veya fazlası.

## 6) Yetkiler

Yeni izin **yok**; seed gerekmez.

| İşlem | İzin | Varsayılan roller |
|---|---|---|
| Liste, sayılar | `Charges.Read` | Admin, Owner, ClinicAdmin, Veteriner, Sekreter |
| Çevirme, ücretsiz sayma | `Charges.Create` | Admin, Owner, ClinicAdmin, Sekreter (Veteriner'de yok) |

## 7) Hizmet kataloğu (`Product` genişlemesi)

- `ProductKind` değerine **`Service`** (= 3) eklendi; mevcut `POST/PUT /products` aynı: `name`, `unit` (örn. `"Adet"`), `unitPrice`, `currency`, istekte `kind: 3` (sayı; diğer türlerle aynı biçim), yanıtta `kind: "Service"`. Hizmette `controlledClass` `None` olmalıdır (`Products.Validation`, 400).
- Hizmet **stok takipli değildir**: uygulama, stok hareketi (giriş/çıkış/sayım), doğrudan satış ve reçete satırında kullanılamaz → `Products.NotStockTracked` (400). `ProductDto` değişmez (`kind` yeni değeri alır).
- Tür değişimi: ürün `Service`'e veya `Service`'ten çevrilemez (`Products.KindChangeNotAllowed`, 400); yanlış kayıt pasif yapılır, yenisi açılır.
- Hizmet **fiyat kataloğudur**; resepsiyon serbest ücret yazarken katalogdan (`GET /products`, `kind = Service` istemcide ayıklanır) ad ve fiyatı doldurur. Hekimin işlemde hizmet seçmesi muayene şablonu satırıyla yapılır (ADR-020 S4, `sourceType = "ExaminationService"`); şablon dışında tek hizmet seçme ucu yoktur. KDV, birim dönüşümü, paket yok. `GET /products` listesine `kind` süzgeci eklenmedi.

---

## 8) Frontend için değişenler

1. **Yeni uçlar:** `GET /charge-suggestions` (Ücretlendir paneli; `visitId`/`examinationId`/`petId` süzgeci), `POST /charge-suggestions/{id}/convert` (tek tık: boş gövde; düzenlendiyse yalnız değişen alanlar), `POST /charge-suggestions/{id}/waive` (gerekçe zorunlu). Çevirmede `requestId` üret, yeniden denemede aynısını gönder.
2. **Hekim:** kalemleri görür; Çevir/Ücretsiz say düğmeleri yalnız `Charges.Create` olanda.
3. **Sayılar:** `related-summary.pendingChargeCount` (kesinleştirme kontrolü: "n ücret bekliyor"), `visits/today` satırında `pendingChargeCount` (Bugün rozeti). `null` = yetki yok/ziyaretsiz satır.
4. **Ürün formu:** `kind` seçeneklerine `Service` ("Hizmet"); seçilince stok, kontrollü sınıf, parti alanlarını gizle; stok/uygulama/satış/reçete ürün seçicilerinden hizmetleri çıkar (sunucu da reddeder).
5. **`ChargeDto` değişmedi.** Fiyatı `null` kalemde birim fiyat alanı boş ve zorunlu gelir.
6. **Yeni hata kodları:** `ChargeSuggestions.*` (bölüm 3, 4), `Products.NotStockTracked`, `Products.KindChangeNotAllowed`, uygulama iptalinde `Administrations.ConcurrencyConflict`. 409'larda listeyi yenile.
7. **Davranış:** ücret iptal edilirse kalem geri gelmez; uygulama iptalinde bekleyen kalem kaybolur; liste her zaman sunucudan okunur.
