# Tanı/şikâyet sözlüğü API sözleşmesi — ADR-020 S3

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-020-clinic-staff-centered-patient-card.md` (karar 4-5, dilim S3), `ux/HEDEF-IS-AKISLARI-2026-10.md` (P7). Uçlar `api/v1/clinical-terms` altında; hata zarfı standart `ProblemDetails` + `code` (bkz. `BACKEND-CONTRACT-STANDARD.md`).

> **Durum:** Uygulandı (kod + test). Migration `20261014115821_AddClinicalTerms` dosyası üretildi, **hiçbir veritabanına uygulanmadı**. Yeni izinler var; sistem sözlüğü ve izin bağları için `DbMigrator seed` gerekir (bölüm 6).

---

## 1) Kavramlar ve kurallar

- **İki tür:** `Diagnosis` (tanı) ve `Complaint` (şikâyet / geliş nedeni). Tür kayıttan sonra değişmez.
- **Kiracının listesi = sistem varsayılanları + kliniğe özel kayıtlar** (mevcut aşı kataloğu deseni, `VaccineDefinition`):
  - **Sistem kaydı** (`isSystem = true`): tüm kiracılarda görünür, seed ile gelir, **salt okunur** (ad düzeltme / pasifleştirme `400 ClinicalTerms.SystemTermReadOnly`).
  - **Kliniğe özel kayıt** (`isSystem = false`): yalnız o kiracıda görünür; eklenir, adı düzeltilir, pasifleştirilir/etkinleştirilir. Silme yoktur.
- **Mükerrer kuralı:** bir kiracının görünür listesinde (sistem + kendi, **pasifler dahil**) aynı **tür + ad** bir kez bulunur. Ad karşılaştırması Türkçe duyarlıdır ve büyük/küçük harf, `I/ı/İ/i`, `ç/ğ/ö/ş/ü`, `â/î/û` farkı ile fazla boşluğu yok sayar (müşteri mükerrer anahtarıyla **aynı kural**, `TurkishTextKey`). Örnek: `"GASTROENTERİT"` sistemdeki `"Gastroenterit"` ile çakışır → `409`. Pasif kendi kaydıyla aynı adı yeniden eklemek yerine kaydı **etkinleştirin**. Aynı ad diğer türde serbesttir.
- **Ad:** kırpılır, art arda boşluk teke iner; 1–200 karakter.
- **Pasif kayıt** seçicide (varsayılan liste) görünmez; muayene metinleri etkilenmez.

### Muayeneyle kullanım (bilinçli seçim: yalnız arama kaynağı)

Sözlük **muayene kaydına bağlanmaz**; muayene uçları ve şeması **değişmedi**. Frontend seçilen kaydın `name` değerini mevcut serbest metin alanına yazar:

| Tür | Muayene alanı (değişmedi, `EXAMINATIONS_WORKSPACE_API_CONTRACT.md`) |
|---|---|
| `Complaint` | `visitReason` |
| `Diagnosis` | `assessment` |

Birden çok seçimi alanda ayırmak (ör. `", "` ile eklemek) ve elle yazmaya devam etmek frontend tercihidir; serbest metin her zaman geçerlidir. **Gerekçe:** en sade ve tam geriye uyumlu yol — muayene migration'ı, kısmi PUT kuralı (null = dokunma) ve kesinleştirme kilidi (ADR-013) etkilenmez; sözlük kaydı sonradan düzeltilse/pasifleşse bile kesinleşmiş muayene metni değişmez. Yapılandırılmış tanı bağı (tanı istatistiği, hasta kartında "açık tanılar" listesi) bu dilimde **yok**; gerekirse ayrı karar ve dilimle eklenir.

### `ClinicalTermDto`

| Alan | Tip | Not |
|---|---|---|
| `id` | guid | |
| `kind` | string | `"Diagnosis"` \| `"Complaint"` |
| `name` | string | Gösterilecek ve muayene alanına yazılacak metin |
| `isActive` | bool | |
| `isSystem` | bool | `true` ise salt okunur (düzenle/pasifleştir düğmelerini gizleyin) |
| `rowVersion` | string | Base64 (8 bayt); PUT için zorunlu |

---

## 2) Listele / ara — `GET /api/v1/clinical-terms`

İzin: `ClinicalTerms.Read`. Kiracı bağlamı zorunlu.

| Sorgu | Tip | Varsayılan | Not |
|---|---|---|---|
| `kind` | `Diagnosis` \| `Complaint` | — (ikisi) | Geçersiz değer `400` |
| `search` | string | — | Türkçe duyarsız; boşlukla ayrılmış **her kelime** adda geçmeli (sıra serbest, en çok 5 kelime). `"ishal kanli"` → `"Kanlı ishal"` |
| `includeInactive` | bool | `false` | Seçici `false` kullanır; ayarlar ekranı `true` |
| `page` | int | 1 | |
| `pageSize` | int | 20 | 1–200 |

`sort` / `order` uygulanmaz. **Sıra:** arama varsa adı ilk kelimeyle **başlayanlar önce** (`"ishal"` → `"İshal"`, sonra `"Kanlı ishal"`), sonra ada göre; arama yoksa ada göre.

Yanıt `200` `PagedResult<ClinicalTermDto>`:

```json
{
  "items": [
    { "id": "…", "kind": "Complaint", "name": "İshal", "isActive": true, "isSystem": true, "rowVersion": "AAAAAAAAB9E=" },
    { "id": "…", "kind": "Complaint", "name": "Kanlı ishal", "isActive": true, "isSystem": false, "rowVersion": "AAAAAAAAB9I=" }
  ],
  "page": 1, "pageSize": 20, "totalItems": 2, "totalPages": 1
}
```

## 3) Tek kayıt — `GET /api/v1/clinical-terms/{id}`

İzin: `ClinicalTerms.Read`. Sistem kaydı veya kendi kaydı döner (pasif dahil). Başka kiracının kaydı `404 ClinicalTerms.NotFound`.

## 4) Ekle — `POST /api/v1/clinical-terms`

İzin: `ClinicalTerms.Create`. Gövde:

```json
{ "kind": "Diagnosis", "name": "Kulak akıntısı" }
```

`201` + `Location` + `ClinicalTermDto` (aktif, `isSystem = false`). Hızlı akış: seçicide sonuç yoksa "‘…’ ekle" ile bu uç çağrılıp dönen `name` alana yazılabilir.

## 5) Ad düzelt — `PUT /api/v1/clinical-terms/{id}`

İzin: `ClinicalTerms.Update`. Gövde (`id` opsiyonel, doluysa rota ile aynı olmalı):

```json
{ "name": "Otitis externa", "rowVersion": "AAAAAAAAB9I=" }
```

`200` + güncel `ClinicalTermDto` (yeni `rowVersion`). Eski `rowVersion` → `409 ClinicalTerms.ConcurrencyConflict`: listeyi yenileyip tekrar deneyin. Yalnız ad değişir; aktiflik ayrı uçlarla.

## 6) Pasifleştir / etkinleştir — `POST /api/v1/clinical-terms/{id}/deactivate` · `…/activate`

İzin (ikisi de): `ClinicalTerms.Deactivate`. Gövde yok, `204`. Zaten istenen durumdaysa da `204` (idempotent). Sürüm istemez.

---

## 7) Hata kodları

| HTTP | `code` | Ne zaman |
|---|---|---|
| 400 | `Validation.FluentValidation` | Boş/200+ ad, sayfa sınırı |
| 400 | (model bağlama) | Tanımsız `kind` metni (sorgu veya gövde) |
| 400 | `ClinicalTerms.Validation` | `rowVersion` yok/geçersiz; ad kuralı |
| 400 | `ClinicalTerms.SystemTermReadOnly` | Sistem kaydını düzeltme/pasifleştirme/etkinleştirme |
| 400 | `ClinicalTerms.RouteIdMismatch` | PUT gövde `id` ≠ rota |
| 403 | — / `Subscriptions.TenantReadOnly` | İzin yok / salt okunur abonelik (yazma) |
| 404 | `ClinicalTerms.NotFound` | Yok veya başka kiracının |
| 409 | `ClinicalTerms.DuplicateName` | Aynı tür + ad (Türkçe duyarsız, sistem ve pasif dahil) |
| 409 | `ClinicalTerms.ConcurrencyConflict` | Eski `rowVersion` / eşzamanlı yazım |

## 8) İzinler ve seed

| İzin | Admin | Owner | ClinicAdmin | Veteriner | Sekreter |
|---|---|---|---|---|---|
| `ClinicalTerms.Read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `ClinicalTerms.Create` | ✓ | ✓ | ✓ | ✓ | — |
| `ClinicalTerms.Update` | ✓ | ✓ | ✓ | ✓ | — |
| `ClinicalTerms.Deactivate` | ✓ | ✓ | ✓ | — | — |

Hekim muayene sırasında eksik terimi ekleyip adını düzeltebilir; listeyi sadeleştirme (pasifleştirme) yönetici düzeyindedir. Sekreter yalnız okur (geliş nedenini seçmek için).

`DbMigrator seed` (migration uygulandıktan sonra): `PermissionSeeder` yeni izinleri, `RolePermissionBindingSeeder` rol bağlarını, `ClinicalTermSeeder` (`DataSeeder` içinde) sistem sözlüğünü (30 şikâyet, 43 tanı; yaygın genel Türkçe terimler) **idempotent** ekler; mevcut satırlar değişmez. Seed öncesi liste boş döner (hata değil).

## 9) Bu dilimde yok

Sistem kaydını kiracı bazında gizleme; tür/ırk filtresi; kod (VeNom vb.); silme; muayeneye yapılandırılmış tanı bağı ve tanı istatistiği; şablonlar (S4).
