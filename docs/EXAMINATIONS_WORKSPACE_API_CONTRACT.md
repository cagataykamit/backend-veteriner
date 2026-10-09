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
- `PUT /examinations/{id}` isteğinde **zorunlu**; eksik/geçersiz → `400` + `Examinations.Validation`.
- Eşzamanlı güncelleme çakışması → `409` + `Examinations.ConcurrencyConflict`.
- Başarılı `POST` / `PUT` yanıt gövdesi: `ExaminationWriteResultDto` (`id`, `rowVersion`).

---

## `POST /api/v1/examinations`

**Yetki:** `Examinations.Create`

### İstek gövdesi

| Alan | Tip | Zorunlu | Null |
|------|-----|---------|------|
| `clinicId` | uuid | Randevu yoksa evet* | - |
| `petId` | uuid | Randevu yoksa evet* | - |
| `appointmentId` | uuid | Hayır | omit/null |
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

`POST` ile aynı klinik alanları + zorunlu `rowVersion`.

**İlişki kuralları**

- `appointmentId` omit/null → mevcut randevu bağlantısı **korunur** (silinmez).
- `appointmentId` mevcut değerden farklı → `400` + `Examinations.AppointmentChangeNotAllowed`.
- `clinicId` / `petId` mevcut kayıttan farklı → `400` + `Examinations.Validation` (değiştirilemez).

### Örnek (yalnızca klinik metin)

```json
{
  "examinedAtUtc": "2026-10-08T11:00:00Z",
  "visitReason": "İştahsızlık — güncellendi",
  "findings": "Hafif dehidrasyon",
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
| `petId`, `appointmentId` | Opsiyonel filtre |
| `examinedOnLocalDate` | `yyyy-MM-dd`, **İstanbul** takvim günü → UTC `[start,end)` |
| `dateFromUtc` | Alt sınır **dahil** |
| `dateToUtc` | Üst sınır **hariç** |
| `examinedOnLocalDate` ile `dateFromUtc`/`dateToUtc` | Birlikte kullanılamaz |

İstanbul 2026-10-08 örneği: `examinedOnLocalDate=2026-10-08` → `ExaminedAtUtc >= 2026-10-07T21:00:00Z` ve `< 2026-10-08T21:00:00Z`.

Alternatif (frontend UTC hesaplıyorsa): `dateFromUtc=2026-10-07T21:00:00Z&dateToUtc=2026-10-08T21:00:00Z` — çift dönüşüm yapmayın.

Rapor/export: `GET /api/v1/reports/examinations*` — `from`/`to` UTC, filtre `[from,to)` (to hariç), liste ile uyumlu.

---

## Hata kodları (seçilmiş)

| HTTP | code | Durum |
|------|------|--------|
| 400 | `Examinations.Validation` | Doğrulama, geçersiz `rowVersion` |
| 400 | `Examinations.DateFilterInvalid` | Tarih filtresi çakışması |
| 403 | `Clinics.AccessDenied` | Klinik yazma/okuma |
| 404 | `Examinations.NotFound` | IDOR-safe bulunamadı |
| 409 | `Examinations.ConcurrencyConflict` | Eski `rowVersion` |
| 409 | `Examinations.AppointmentChangeNotAllowed` | Randevu değiştirme |

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

**Eski frontend + yeni backend:** `PUT /examinations/{id}` gövdesinde `rowVersion` yoksa FluentValidation/handler → **HTTP 400** + `Examinations.Validation` (“RowVersion zorunludur…”). Kayıt değişmez. Bu bilinçli sözleşme kırılımıdır; sürümsüz PUT desteklenmez.

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

**Kod durumu:** repoda uygulandı; paylaşılan DB migration **bu iş kapsamında çalıştırılmadı**.
