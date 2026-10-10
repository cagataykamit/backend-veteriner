# Muayene çalışma alanı API sözleşmesi

Backend tek doğruluk kaynağıdır. Tüm endpoint’ler `api/v1` altında, JSON gövde/query, standart `ProblemDetails` + `extensions.code` hata zarfı.

**Yetki anahtarları**

| İşlem | Policy / claim |
|--------|----------------|
| Oluşturma | `Examinations.Create` |
| Güncelleme | `Examinations.Update` |
| Okuma (detay, liste, related-summary) | `Examinations.Read` |

Klinik kapsamı: JWT/header `clinic_id` ve istek `clinicId` uyumu mevcut kurallarla zorunlu. Liste için ayrıca klinik kapsamı şart (`Examinations.ClinicScopeRequired`).

**Hasta / sahip başlığı (mevcut endpoint’ler)**

| Endpoint | Amaç |
|----------|------|
| `GET /api/v1/examinations/{id}` | Muayene detayı; `petName`, `clientId`, `clientName` dahil |
| `GET /api/v1/pets/{id}` | Hasta detayı (tenant + erişim kontrollü) |
| `GET /api/v1/clients/{id}` | Sahip detayı |

Yeni özet endpoint eklenmedi.

---

## Kayıt sürümü (concurrency)

- SQL Server `rowversion` → API’de **Base64** (8 bayt), alan adı: `rowVersion`.
- `GET /examinations/{id}` yanıtında zorunlu.
- `PUT /examinations/{id}` isteğinde **zorunlu**:
  - eksik/boş → `400` + `Validation.FluentValidation` (alan hatası `rowVersion`, `ValidationProblemDetails`);
  - dolu ama geçersiz Base64 / 8 bayt değil → `400` + `Examinations.Validation`.
- Eşzamanlı güncelleme çakışması → `409` + `Examinations.ConcurrencyConflict`.
- Başarılı `POST` / `PUT` yanıt gövdesi: `ExaminationWriteResultDto` (`id`, `rowVersion`).

### `409` sonrası akış

1. Sunucu çakışmada kaydı **değiştirmez**; veriler birleştirilmez.
2. İstemci `GET /api/v1/examinations/{id}` ile güncel kaydı ve **yeni** `rowVersion` değerini alır.
3. Kullanıcının bekleyen değişikliklerini güncel kayıt üzerine uygular (yalnızca değiştirdiği alanları göndermek, başkasının yazdığı alanların ezilmesini önler).
4. Yeni `rowVersion` ile PUT’u yeniden dener. Aynı eski `rowVersion` ile tekrar denemek yine `409` döner.

Çakışma tespiti veritabanı `rowversion` belirtecine dayanır; iki oturumun aynı eski sürümle yazması entegrasyon testinde (DbContext düzeyinde) doğrulandı, HTTP uçtan uca çağrı yapılmadı.

---

## `POST /api/v1/examinations`

**Yetki:** `Examinations.Create`

### İstek gövdesi

| Alan | Tip | Zorunlu | Null |
|------|-----|---------|------|
| `clinicId` | uuid | Randevu yoksa evet* | - |
| `petId` | uuid | Randevu yoksa evet* | - |
| `appointmentId` | uuid | Hayır | omit/null |
| `visitId` | uuid | Hayır | omit/null |
| `examinedAtUtc` | datetime (ISO-8601) | Evet | - |
| `visitReason` | string | Evet (boşluk olamaz) | - |
| `complaint` | string | Hayır (legacy; `visitReason` boşsa kullanılır) | - |
| `anamnesis` | string | Hayır | omit/null |
| `findings` | string | Hayır | omit → `""` |
| `weightKg` | number | Hayır | null = ölçülmedi |
| `temperatureC` | number | Hayır | null |
| `heartRateBpm` | int | Hayır | null |
| `respiratoryRatePerMin` | int | Hayır | null |
| `vitalsMeasuredAtUtc` | datetime | Hayır | null; yalnızca en az bir vital ile |
| `assessment` | string | Hayır | omit/null |
| `plan` | string | Hayır | omit/null |
| `notes` | string | Hayır | omit/null |

\* `appointmentId` verildiğinde klinik/hasta randevudan doğrulanır; istekteki `clinicId`/`petId` uyumsuzsa `Examinations.AppointmentPetClinicMismatch`.

