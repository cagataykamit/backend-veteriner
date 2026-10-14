# Muayene şablonu (tedavi paketi) API sözleşmesi — ADR-020 S4

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-020-clinic-staff-centered-patient-card.md` (karar 4-5, dilim S4), `ux/HEDEF-IS-AKISLARI-2026-10.md` (P9); ilgili sözleşmeler `ADMINISTRATIONS_API_CONTRACT.md`, `CHARGE_SUGGESTION_API_CONTRACT.md` (ADR-018), `EXAMINATIONS_WORKSPACE_API_CONTRACT.md`, `EXAMINATION_FINALIZE_API_CONTRACT.md` (ADR-013), `DIAGNOSIS_DICTIONARY_API_CONTRACT.md` (S3, aynı desen). Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `code`.

> **Durum:** Uygulandı (kod + test). Migration `20261014132031_AddExaminationTemplates` dosyası üretildi, **hiçbir veritabanına uygulanmadı**. Yeni izinler var; sistem şablonları ve izin bağları için `DbMigrator seed` gerekir (bölüm 9).

---

## 1) Kavramlar ve kurallar

- **Şablon = kliniğin ön dolum paketi:** ad, opsiyonel şikâyet (`visitReason`), bulgu (`findings`), plan (`plan`) metni ve en çok 20 **işlem satırı** (katalog ürünü veya hizmet + miktar). **Şablonda fiyat yoktur;** fiyat uygulama anında ürün kartından gelir (ADR-018).
- **Kiracının listesi = sistem şablonları + kliniğe özel şablonlar** (S3 sözlük deseni):
  - **Sistem şablonu** (`isSystem = true`): tüm kiracılarda görünür, seed ile gelir, **salt okunur**, **yalnız metin** (kiracıdan bağımsız ürün bağı uydurulmaz). Başlangıç: "Rutin muayene", "Aşı muayenesi", "Kontrol muayenesi" — sonuç yazmayan boş başlıklar (ör. `Genel durum: `), hekim doldurur.
  - **Kliniğe özel şablon** (`isSystem = false`): eklenir, tam güncellenir, pasifleştirilir/etkinleştirilir. Silme yoktur. Sistem şablonunu uyarlamak için aynı içerikle **farklı adla** kendi şablonunuzu oluşturun.
- **Ad:** kırpılır, art arda boşluk teke iner; 1–200 karakter. **Mükerrer:** kiracının görünür listesinde (sistem + kendi, **pasifler dahil**) Türkçe duyarsız aynı ad bir kez bulunur (`TurkishTextKey`, S3 ile aynı kural) → `409`.
- **Metin sınırları:** şikâyet ≤ 500, bulgu ve plan ≤ 4000. Şablonda en az bir metin **veya** bir satır olmalı.
- **Satır kuralları** (ekle/güncelle ve uygula anında yeniden denetlenir):
  - Ürün kiracının kataloğunda olmalı (`Products.NotFound`, 404) ve **aktif** olmalı (`ExaminationTemplates.ProductInactive`, 400).
  - **Aşı ürünü satır olamaz** (`ExaminationTemplates.VaccineNotAllowed`, 400): aşı yalnız aşı kaydı ekranından girilir (sonraki doz/hatırlatma o kayıttan doğar).
  - Hizmet (`Service`) ve kontrollü ürün serbesttir; stok, parti, narkotik ve ücret kuralları **uygulamada** mevcut akışla işler (bölüm 7).
  - Aynı ürün şablonda bir kez yer alır (miktarı artırın). Miktar `> 0`, ≤ 100000, en çok 3 ondalık, ürünün **stok birimi** cinsinden (uygulama kaydıyla aynı kural).
  - Satır sırası = gönderilen liste sırası.
- Satır kartın güncel hâliyle döner: ürün sonradan pasifleşirse satır `productIsActive = false` görünür, şablon **uygulanamaz** (satırı çıkarıp kaydedin).

### `ExaminationTemplateDto`

| Alan | Tip | Not |
|---|---|---|
| `id` | guid | |
| `name` | string | |
| `visitReason` | string \| null | Şikâyete eklenecek metin |
| `findings` | string \| null | Bulguya eklenecek metin (çok satırlı olabilir) |
| `plan` | string \| null | Plana eklenecek metin |
| `isActive` | bool | |
| `isSystem` | bool | `true` ise salt okunur (düzenle/pasifleştir düğmelerini gizleyin) |
| `lines` | `ExaminationTemplateLineDto[]` | Sırayla; sistem şablonunda boş |
| `rowVersion` | string | Base64 (8 bayt); PUT için zorunlu |

### `ExaminationTemplateLineDto` (ürün alanları kartın **güncel** hâli)

| Alan | Tip | Not |
|---|---|---|
| `productId` | guid | |
| `productName` | string | |
| `unit` | string | Stok birimi |
| `productKind` | string | `"Other"` \| `"Medicine"` \| `"Vaccine"` \| `"Service"`; `Service` stok düşmez |
| `productIsActive` | bool | `false` ise şablon uygulanamaz |
| `quantity` | decimal | |
| `currentUnitPrice` | decimal | Kartın bugünkü fiyatı (önizleme; şablonda saklanmaz) |
| `currency` | string | |

---

## 2) Listele / ara — `GET /api/v1/examination-templates`

İzin: `ExaminationTemplates.Read`. Kiracı bağlamı zorunlu.

| Sorgu | Tip | Varsayılan | Not |
|---|---|---|---|
| `search` | string | — | Türkçe duyarsız; boşlukla ayrılmış **her kelime** adda geçmeli (en çok 5 kelime; S3 sözlükle aynı kural) |
| `includeInactive` | bool | `false` | Seçici `false`; ayarlar ekranı `true` |
| `page` | int | 1 | |
| `pageSize` | int | 20 | 1–200 |

Sıra: ada göre (sistem ve kendi şablonları karışık). `sort`/`order` uygulanmaz. Yanıt `200` `PagedResult<ExaminationTemplateDto>` (satırlar dahil; seçicide önizleme için ek çağrı gerekmez).

## 3) Tek şablon — `GET /api/v1/examination-templates/{id}`

İzin: `ExaminationTemplates.Read`. Sistem veya kendi şablonu (pasif dahil). Başka kiracınınki `404 ExaminationTemplates.NotFound`.

## 4) Ekle — `POST /api/v1/examination-templates`

İzin: `ExaminationTemplates.Create`. `lines` opsiyonel (yoksa yalnız metin).

```json
{
  "name": "Otitis paketi",
  "visitReason": "Kulak kaşıntısı",
  "findings": "Otoskopi: \nKulak akıntısı: ",
  "plan": "Kontrol: 7 gün",
  "lines": [
    { "productId": "…hizmet: Kulak temizliği…", "quantity": 1 },
    { "productId": "…ilaç: kulak damlası…", "quantity": 1 }
  ]
}
```

`201` + `Location` + `ExaminationTemplateDto` (aktif, `isSystem = false`). Hatalı satırda ProblemDetails'e **`productId`** eklenir (arayüz satırı işaretler).

## 5) Güncelle — `PUT /api/v1/examination-templates/{id}`

İzin: `ExaminationTemplates.Update`. **Tam güncelleme:** ad, üç metin ve satırlar birlikte yazılır; gönderilmeyen metin `null` olur, **`lines` zorunludur** (boş liste = satırsız; yoksa `400`). `rowVersion` zorunlu; `id` opsiyonel, doluysa rota ile aynı olmalı.

```json
{ "name": "Otitis paketi", "visitReason": "Kulak kaşıntısı", "findings": null, "plan": "Kontrol: 7 gün",
  "lines": [ { "productId": "…", "quantity": 2 } ], "rowVersion": "AAAAAAAAB9I=" }
