# Klinik metin parçaları API sözleşmesi — EXAM-011 / EXAM-012

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `backlog/feature-backlog.md` (EXAM-011 klinik snippet sistemi, EXAM-012 klinik bazlı yönetim), `research/patterns.md` PATTERN-017, `competitors/daysmart.md` (snippet: `#` ile açılan liste, AI değil). Uçlar `api/v1/clinical-snippets` altında; hata zarfı standart `ProblemDetails` + `code` (bkz. `BACKEND-CONTRACT-STANDARD.md`).

> **Durum:** Uygulandı (kod + test). Migration `20261101182503_AddClinicalSnippets`. Yeni izinler var; rol bağları için `DbMigrator seed` gerekir (bölüm 7).

---

## 1) Kavramlar ve kurallar

- **Metin parçası** = kısayol + başlık + metin. Hekim muayene metin alanında `#` yazınca parçalar listelenir; seçilen parçanın **metni imlecin olduğu yere eklenir** (ör. `#kusma` → kusma anamnez soru seti).
- **Ekleme istemci tarafındadır.** Sunucuda "uygula" ucu yoktur; muayene uçları ve şeması **değişmedi**. Eklenen metin muayenenin serbest metninin parçası olur ve muayeneyle kaydedilir. Parça sonradan düzenlense/pasifleşse de yazılmış metin değişmez; kesinleştirme kilidi (ADR-013) olduğu gibi geçerlidir.
- **Kapsam kiracıdır** (şubeler aynı listeyi paylaşır). Sistem varsayılanı **yoktur**: klinik kendi dilini kendisi yazar (tanı sözlüğünden farkı; sözlük tek terim, parça kliniğin üslubu).
- **Kısayol:** baştaki `#` atılır, Türkçe harfler katlanır ve küçültülür (`#İshal` → `ishal`); 2–30 karakter, yalnız `a-z`, `0-9`, `-` (boşluk yok). Kiracı içinde **tek** (pasifler dahil); çakışma `409 ClinicalSnippets.DuplicateShortcut`.
- **Başlık:** 1–100 karakter (seçicide kısayolun yanında görünür). **Metin:** 1–2.000 karakter, satır sonları korunur (`\n`'e normalleşir), baş/son boşluk kırpılır.
- **Tavan:** kiracı başına en fazla **200** parça (pasifler dahil) → `409 ClinicalSnippets.LimitReached`. Seçici listeyi tek istekte çeker.
- **Silme yoktur;** pasif parça seçicide görünmez.

### Muayene şablonu ile ayrım (karıştırılmasın)

| | Muayene şablonu / tedavi paketi (ADR-020 S4, EXAM-006/007) | Metin parçası (EXAM-011/012) |
|---|---|---|
| Ne yapar | Muayeneye **sunucuda uygulanır**: geliş nedeni/bulgu/plan metni + ürün/hizmet satırları → uygulama kayıtları, "ücret bekliyor" kalemleri, paket fiyatı | Yalnız **metin** ekler; kayıt, stok, ücret üretmez |
| Nereden | Muayene detayında "Şablon uygula" (veya gelişte hazırlık, CHECKIN-002/003) | Herhangi bir uzun muayene metin alanında `#` |
| Hangi alan | Sabit: `visitReason`, `findings`, `plan` | İmlecin olduğu alan: `anamnesis`, `findings`, `assessment`, `plan`, `notes` |
| Kapsam | Sistem + kiracı | Yalnız kiracı |
| İzin | `ExaminationTemplates.*` + `Examinations.Update` + `Administrations.Create` | Kullanmak için yalnız `ClinicalSnippets.Read` (muayeneyi kaydetmek zaten `Examinations.Update` ister) |

Kural: bir muayene **türünün tamamı** (metin + işlemler) şablondur; tekrar eden **soru seti veya cümle** parçadır.

### `ClinicalSnippetDto`

| Alan | Tip | Not |
|---|---|---|
| `id` | guid | |
| `shortcut` | string | Saklanan anahtar (`#`'siz, küçük) |
| `title` | string | |
| `body` | string | Eklenecek metin |
| `isActive` | bool | |
| `createdAtUtc` | datetime | |
| `updatedAtUtc` | datetime? | |
| `rowVersion` | string | Base64 (8 bayt); PUT için zorunlu |

---

## 2) Listele / ara — `GET /api/v1/clinical-snippets`

İzin: `ClinicalSnippets.Read`.

| Sorgu | Tip | Varsayılan | Not |
|---|---|---|---|
| `search` | string | — | Türkçe duyarsız; boşlukla ayrılmış **her kelime** kısayolda **veya** başlıkta geçmeli (en çok 5 kelime). Metinde aranmaz |
| `includeInactive` | bool | `false` | Seçici `false`, ayarlar ekranı `true` |
| `page` | int | 1 | |
| `pageSize` | int | 20 | 1–200 (seçici 200 ile tümünü alır) |

Sıra: arama varsa kısayolu ilk kelimeyle **başlayanlar önce**, sonra kısayola göre; arama yoksa kısayola göre. Yanıt `200 PagedResult<ClinicalSnippetDto>`.

## 3) Tek kayıt — `GET /api/v1/clinical-snippets/{id}`

İzin: `ClinicalSnippets.Read`. Başka kiracının kaydı `404 ClinicalSnippets.NotFound`.

## 4) Ekle — `POST /api/v1/clinical-snippets`

İzin: `ClinicalSnippets.Create`. Gövde `{ "shortcut": "#kusma", "title": "Kusma anamnezi", "body": "Kaç kez?\nİçerik?" }` → `201` + `ClinicalSnippetDto` (aktif doğar).

## 5) Düzenle — `PUT /api/v1/clinical-snippets/{id}`

İzin: `ClinicalSnippets.Update`. Gövde `{ "shortcut", "title", "body", "rowVersion" }` (isteğe bağlı `id` rotayla aynı olmalı). Üç alan da yazılır (kısmi güncelleme yok). Eski `rowVersion` → `409 ClinicalSnippets.ConcurrencyConflict`.

## 6) Pasifleştir / etkinleştir — `POST …/{id}/deactivate` · `…/{id}/activate`

İzin: `ClinicalSnippets.Deactivate` (ikisi için). İdempotent, `204`.

## 7) İzinler ve seed

| İzin | Admin | Owner | ClinicAdmin | Veteriner | Sekreter |
|---|---|---|---|---|---|
| `ClinicalSnippets.Read` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `ClinicalSnippets.Create` | ✓ | ✓ | ✓ | ✓ | — |
| `ClinicalSnippets.Update` | ✓ | ✓ | ✓ | ✓ | — |
| `ClinicalSnippets.Deactivate` | ✓ | ✓ | ✓ | — | — |

Tanı sözlüğüyle aynı dağılım: hekim kendi parçasını ekleyip düzeltir, listeyi sadeleştirmek yönetici düzeyindedir. `DbMigrator seed` izinleri ve rol bağlarını idempotent ekler; özel roller elle almalı.

## 8) Hata kodları

| HTTP | `code` | Ne zaman |
|---|---|---|
| 400 | `Validation.FluentValidation` | Boş alan, uzunluk, sayfa sınırı |
| 400 | `ClinicalSnippets.Validation` | Kısayol biçimi/uzunluğu, başlık/metin kuralı, `rowVersion` yok/geçersiz |
| 400 | `ClinicalSnippets.RouteIdMismatch` | PUT gövde `id` ≠ rota |
| 403 | — / `Subscriptions.TenantReadOnly` | İzin yok / salt okunur abonelik (yazma) |
| 404 | `ClinicalSnippets.NotFound` | Yok veya başka kiracının |
| 409 | `ClinicalSnippets.DuplicateShortcut` | Aynı kısayol (Türkçe duyarsız, pasif dahil) |
| 409 | `ClinicalSnippets.LimitReached` | Kiracıda 200 parça var |
| 409 | `ClinicalSnippets.ConcurrencyConflict` | Eski `rowVersion` |

## 9) Frontend davranışı (öneri, bağlayıcı değil)

- Muayene çalışma alanı açılınca aktif parçalar bir kez çekilir (`pageSize=200`), istemcide süzülür.
- Uzun metin alanında `#` + harfler yazılınca imlecin altında liste açılır (kısayol + başlık); ↑/↓ seç, Enter/Tab ekle, Esc kapat. Seçimde `#sorgu` metni parçanın gövdesiyle **değiştirilir**.
- `#` öncesinde harf/rakam varsa (ör. `C#`, `no#3`) tetiklenmez.
- `ClinicalSnippets.Read` yoksa tetik çalışmaz; metin normal yazılır.

## 10) Bu dilimde yok

Kişiye özel parça, şube bazında gizleme, değişken yer tutucuları (`{hasta}`), parça içinde parça, kullanım istatistiği, sistem varsayılan parçaları, seslendirme/AI (AI-* ayrı ürün kararı).