Vital kuralları: değerler `> 0`; üst sınır/uyarı eşiği yok. İlk vital girişinde `vitalsMeasuredAtUtc` yoksa `examinedAtUtc` kullanılır; sonradan yalnızca `examinedAtUtc` değişince ölçüm zamanı otomatik kaymaz.

### Örnek istek

```json
{
  "clinicId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "petId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "examinedAtUtc": "2026-10-08T10:30:00Z",
  "visitReason": "İştahsızlık",
  "findings": "",
  "anamnesis": "3 gündür az yiyor",
  "weightKg": 4.25,
  "temperatureC": 38.6,
  "assessment": "Gastroenterit şüphesi",
  "plan": "Sıvı tedavisi, kontrol 48s"
}
```

### Yanıt `201 Created`

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "rowVersion": "AQIDBAUGBwgJCgs="
}
```

`Location`: `GET /api/v1/examinations/{id}`

**Randevu etkisi (mevcut davranış):** `appointmentId` ile oluşturma ve randevu `Scheduled` ise randevu `Complete()` ile tamamlanır; iptal randevuda `Examinations.AppointmentCancelled`.
**Geliş (Visit) bağlantısı:** `visitId` verilirse klinik, hayvan ve randevu Visit'ten türetilir (hasta tekrar seçilmez); istekteki `clinicId`/`petId`/`appointmentId` Visit ile uyuşmazsa `400` + `Examinations.VisitMismatch`. Visit bulunamazsa `404` + `Visits.NotFound`; tamamlanmış veya yanlış geliş işaretli Visit için `409` + `Visits.NotOpen`. Visit `Waiting` ise muayene ile aynı işlemde `InProgress` olur. Randevuyu otomatik tamamlama davranışı Visit'ten bağımsız aynen sürer. `PUT` `visitId`'yi değiştirmez. Aynı Visit’e ikinci muayene engellenmez (K4 kapsam dışı); Visit’in mevcut muayenesini bulmak için `GET /examinations?clinicId=…&visitId=…` kullanılır. Ayrıntı: `VISITS_API_CONTRACT.md`.

---

## `GET /api/v1/examinations/{id}`

**Yetki:** `Examinations.Read`

### Yanıt `200`

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "tenantId": "...",
  "clinicId": "...",
  "petId": "...",
  "petName": "Pamuk",
  "clientId": "...",
  "clientName": "Ali Veli",
  "appointmentId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "visitId": null,
  "examinedAtUtc": "2026-10-08T10:30:00Z",
  "visitReason": "İştahsızlık",
  "anamnesis": "3 gündür az yiyor",
  "findings": "",
  "weightKg": 4.25,
  "temperatureC": 38.6,
  "heartRateBpm": null,
  "respiratoryRatePerMin": null,
  "vitalsMeasuredAtUtc": "2026-10-08T10:30:00Z",
  "assessment": "Gastroenterit şüphesi",
  "plan": "Sıvı tedavisi",
  "notes": null,
  "rowVersion": "AQIDBAUGBwgJCgs=",
  "createdAtUtc": "2026-10-08T10:35:00Z",
  "updatedAtUtc": null
}
```

Eski kayıtlar: yeni alanlar `null` / `findings` `""`; `rowVersion` migration sonrası dolu.

---

## `PUT /api/v1/examinations/{id}`

**Yetki:** `Examinations.Update`

### İstek

`POST` ile aynı klinik alanları + zorunlu `rowVersion` + isteğe bağlı `clearVitals` (aşağıda). PUT **kısmi güncellemedir**.

**Her zaman gönderilmesi gerekenler:** `rowVersion`, `examinedAtUtc`, `visitReason` (veya legacy `complaint`).

**Alan kuralı (null = dokunma)**

| Alan | Gövdede yok / `null` | `""` veya yalnızca boşluk | Dolu değer |
|------|----------------------|---------------------------|------------|
| `findings` | mevcut değer korunur | temizlenir → kayıtta `""` | kaydedilir (baş/son boşluk kırpılır) |
| `anamnesis`, `assessment`, `plan`, `notes` | mevcut değer korunur | temizlenir → `null` | kaydedilir (kırpılır) |
| `weightKg`, `temperatureC`, `heartRateBpm`, `respiratoryRatePerMin` | mevcut değer korunur | — (sayısal alan) | kaydedilir; `> 0` olmalı |
| `vitalsMeasuredAtUtc` | mevcut ölçüm zamanı korunur | — | kaydedilir; yalnızca güncelleme sonrası en az bir vital varsa |

