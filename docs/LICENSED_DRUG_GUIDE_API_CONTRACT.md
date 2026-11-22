# İlaç rehberi API sözleşmesi — REF-001 Dilim 1–2

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-049-ilac-rehberi-ve-klinik-basvuru.md` (K2, K3, K4, K6, K7), backlog REF-001. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`).

> **Durum:** Sözleşme. **Veri:** Bakanlık (HBS) verisinin toplu alımı yazılı izne bağlıdır (ADR-049 açık karar 2); izin gelene kadar katalog **boştur**. Yerelde yalnız `DbMigrator seed-dev-drug-guide` (Development) ile elle yazılmış birkaç "ÖRNEK" kayıt eklenir; bunlar Bakanlık verisi değildir. **Kapsam dışı:** HBS eşitleme komutu ve aylık karşılaştırma (izinden sonra), farmasötik şekil / ATCvet / terapötik grup alanları, barkodla eşleşme önerisi (kaynakta barkod yok), ilaç etkileşimi, doz bilgisi, prospektüs metni.

---

## 1) Kavramlar

- **Ruhsatlı ürün (`LicensedProduct`)**: global, kiracısız, salt okunur katalog satırı. Yalnız **olgu alanları**: ürün adı, etken madde(ler), hedef türler, ruhsat sahibi, ruhsat numarası (kaynakta yoksa boş), Ürün Özellikleri Özeti bağlantısı (yalnız `https`; belge metni saklanmaz). Her satırda kaynak (`source`) ve son eşitleme zamanı (`sourceSyncedAtUtc`) vardır.
- **Kaynaktan düşen ürün silinmez:** `isListedInSource = false` ile "ruhsat listesinde artık yok" diye gösterilir.
- **Etken madde (`ActiveIngredient`)**: ayrı, normalize edilmiş global liste. Yazım varyantları Türkçe karşılaştırma anahtarıyla (`TurkishTextKey`: büyük/küçük harf, İ/ı, ç/ğ/ö/ş/ü ve boşluk farkı yok sayılır) tek maddeye eşlenir; anahtar benzersizdir.
- **Hedef tür**: kaynaktaki yazım (`label`) ve eşlenebildiyse bizim tür kaydımız (`speciesId`). Eşlenemeyen tür (ör. arı) yalnız etiketle gösterilir.
- **Ürün kartı bağı**: klinik ürün kartında (`Product`) isteğe bağlı `licensedProductId`. Etken madde ve hedef tür bağdan okunur, karta kopyalanmaz. Bağsız ürün (hizmet, mama, ithal ürün) olduğu gibi çalışır.
- **Hedef tür notu**: hastanın türü ruhsattaki eşlenmiş hedef türler arasında değilse `notLicensedForPetSpecies = true`. **Engellemez**; ruhsat dışı kullanım hekimin kararıdır. Hedef türlerden hiçbiri eşlenmemişse bilinmez (`null`), not gösterilmez. Kural tek yerde: `LicensedProduct.IsLicensedFor`.
- **Klinik sınır (K6)**: rehber bilgi ekranıdır; tanı, doz ve tedavi önermez. "Olası teşhis", "olasılık", "%" ifadesi kullanılmaz. Ekranda kısa not: bilgi Bakanlık ruhsat listesinden alınmıştır [tarih], esas metin onaylı Ürün Özellikleri Özeti'dir, klinik karar hekime aittir.

## 2) Yetkiler

| İzin | Kapsam | Rol bağı |
|---|---|---|
| `DrugGuide.Read` `[YENİ]` | Arama, ürün detayı, etken madde araması, özet, reçete/uygulama satırı bilgisi | Admin, Owner, ClinicAdmin, Veteriner, Sekreter (her rol; `RolePermissionBindings`) |
| `Products.Read` (mevcut) | Etken maddeye göre stoktaki ürünler (stok bilgisi) | değişmedi |
| `Products.Update` (mevcut) | Ürün kartı bağı | değişmedi |

Yeni izin `PermissionSeeder` + `RolePermissionBindingSeeder` ile eklenir (DbMigrator `seed`). Paket kısıtı yoktur (K7: her pakette).

## 3) Uçlar — `api/v1/drug-guide`

### `GET /drug-guide/summary`

```json
{ "productCount": 0, "ingredientCount": 0, "lastSyncedAtUtc": null }
```

Katalog boşsa `lastSyncedAtUtc = null`; ekran "henüz yüklenmedi" boş durumunu gösterir.

### `GET /drug-guide/products?search=&field=&activeIngredientId=&page=&pageSize=`

Sayfalı (`PagedResult`, `pageSize` 1–100). `field`: `All` (varsayılan: ad, etken madde, ruhsat sahibi), `Product`, `Ingredient`, `Holder`. `search` en çok 5 kelime; her kelime seçilen alanın Türkçe anahtarında geçmeli (sıra bağımsız). `activeIngredientId` verilirse yalnız o maddeyi içerenler. Sıra: kaynakta listelenenler önce, sonra ada göre.

