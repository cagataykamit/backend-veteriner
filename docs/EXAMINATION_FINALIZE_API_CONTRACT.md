# Muayene kesinleştirme ve ek not (addendum) API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: ADR-005 §9.7, backlog RECORD-007 (ertelendi), `docs/ASAMA4_DISCOVERY.md` dilim D ve E. Tüm uçlar `api/v1` altında, JSON gövde, standart `ProblemDetails` + `extensions.code` hata zarfı (bkz. `EXAMINATIONS_WORKSPACE_API_CONTRACT.md`).

> **Durum:** Sözleşme. Kullanıcı kararları (2026-10-11): D1 seçenek (a) — kesinleştirilen muayene **salt okunur**, düzeltme yalnız **ek not**; yeniden açma ve revizyon anlık görüntüsü **yok** (RECORD-007). D5 — mevcut tüm muayeneler `Draft` kalır, otomatik kilitleme yoktur.

---

## 1) Kavramlar

- **Durum** (`status`): `Draft` (Taslak) → `Finalized` (Kesinleşti). JSON'da **string** enum. Geri dönüş **yoktur** (yeniden açma ertelendi).
- `Draft` muayene bugünkü gibi `PUT /examinations/{id}` ile düzenlenir.
- `Finalized` muayenenin klinik içeriği (başvuru nedeni, anamnez, bulgular, vitaller, değerlendirme, plan, not, muayene zamanı) **değişmez**; `PUT` reddedilir. Düzeltme yalnız **ek not** ile yapılır; ilk metin yerinde kalır.
- **Ek not** (addendum): kesinleşmiş muayeneye eklenen, gerekçeli, **append-only** kayıt. Güncellenemez, silinemez; bunun için uç yoktur.
- Kesinleştirme `Visit` durumunu değiştirmez; `Visit` tamamlanması muayeneyi kesinleştirmez. İkisi ayrı kavramdır.
- Mevcut (migration öncesi) tüm muayeneler `Draft` başlar. Toplu kesinleştirme yapılmaz.
- Muayene silme ucu yoktur. İleride eklenirse kesinleşmiş muayene silinemez (kural `Examination` domain'inde tutulur).

---

## 2) Yetkiler

| İşlem | Policy | Rol bağları (varsayılan seed) |
|-------|--------|-------------------------------|
| Muayeneyi kesinleştirme | `Examinations.Finalize` (**yeni**) | Admin, Owner, ClinicAdmin, Veteriner |
| Ek not yazma | `Examinations.Addendum` (**yeni**) | Admin, Owner, ClinicAdmin, Veteriner |
| Ek notları okuma | `Examinations.Read` (mevcut) | Admin, Owner, ClinicAdmin, Veteriner, Sekreter |

- Sekreter muayeneyi kesinleştiremez ve ek not yazamaz; okuyabilir (muayenenin kendisini zaten okuyabilir).
- Ek not okuma için ayrı izin yoktur (keşif önerisi: yalnız istenirse). Ayrı okuma izni gerekirse `Examinations.ReadRevisions` benzeri bir izin sonradan eklenir.
- Yeni izinler **DbMigrator seed** ile gelir (`PermissionCatalog` + `RolePermissionBindings`); API açılışında eklenmez. **Deploy notu:** özel (elle oluşturulmuş) roller yeni izinleri elle almalıdır (`Visits.*` ile aynı).
- Klinik kapsamı mevcut muayene yazma kuralıyla aynıdır: aktif klinik bağlamı ile muayene kliniği uyumsuzsa `Examinations.ClinicContextMismatch`; atanmamış klinik → `Clinics.AccessDenied`. Kiracı bağlamı yoksa `Tenants.ContextMissing`; pasif kiracı yazması `Tenants.TenantInactive` (403).

---

## 3) Muayeneyi kesinleştir — `POST /api/v1/examinations/{id}/finalize`

`Examinations.Finalize`. İstek:

```json
{ "rowVersion": "AAAAAAAAB9E=" }
```

- `rowVersion` **zorunlu**: istemcinin formu/ekranı açarken `GET /examinations/{id}` ile aldığı sürüm (Base64, 8 bayt). Kesinleştirme, ekrandaki içeriğin son kayıtlı içerik olduğunu doğrular; aradan başka biri düzenlemişse `409`.
- Kesinleştirme için ek alan koşulu yoktur (örn. değerlendirme zorunlu değil); muayene zaten geçerli bir başvuru nedeni taşır.

**200 yanıt:**

```json
{
  "id": "…",
  "rowVersion": "AAAAAAAAB9F=",
  "status": "Finalized",
  "finalizedAtUtc": "2026-10-11T09:30:00Z",
  "finalizedByUserId": "…"
}
```

Kesinleştirmeden sonra istemci yeni `rowVersion`'ı kullanır (ek not yazarken gerekmez).

**Hatalar:**

| Durum | `code` | HTTP |
|-------|--------|------|
| `rowVersion` eksik/geçersiz | `Examinations.Validation` | 400 |
| Muayene yok / başka kiracı / başka klinik bağlamı | `Examinations.NotFound` | 404 |
| Zaten kesinleşmiş | `Examinations.AlreadyFinalized` | **409** |
| Sürüm çakışması (arada düzenlendi) | `Examinations.ConcurrencyConflict` | 409 |
| Klinik erişimi yok | `Clinics.AccessDenied` | 403 |
| İzin yok | — (policy) | 403 |

`AlreadyFinalized` idempotent başarı **değildir**: kesinleştiren kullanıcı ve zaman ilk kesinleştirmede sabitlenir, ikinci çağrı bunları değiştirmez. Çift tıklama koruması istemcide yapılır; ikinci çağrının 409 dönmesi beklenen davranıştır.

---

## 4) Kesinleşmiş muayenede düzenleme — `PUT /api/v1/examinations/{id}`

Muayene `Finalized` ise güncelleme **reddedilir**: `409` + `code = Examinations.Finalized`. İçerik, vital ve zaman alanlarının hiçbiri (kısmi PUT ve `clearVitals` dahil) değiştirilemez. Kural `Examination` domain'indedir; denetim sırası: tenant/izin → bulunma → klinik erişimi → bu kural.

Yeni durum alanlarını `PUT` değiştirmez (gövdede alan yok).

---

## 5) Ek not yaz — `POST /api/v1/examinations/{id}/addenda`

`Examinations.Addendum`. Yalnız `Finalized` muayenede. İstek:

```json
{ "reason": "Kilo ölçümü hatalı girilmişti", "text": "Doğru kilo 12,4 kg; tartı yeniden yapıldı." }
```

| Alan | Kural |
|------|-------|
| `reason` | Zorunlu, kırpılmış 5–500 karakter (gerekçe) |
| `text` | Zorunlu, kırpılmış en çok 4000 karakter (ek not içeriği) |

**201 yanıt** (`Location`: ek not listesi): `ExaminationAddendumDto` (madde 6). Sunucu `createdAtUtc` ve `createdByUserId` atar; istemci göndermez. İdempotency anahtarı yoktur: aynı istek iki kez gönderilirse iki ek not oluşur (çift tıklama koruması istemcide).

**Hatalar:**

| Durum | `code` | HTTP |
|-------|--------|------|
| `reason`/`text` eksik veya sınır dışı | `Examinations.Validation` | 400 |
| Muayene `Draft` | `Examinations.NotFinalized` | **409** (taslak düzenlenir, ek not gerekmez) |
| Muayene yok / başka kiracı / başka klinik bağlamı | `Examinations.NotFound` | 404 |
| Klinik erişimi yok | `Clinics.AccessDenied` | 403 |

Ek not eklemek muayenenin `rowVersion`'ını **değiştirmez** ve `updatedAtUtc`'yi güncellemez.

---

## 6) Ek notları listele — `GET /api/v1/examinations/{id}/addenda`

`Examinations.Read`. Sayfalama yok (hayvan başına ek not sayısı küçüktür). `createdAtUtc` artan (en eski önce), eşitse `id`. Muayene yok/erişilemez → `404 Examinations.NotFound`. Taslak muayenede boş liste döner.

```json
[
  {
    "id": "…",
    "examinationId": "…",
    "reason": "Kilo ölçümü hatalı girilmişti",
    "text": "Doğru kilo 12,4 kg; tartı yeniden yapıldı.",
    "createdAtUtc": "2026-10-11T10:05:00Z",
    "createdByUserId": "…",
    "createdByName": "Dr. Ayşe Yılmaz"
  }
]
```

`createdByName` görünen ad çözümüdür (Visit hekim adı ile aynı kaynak); kullanıcı bulunamazsa `null`.

---

## 7) Mevcut yanıtlarda eklenen alanlar (geriye uyumlu)

`GET /examinations/{id}` (`ExaminationDetailDto`) sonuna eklenir:

| Alan | Tip | Not |
|------|-----|-----|
| `status` | `Draft` \| `Finalized` | |
| `finalizedAtUtc` | datetime? | Taslakta `null` |
| `finalizedByUserId` | guid? | Taslakta `null` |
| `finalizedByName` | string? | Taslakta veya ad çözülemezse `null` |

`GET /examinations` liste satırına (`ExaminationListItemDto`) `status` eklenir. Alan eklemesi geriye uyumludur; eski istemciler yok sayar.

İstemci akışı: `status = Finalized` ise form salt okunur gösterilir, "Ek not ekle" eylemi açılır; kesinleştir eylemi yalnız `Examinations.Finalize` izni olana ve `Draft` iken gösterilir.

---

## 8) Eşzamanlılık

- Kesinleştirme ve içerik güncellemesi aynı `rowVersion` ile korunur. Eşzamanlı `PUT` ve `finalize` yarışında yalnız biri kazanır; kaybeden `Examinations.ConcurrencyConflict` (409) alır, `Finalized` sonrası `PUT` ise `Examinations.Finalized` (409).
- Kesinleşmiş içerik değişmediği için ek not yazarken `rowVersion` gerekmez.

---

## 9) Audit

Muayene komutları artık `IAuditableRequest` ile `AuditLog`'a yazılır (rapor boşluğu kapatıldı):

| Komut | Action | Hedef |
|-------|--------|-------|
| Muayene oluştur | `Examination.Create` | `PetId=…` |
| Muayene güncelle | `Examination.Update` | `ExaminationId=…` |
| Kesinleştir | `Examination.Finalize` | `ExaminationId=…` |
| Ek not yaz | `Examination.AddAddendum` | `ExaminationId=…` |

**Gizlilik:** Genel `AuditLog` istek yükünü JSON saklar ve erişimi klinik kayıt yetkisinden bağımsızdır; bu nedenle klinik metin alanları (başvuru nedeni, anamnez, bulgular, değerlendirme, plan, not, ek not gerekçe ve metni) audit yüküne **yazılmaz** (`JsonIgnore`). Audit "kim, ne zaman, hangi muayene, hangi işlem, başarı/hata" bilgisini tutar; klinik geçmişin yerini tutmaz. Ek notun kendisi kalıcı olarak `ExaminationAddenda` tablosundadır.

---

## 10) Veri modeli (migration)

- `Examinations`: `Status` int NOT NULL (varsayılan 0 = Draft), `FinalizedAtUtc` datetime2 NULL, `FinalizedByUserId` uniqueidentifier NULL. Mevcut satırlar `Draft` olur.
- Yeni tablo `ExaminationAddenda`: `Id`, `TenantId`, `ClinicId`, `ExaminationId`, `Reason` (500), `Text` (4000), `CreatedAtUtc`, `CreatedByUserId`; indeks `(TenantId, ExaminationId, CreatedAtUtc)`. Güncelleme/silme için uygulama yolu yoktur.
- Migration dosyası oluşturulur ama yerel dev veritabanına **uygulanmaz**; paylaşılan/üretim DB ayrı onayla.

---

## 11) Kapsam dışı (bilinçli)

- Yeniden açma, revizyon anlık görüntüsü ve sürüm karşılaştırma (RECORD-007, ertelendi).
- Taslak aşamasındaki düzenleme geçmişi.
- Ek not düzenleme/silme, ek notta ek dosya.
- "Yaşlı taslak" uyarısı; Visit tamamlanırken taslak muayene uyarısı (istemci tarafı, zorunluluk değil).
- Özet/timeline (Aşama 4 okuma grubu) alanları; ayrı dalda yürür.