Gönderilmeyen alan hiçbir zaman silinmez/ezilmez.

**Vitalleri bilerek temizleme:** `clearVitals: true` → dört vital değer **ve** `vitalsMeasuredAtUtc` temizlenir (`null`). Metin alanları etkilenmez. `clearVitals: true` ile birlikte herhangi bir vital değer veya `vitalsMeasuredAtUtc` gönderilirse `400` + `Examinations.Validation` (kayıt değişmez). Tek bir vitali temizlemek desteklenmez; yalnızca tümü.

**Ölçüm zamanı:** vitaller yokken ilk kez vital girilirse ve `vitalsMeasuredAtUtc` verilmediyse güncellenmiş `examinedAtUtc` kullanılır; vital zaten varsa mevcut ölçüm zamanı korunur (yalnızca `examinedAtUtc` değişince kaymaz).

**Gövde alanları (doğrulanmış, kodda):**

- `id` (gövdede, isteğe bağlı): doluysa ve route `id` ile farklıysa `400` + `Examinations.RouteIdMismatch`. Boş/`null`/aynı ise yok sayılır.
- `complaint` (legacy): `visitReason` boş/boşluksa `visitReason` yerine `complaint` kullanılır; ikisi de doluysa `visitReason` önceliklidir. İkisi de boşsa `400` (`Validation.FluentValidation`, alan `visitReason`). Yanıtlarda yalnızca `visitReason` döner.

**İlişki kuralları**

- `appointmentId` omit/null → mevcut randevu bağlantısı **korunur** (silinmez).
- `appointmentId` mevcut değerden farklı → `400` + `Examinations.AppointmentChangeNotAllowed`.
- `clinicId` / `petId` mevcut kayıttan farklı → `400` + `Examinations.Validation` (değiştirilemez).

### Örnek (kısmi: yalnızca bulgular değişir; diğer alanlar korunur)

```json
{
  "examinedAtUtc": "2026-10-08T11:00:00Z",
  "visitReason": "İştahsızlık — güncellendi",
  "findings": "Hafif dehidrasyon",
  "rowVersion": "AQIDBAUGBwgJCgs="
}
```

### Örnek (bilinçli temizleme)

`notes` temizlenir, vitaller temizlenir; `plan`, `anamnesis` vb. korunur.

```json
{
  "examinedAtUtc": "2026-10-08T11:00:00Z",
  "visitReason": "İştahsızlık",
  "notes": "",
  "clearVitals": true,
  "rowVersion": "AQIDBAUGBwgJCgs="
}
```

