# Mobil barkod API sözleşmesi — TIER-006

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-006` (bağımlılık `TIER-001`, `MOBILE-004`), ADR-035 (2026-11-06 paket yönü). İlgili: `PLAN_FEATURES_API_CONTRACT.md`, `DIRECT_SALE_API_CONTRACT.md`, `GOODS_RECEIPT_API_CONTRACT.md`.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-006-mobil-barkod`. Migration **`AddProductBarcodeUniqueIndex`** (yalnız yerel DB). Yeni izin yok, yeni uç yok. §5'teki maddeler **çalışma varsayımıdır, kullanıcı onayı bekliyor.**

---

## 1) Kapsam

| Parça | Paket | Ne yapar |
|---|---|---|
| Ürün kartında barkod | Her pakette | Mevcut `barcode` alanı; artık kiracı içinde benzersiz |
| Barkodla tam eşleşme araması | Her pakette | `GET /api/v1/products?barcode=` |
| Elle ya da okuyucuyla barkod yazmak | Her pakette | Bugünkü `search` (ad, SKU, barkod içinde geçen) aynen kalır |
| Telefon kamerasıyla okutma | Profesyonel, Kurumsal (`PlanFeature.MobileBarcode`) | Okuma tarayıcıda yapılır; sunucuya yalnız okunan barkod gelir |

Kuralların tek yeri: barkod biçimi `Product.NormalizeBarcode` (kayıt, benzersizlik ve arama aynı biçimi kullanır); benzersizlik hatası `ProductErrors`; paket tablosu `SubscriptionPlanCatalog`.

## 2) Ürün kartı barkodu

| Konu | Kural |
|---|---|
| Biçim | Kenar boşlukları atılır; boş ya da yalnız boşluk `null` yazılır. En fazla 100 karakter (`Product.BarcodeMaxLength`) |
| Benzersizlik | Kiracı içinde tek; **pasif ürün dahil** (SKU ile aynı kural). Farklı kiracılar aynı barkodu kullanabilir |
| Karşılaştırma | Veritabanı harmanlamasıyla (büyük-küçük harf farkı yok sayılır); barkodlar çoğunlukla rakamdır |
| Veritabanı | Filtreli benzersiz indeks `IX_Products_TenantId_Barcode` (`[Barcode] IS NOT NULL`) |

`POST /api/v1/products` ve `PUT /api/v1/products/{id}` gövdesi değişmedi. Yeni hata:

```json
{ "status": 409, "code": "Products.DuplicateBarcode", "detail": "Bu barkod \"Mama 1 kg\" ürününde kayıtlı." }
```

Eşzamanlı iki kayıtta ön denetimi geçen ikinci yazma indekse takılır ve aynı kodla 409 döner (`detail` ürün adı olmadan).

## 3) Barkodla arama — `GET /api/v1/products` (`Products.Read`, değişti, geriye uyumlu)

Yeni sorgu parametresi `barcode`: **tam eşleşme**, kenar boşluğu atılır, en fazla 100 karakter (aşarsa `Validation.FluentValidation` 400). Diğer parametrelerle (`isActive`, `productCategoryId`, `search`) birlikte uygulanır. Yanıt bugünkü sayfalı ürün listesidir; barkod tek olduğundan en fazla bir satır döner.

```
GET /api/v1/products?page=1&pageSize=1&isActive=true&barcode=8690000000001
→ 200 { "items": [ { "id": "…", "name": "Mama 1 kg", "barcode": "8690000000001", … } ], "totalItems": 1, … }
```

Bulunamazsa `items: []` (404 değil). Panel kullanımı: satışta `isActive=true` ve satılabilir ürün; mal kabulde `isActive=true` ve stok takipli ürün; ürün listesinde filtresiz.

## 4) Paket: `MobileBarcode`

| Basic | Pro | Premium |
|---|---|---|
| yok | var | var |

Kamera tarayıcıda çalıştığı için **ayrı sunucu kapısı yoktur**; `PortalAppointmentRequests` gibi satır yalnız `access-state` (`planFeatures`) ve paket tablosu içindir. Panel `MobileBarcode` kapalıyken kamera düğmesi yerine kilitli düğme ve "üst pakete geç" kutusu gösterir. §3 ucu paket sormaz: elle girilen barkodla arama her pakette açıktır.

Tarayıcı notları (panel): kamera yalnız güvenli bağlamda (HTTPS ya da `localhost`) açılır. Önce tarayıcının kendi `BarcodeDetector`'ı denenir; yoksa `barcode-detector` (MIT) kütüphanesi ve kendi sunucumuzdan verilen `zxing_reader.wasm` kullanılır. İzin reddi, kamera yokluğu ya da http'de elle giriş kutusu her zaman açıktır. İlaç kutusundaki GS1 karekoddan (DataMatrix) yalnız GTIN alınır, başındaki 0 atılarak EAN-13'e çevrilir.

## 5) Çalışma varsayımları, kullanıcı onayı bekliyor

1. Benzersizlik pasif ürünleri de kapsar (SKU gibi). Pasif üründeki barkod yeni kartta kullanılacaksa önce eski karttan silinir.
2. Barkod büyük-küçük harf duyarsızdır (veritabanı harmanlaması); arama da öyle.
3. Ürün başına tek barkod. Aynı ürünün farklı ambalaj barkodları (koli, tekli) bu işin dışında.
4. GS1 karekoddan yalnız GTIN alınır; karekoddaki parti ve son kullanma tarihi mal kabul satırına yazılmaz (ayrı iş önerisi).
5. Kamera ile okunan ürün mal kabulde ilk boş satıra, boş satır yoksa yeni satıra yazılır; satışta sepetteki aynı ürünün miktarı artar (bugünkü okuyucu davranışı).

## 6) Canlıya geçiş notu

Migration, kiracı içinde aynı barkodu taşıyan iki ürün varsa indeks oluşturamaz ve durur. Uygulamadan önce kontrol:

```sql
SELECT TenantId, Barcode, COUNT(*) FROM Products WHERE Barcode IS NOT NULL GROUP BY TenantId, Barcode HAVING COUNT(*) > 1;
```

Yerel geliştirme veritabanında barkodlu ürün yoktu (2026-11-08). Paylaşılan ve üretim veritabanı denetlenmedi.

## 7) Testler

- Domain: `ProductBarcodeTests` (biçim, boş değer, kayıt ve güncelleme).
- Application: `ProductHandlersTests` (eklemede ve güncellemede çift barkod, ürün adı mesajda, boş barkodda denetim yok), `TenantPlanFeatureEvaluatorTests` paket tablosu.
- Entegrasyon: `ProductsEndpointTests.Barcode_Should_Be_Unique_Per_Tenant_And_Searchable_By_Exact_Match` (kırpma, 409, pasif ürün dahil, güncellemede kendini dışlama, boşluk barkodu, tam eşleşme, kısmi değer bulunmaz, `isActive` ile birlikte, başka kiracı).

## 8) Bilinen sınırlar

- Kameranın paket kapısı panel tarafındadır; elle yazılan barkodla aynı uç kullanıldığından sunucu kamerayla okunduğunu ayırt etmez (ayırt etmesi de gerekmez: veri girişi kilitlenmez).
- Gerçek telefon kamerasıyla deneme yapılmadı; yalnız birim testleri ve tarayıcıda elle giriş yolu denendi.