`LicensedProductDto`:

```json
{
  "id": "guid", "name": "string",
  "activeIngredients": [ { "id": "guid", "name": "string" } ],
  "targetSpecies": [ "Kedi", "Köpek" ],
  "licenseHolder": "string", "licenseNumber": "string?", "spcUrl": "https://…?",
  "isListedInSource": true, "source": "HBS", "sourceSyncedAtUtc": "datetime"
}
```

### `GET /drug-guide/products/{id}`

`LicensedProductDto`; yoksa 404 `DrugGuide.ProductNotFound`.

### `GET /drug-guide/active-ingredients?search=&limit=`

Ada göre, `limit` 1–50 (varsayılan 20): `[{ "id", "name", "productCount" }]`.

### `GET /drug-guide/active-ingredients/{id}/stock-products?clinicId=` — `Products.Read`

Etken maddeyi içeren ruhsatlı ürünlere **bağlı** klinik ürün kartları (aktifler önce, ada göre) ve tek klinikteki stok miktarı; stok satırı yoksa 0.

```json
[ { "productId": "guid", "productName": "string", "unit": "Kutu", "isActive": true,
    "licensedProductId": "guid", "licensedProductName": "string", "quantityOnHand": 7 } ]
```

Klinik kapsamı gerekir (`clinicId` ya da aktif klinik; yoksa 400 `DrugGuide.ClinicScopeRequired`); `IClinicReadScopeResolver` ile denetlenir. Madde yoksa 404 `DrugGuide.IngredientNotFound`.

### `GET /drug-guide/product-info?productIds={guid}&productIds={guid}&petId={guid?}`

Reçete ve uygulama satırları için (1–50 ürün). Yalnız rehbere bağlı ürünler döner:

```json
[ { "productId": "guid", "licensedProductId": "guid", "licensedProductName": "string",
    "activeIngredients": [ { "id": "guid", "name": "string" } ], "targetSpecies": [ "Köpek" ],
    "notLicensedForPetSpecies": true, "petSpeciesName": "Kedi" } ]
```

`petId` yoksa ya da kiracıda bulunamazsa `notLicensedForPetSpecies = null`, `petSpeciesName = null`.

## 4) Ürün kartı bağı — `api/v1/products`

- `PUT /products/{id}/licensed-product` — `Products.Update`. Gövde `{ "licensedProductId": "guid | null" }`; `null` bağı kaldırır. 204. Ürün yoksa 404 `Products.NotFound`, katalog kaydı yoksa 404 `DrugGuide.ProductNotFound`.
- `GET /products/{id}` (`ProductDto`): `licensedProductId` eklendi (bağsızsa `null`). Ürün güncelleme (`PUT /products/{id}`) bağa dokunmaz.
- Öneri: kaynakta barkod olmadığı için bağ, ürün adıyla rehber araması üzerinden kullanıcı onayıyla kurulur.

## 5) Hata kodları

| Durum | Kod | HTTP |
|---|---|---|
| Katalog ürünü yok | `DrugGuide.ProductNotFound` | 404 |
| Etken madde yok | `DrugGuide.IngredientNotFound` | 404 |
| Stok listesinde klinik kapsamı yok | `DrugGuide.ClinicScopeRequired` | 400 |
| Ürün kartı yok | `Products.NotFound` | 404 |
| Biçim (sayfa, uzunluk, boş kimlik) | standart doğrulama zarfı | 400 |

## 6) Veri modeli ve migration

Migration `AddDrugGuideCatalog`:

- `ActiveIngredients`: `Id`, `Name` (200), `NormalizedName` (200, benzersiz).
- `LicensedProducts`: `Id`, `Name`/`NormalizedName` (300), `LicenseHolder`/`NormalizedLicenseHolder` (300), `LicenseNumber` (100)?, `SpcUrl` (1000)?, `Source` (50), `SourceSyncedAtUtc`, `IsListedInSource`. İndeksler ad ve ruhsat sahibi anahtarı.
- `LicensedProductIngredients`: (`LicensedProductId`, `ActiveIngredientId`) anahtar, `SortOrder`; ürünle cascade, maddeye restrict.
- `LicensedProductTargetSpecies`: `Id`, `LicensedProductId` (cascade), `Label` (100), `SpeciesId`? (FK `Species`, restrict).
- `Products.LicensedProductId`? (FK `LicensedProducts`, restrict; indeksli). Mevcut ürünler bağsız kalır.

## 7) Kullanım etkisi (panel)

- Stok grubunda "İlaç rehberi" sayfası (ADR-049 açık karar 6 A: ayrı menü öğesi yok, Stok grubunda sayfa). Katalog boşken boş durum.
- Ürün kartında rehber bağı (adla arayıp seç / kaldır).
- Reçete satırı ve uygulama formunda bağlı ürünün etken maddeleri ve engellemeyen hedef tür notu.
- Rehberde etken madde seçilince stoktaki ürünler (`Products.Read` varsa).