### Yanıt `200 OK`

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "rowVersion": "BwsMDQ4PEBESExQ="
}
```

---

## `GET /api/v1/examinations` (liste)

**Yetki:** `Examinations.Read`

| Parametre | Açıklama |
|-----------|----------|
| `clinicId` | Opsiyonel (kapsam zorunluluğu geçerli) |
| `petId`, `appointmentId`, `visitId` | Opsiyonel filtre. `visitId` yalnızca o Visit’e bağlı muayeneleri döner; klinik kapsamı (`clinicId` zorunlu, atama kontrolü) ve kiracı filtresi aynen geçerlidir. `Guid.Empty` → `400 Validation.FluentValidation`; başka klinikteki Visit’in muayeneleri kendi klinik kapsamında boş döner. |
| `examinedOnLocalDate` | `yyyy-MM-dd`, **İstanbul** takvim günü → UTC `[start,end)` |
| `dateFromUtc` | Alt sınır **dahil** |
| `dateToUtc` | Üst sınır **hariç** |
| `examinedOnLocalDate` ile `dateFromUtc`/`dateToUtc` | Birlikte kullanılamaz |

İstanbul 2026-10-08 örneği: `examinedOnLocalDate=2026-10-08` → `ExaminedAtUtc >= 2026-10-07T21:00:00Z` ve `< 2026-10-08T21:00:00Z`.

Alternatif (frontend UTC hesaplıyorsa): `dateFromUtc=2026-10-07T21:00:00Z&dateToUtc=2026-10-08T21:00:00Z` — çift dönüşüm yapmayın.

Rapor/export: `GET /api/v1/reports/examinations*` — `from`/`to` UTC, filtre `[from,to)` (to hariç), liste ile uyumlu.

### Sayfalama ve yanıt şekli (doğrulanmış, kodda)

Sorgu: `page` (varsayılan `1`, en az 1), `pageSize` (varsayılan `20`, sunucuda `1..200` aralığına sıkıştırılır), `search` veya `page.search` (metin araması). `sort`/`order` **işlenmez**; sıralama sabit: `examinedAtUtc` azalan, eşitlikte `id` azalan.

```json
{
  "items": [
    {
      "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "clinicId": "...",
      "petId": "...",
      "petName": "Pamuk",
      "clientId": "...",
      "clientName": "Ali Veli",
      "appointmentId": null,
      "visitId": null,
      "examinedAtUtc": "2026-10-08T10:30:00Z",
      "visitReason": "İştahsızlık"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalItems": 1,
  "totalPages": 1
}
```

Liste öğesinde `rowVersion`, `findings` ve vital alanlar **yoktur**; düzenleme için `GET /examinations/{id}` kullanılır. Hayvan/sahip bulunamazsa `petName`/`clientName` boş string, `clientId` boş Guid döner. JSON alan adlarının camelCase olduğu, hata zarfı örnekleriyle aynı serileştirme ayarına dayanır; ayrıca HTTP düzeyinde çalıştırılıp doğrulanmadı.

### Tarih yorumlama (Z’siz değerler)

- **Gövde (`examinedAtUtc`, `vitalsMeasuredAtUtc`)**: sunucu `Kind=Utc` ise olduğu gibi, `Kind=Local` ise UTC’ye çevirerek, `Kind=Unspecified` (ofsetsiz, `Z`’siz metin) ise **UTC kabul ederek** işler (kodda doğrulandı). İstemci her zaman `Z` veya açık ofsetle göndermelidir; ofsetli değerin JSON ayrıştırıcıda hangi `Kind` ile geldiği HTTP düzeyinde doğrulanmadı.
- **Liste sorgusu (`dateFromUtc`, `dateToUtc`)**: değerler olduğu gibi (Kind dönüştürülmeden) filtreye geçer; `Z`’siz değer UTC gibi karşılaştırılır. Ofsetli sorgu değerinin model bağlayıcıda nasıl dönüştüğü doğrulanmadı — `Z` ile gönderin.
- **`examinedOnLocalDate`** `yyyy-MM-dd` biçimindedir ve İstanbul takvim günüdür; saat dilimi bilgisi taşımaz.

---

## Hata kodları (seçilmiş)

| HTTP | code | Durum |
|------|------|--------|
| 400 | `Validation.FluentValidation` | Alan doğrulaması; eksik/boş `rowVersion` dahil |
| 400 | `Examinations.Validation` | Geçersiz `rowVersion` (Base64/8 bayt), `clinicId`/`petId` değiştirme girişimi |
| 400 | `Examinations.DateFilterInvalid` | Tarih filtresi çakışması |
| 403 | `Clinics.AccessDenied` | Klinik yazma/okuma |
| 404 | `Examinations.NotFound` | IDOR-safe bulunamadı |
| 400 | `Examinations.VisitMismatch` | `visitId` ile istekteki klinik/hayvan/randevu uyuşmuyor |
| 404 | `Visits.NotFound` | `visitId` bulunamadı / kliniğe ait değil |
| 409 | `Visits.NotOpen` | Visit tamamlanmış veya yanlış geliş işaretli |
| 409 | `Visits.ConcurrencyConflict` | Visit eşzamanlı güncellendi; istek tekrarlanmalı |
| 409 | `Examinations.ConcurrencyConflict` | Eski `rowVersion` |
| 400 | `Examinations.AppointmentChangeNotAllowed` | Randevu değiştirme |
| 400 | `Examinations.RouteIdMismatch` | PUT gövdesindeki `id` route `id` ile farklı |
| 400 | `Examinations.Validation` | `clearVitals: true` ile vital değer / `vitalsMeasuredAtUtc` birlikte gönderildi |

Örnek ProblemDetails:

```json
{
  "status": 409,
  "title": "Çakışma",
  "detail": "Muayene kaydı eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.",
  "extensions": {
    "code": "Examinations.ConcurrencyConflict",
    "traceId": "...",
    "correlationId": "...",
    "timestampUtc": "2026-10-08T18:00:00Z"
  }
}
```

---

## Koordineli yayın ve bakım (backend ↔ frontend)

| Adım | Aksiyon |
|------|---------|
| 1 | Paylaşılan ortamlarda migration `AddExaminationWorkspaceFieldsAndRowVersion` uygulanır (bakım penceresi veya sıfır-downtime planına göre). |
| 2 | **Yeni backend** deploy edilir. |
| 3 | **Yeni frontend** deploy edilir (`rowVersion` zorunlu PUT, write yanıtı, yeni alanlar). |

**Eski frontend + yeni backend:** `PUT /examinations/{id}` gövdesinde `rowVersion` yoksa FluentValidation → **HTTP 400** + `Validation.FluentValidation` (alan: `rowVersion`, “RowVersion zorunludur.”). Kayıt değişmez. Bu bilinçli sözleşme kırılımıdır; sürümsüz PUT desteklenmez.

**Yeni frontend + eski backend:** Yeni alanlar ve sürüm koruması yok; üretimde bu kombinasyonu hedeflemeyin — önce backend.

**Kısa bakım önerisi:** Eski SPA cache’ini temizleyin veya tek oturumda hem API hem UI güncelleyin; aksi halde kullanıcılar kayıt sırasında 400 görür (PUT).

---

## Geri alma (rollback) — iki ayrı boyut

### A) Uygulama (deploy) geri alma

- **Eski API ikilisini** (önceki container/IIS sürümü) geri koymak, veritabanı şemasını otomatik geri almaz.
- Migration **zaten uygulanmışsa** DB’de `Anamnesis`, `Plan`, vital kolonları ve `RowVersion` kalır; eski kod bu kolonları okuyup yazmayabilir veya EF model uyumsuzluğu yaşayabilir.
- **Sürüm koruması:** Eski backend `rowVersion` zorunluluğunu bilmez; eşzamanlı yazma koruması fiilen devre dışı kalır (son yazan kazanır).
- **Klinik veri:** Yeni sürümle yazılmış anamnez/plan/vital değerleri DB’de durur; eski UI bunları göstermeyebilir ama veri silinmez.

**Önerilen operasyonel geri dönüş:** Önce **uygulama** rollback; şema geri alınacaksa ayrı planlı DB adımı (B). Kolon düşürmeyi varsayılan rollback adımı olarak kullanmayın.

### B) Migration `Down` (şema geri alma)

- `Down` yeni kolonları ve `RowVersion`’ı **DROP** eder → **veri kaybı** (yeni alanlardaki içerik gider).
- Üretimde yalnızca felaket kurtarma veya bilinçli şema gerilemesi senaryosunda, yedek sonrası değerlendirilir.
- `Down` çalıştırmadan önce: tam DB yedekği, frontend/backend sürümünün şemayla uyumu, rapor/export ihtiyaçları.

**Kod durumu:** repoda uygulandı. Migration `AddExaminationWorkspaceFieldsAndRowVersion` yerel geliştirme veritabanına (sunucu `DESKTOP-2U2UUHO`, `VetinityCommandDb`) 2026-10-09’da `DbMigrator migrate` ile uygulandı. Paylaşılan, staging ve canlı ortamlara **uygulanmadı**.

---

## Frontend için değişiklik özeti (kısmi PUT ve `clearVitals`)

Bu değişiklik sözleşmeyi etkiler: yeni `clearVitals` alanı eklendi ve gönderilmeyen alanların anlamı değişti.

- **Eski davranış:** PUT’ta gönderilmeyen (`null`) alanlar kayıttan silinirdi; istemci formdaki tüm alanları göndermek zorundaydı.
- **Yeni davranış:** gönderilmeyen alan korunur. Yalnızca değişen alanları göndermek güvenlidir.
- **Her istekte gönderin:** `rowVersion`, `examinedAtUtc`, `visitReason`.
- **Metni silmek için** alanı `""` ile gönderin. `null`/gönderilmeme artık silmez. Form alanını boşaltan kullanıcı için `""` gönderilmelidir.
- **Vitalleri silmek için** `clearVitals: true` gönderin (vital değerlerle birlikte göndermeyin). Tek bir vitali silmek yok; yalnızca tümü.
- **Etki:** tüm alanları hâlâ gönderen mevcut istemci aynı sonucu alır (dolu değerler yazılır). Boş bırakılmış metin alanını `null` ile gönderen istemci artık o alanı **silemez**; `""` göndermesi gerekir. Boş vital alanını `null` ile gönderen istemci vitali silemez; `clearVitals` kullanmalıdır.
- **`409` sonrası:** `GET` ile yeni `rowVersion` alıp yeniden deneyin (bkz. “`409` sonrası akış”).
- Bu davranışın HTTP üzerinden uçtan uca çağrısı yapılmadı; kural domain, handler ve veritabanı düzeyinde test edildi.