```

`200` + güncel `ExaminationTemplateDto` (yeni `rowVersion`). Eski `rowVersion` → `409 ExaminationTemplates.ConcurrencyConflict`. Aktiflik ayrı uçlarla değişir.

## 6) Pasifleştir / etkinleştir — `POST /api/v1/examination-templates/{id}/deactivate` · `…/activate`

İzin (ikisi de): `ExaminationTemplates.Deactivate`. Gövde yok, `204`; zaten istenen durumdaysa da `204`. Pasif şablon seçicide görünmez ve **uygulanamaz**; daha önce uygulandığı muayeneler etkilenmez (şablon muayeneye bağlanmaz).

---

## 7) Muayeneye uygula — `POST /api/v1/examinations/{id}/apply-template`

İzin: **`ExaminationTemplates.Apply` + `Examinations.Update` + `Administrations.Create`** (üçü birden; varsayılan rollerde birlikte verilir). Kiracı ve klinik bağlamı muayene güncellemesiyle aynı (atanmış klinik, aktif klinik bağlamı).

```json
{ "templateId": "…", "rowVersion": "AAAAAAAAB9E=" }
```

`rowVersion` **muayenenin** güncel sürümüdür (zorunlu). Tek transaction'da sırayla:

1. **Metin birleştirme** (kural tek yerde, `ExaminationTextMerge`; mevcut içerik **asla silinmez/değiştirilmez**), alan bazında:
   1. Şablon metni boşsa alan aynen kalır.
   2. Alan boşsa şablon metni yazılır.
   3. Alan şablon metnini **zaten içeriyorsa** (Türkçe harf, büyük/küçük, boşluk farkı yok sayılır) alan aynen kalır — aynı şablonu ikinci kez uygulamak metni çoğaltmaz.
   4. Aksi hâlde metnin **sonuna** eklenir: şikâyete `", "` ile, bulgu ve plana yeni satırla (`"\n"`).
   
   Örnek: şikâyet `"Aşı"` + şablon `"Kulak kaşıntısı"` → `"Aşı, Kulak kaşıntısı"`; bulgu `"Bulgu yok"` + `"Otoskopi:"` → `"Bulgu yok\nOtoskopi:"`. Birleşen metin muayene sınırını (şikâyet 2000, bulgu 8000, plan 4000) aşarsa `400 Examinations.TemplateTextTooLong` (hiçbir şey yazılmaz). Diğer muayene alanları (değerlendirme/tanı, notlar, vitaller) değişmez.
2. **Stoklu ürün satırı** → mevcut **uygulama** kaydı (`POST /administrations` ile aynı handler ve kurallar: FEFO parti seçimi, süresi geçmiş parti yasağı, yetersiz bakiye, kontrollü ürün kuralları; muayeneye bağlı, uygulama zamanı = şimdi). Fiyatlıysa "ücret bekliyor" kalemi (`sourceType = "Administration"`) doğar.
3. **Hizmet satırı** → stok yok; katalog fiyatı `> 0` ise "ücret bekliyor" kalemi (`sourceType = "ExaminationService"`, muayene ve vizite bağlı, fiyat kartın o anki fiyatı). Bedelsiz hizmet kalem doğurmaz.

**Hepsi ya da hiçbiri:** herhangi bir satır reddedilirse (ör. `Administrations.InsufficientStock`) metin dahil **hiçbir şey yazılmaz**; ProblemDetails'te reddedilen satırın **`productId`**'si döner. Hekim satırı düzeltip (stok girişi, şablonda miktar) tekrar dener.

**Kesinleşmiş muayeneye uygulanamaz** (`409 Examinations.Finalized`, ADR-013); kesinleşmiş muayeneye işlem gerekiyorsa mevcut "sonradan ekleme" akışı (gerekçeli uygulama) kullanılır.

**Çift tıklama / eşzamanlılık:** her uygulama muayene sürümünü ilerletir. Aynı `rowVersion` ile ikinci istek `409 Examinations.ConcurrencyConflict` alır ve stok düşmez. Güncel sürümle **bilinçli** ikinci uygulama metni çoğaltmaz ama **satırları yeniden yazar** (ikinci uygulama/kalem). Muayene formu açıkken uygulama yapıldıysa form yanıttaki `rowVersion` ile devam etmeli (aksi hâlde formun sonraki PUT'u 409 alır); en sade yol: uygulamadan önce formu kaydetmek, sonra muayeneyi yeniden okumak.

Yanıt `200`:

```json
{
  "examinationId": "…",
  "rowVersion": "AAAAAAAAB9M=",
  "updatedFields": ["visitReason", "findings", "plan"],
  "administrations": [ { "id": "…", "productId": "…", "quantity": 1, "batchId": "…", "status": "Active", "…": "AdministrationDto" } ],
  "services": [ { "productId": "…", "productName": "Kulak temizliği", "unit": "Adet", "quantity": 1, "chargeSuggestionId": "…" } ]
}
```

| Alan | Not |
|---|---|
| `rowVersion` | Muayenenin **yeni** sürümü; sonraki muayene PUT/kesinleştirme bununla |
| `updatedFields` | Metni değişen alanlar (`visitReason`, `findings`, `plan`); boş olabilir |
| `administrations` | Yazılan uygulama kayıtları (`AdministrationDto`, `ADMINISTRATIONS_API_CONTRACT.md`) |
| `services` | Uygulanan hizmet satırları; `chargeSuggestionId` bedelsiz hizmette `null` |

Arayüz uygulamadan sonra muayeneyi (`GET /examinations/{id}`) ve ilişkili özeti (`…/related-summary`) yeniden okur.

---

## 8) Hata kodları

| HTTP | `code` | Ne zaman |
|---|---|---|
| 400 | `Validation.FluentValidation` | Boş/uzun ad veya metin, 20+ satır, miktar ≤ 0, PUT'ta `lines` yok, sayfa sınırı |
| 400 | `ExaminationTemplates.Validation` | `rowVersion` yok/geçersiz; ad/metin/satır domain kuralı (içerik yok, aynı ürün iki kez, miktar ölçeği) |
| 400 | `ExaminationTemplates.SystemTemplateReadOnly` | Sistem şablonunu güncelleme/pasifleştirme/etkinleştirme |
| 400 | `ExaminationTemplates.VaccineNotAllowed` / `ExaminationTemplates.ProductInactive` | Satır kuralı (+ `productId`) |
| 400 | `ExaminationTemplates.RouteIdMismatch` | PUT gövde `id` ≠ rota |
| 400 | `ExaminationTemplates.Inactive` | Pasif şablonu uygulama |
| 400 | `Examinations.Validation` | Uygula: muayene `rowVersion` yok/geçersiz |
| 400 | `Examinations.TemplateTextTooLong` | Uygula: birleşen metin muayene sınırını aşıyor |
| 400 | `Administrations.*` (ör. `InsufficientStock`, `BatchExpired`) | Uygula: satırın uygulama kaydı reddedildi (+ `productId`); kodlar `ADMINISTRATIONS_API_CONTRACT.md` ile aynı |
| 403 | — / `Subscriptions.TenantReadOnly` / `Tenants.TenantInactive` | İzin yok / salt okunur abonelik / pasif kiracı |
| 404 | `ExaminationTemplates.NotFound` | Yok veya başka kiracının |
| 404 | `Products.NotFound` | Satır ürünü yok veya başka kiracının (+ `productId`) |
| 404 | `Examinations.NotFound` | Uygula: muayene yok |
| 409 | `ExaminationTemplates.DuplicateName` | Aynı ad (Türkçe duyarsız, sistem ve pasif dahil) |
| 409 | `ExaminationTemplates.ConcurrencyConflict` | Şablon eski `rowVersion` |
| 409 | `Examinations.Finalized` | Uygula: kesinleşmiş muayene |
| 409 | `Examinations.ConcurrencyConflict` | Uygula: muayene eski `rowVersion` (çift tıklama dahil) |

## 9) İzinler ve seed

| İzin | Admin | Owner | ClinicAdmin | Veteriner | Sekreter |
|---|---|---|---|---|---|
| `ExaminationTemplates.Read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `ExaminationTemplates.Create` | ✓ | ✓ | ✓ | ✓ | — |
| `ExaminationTemplates.Update` | ✓ | ✓ | ✓ | ✓ | — |
| `ExaminationTemplates.Deactivate` | ✓ | ✓ | ✓ | — | — |
| `ExaminationTemplates.Apply` | ✓ | ✓ | ✓ | ✓ | — |

Hekim kendi paketini oluşturup düzeltir ve uygular; listeyi sadeleştirme (pasifleştirme) yönetici düzeyindedir. Sekreter yalnız okur.

`DbMigrator seed` (migration uygulandıktan sonra): `PermissionSeeder` yeni izinleri, `RolePermissionBindingSeeder` rol bağlarını, `ExaminationTemplateSeeder` (`DataSeeder` içinde) 3 sistem şablonunu **idempotent** ekler; mevcut satırlar değişmez. Seed öncesi liste yalnız kliniğin kendi şablonlarını döner (hata değil).

## 10) Bu dilimde yok

Sistem şablonunu kiracı bazında gizleme/kopyalama ucu (istemci içeriği kopyalayıp yeni adla POST edebilir); tür/ırk filtresi; şablonda tanı (S3 sözlüğü serbest metin olarak `assessment`'a yazılır, şablon değerlendirme alanına dokunmaz); aşı satırı; şablonda fiyat/indirim; uygulamayı geri alma ucu (yazılan uygulama mevcut iptal akışıyla, hizmet kalemi "ücretsiz say" ile kapatılır); hangi şablonun uygulandığının muayenede izi (audit kaydında `ExaminationTemplate.Apply` olarak bulunur).
