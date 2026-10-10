# Visit (geliş) ve Bugün API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-009 (vetinity-product), backlog CHECKIN-006/007. Tüm endpoint'ler `api/v1` altında, JSON gövde/query, standart `ProblemDetails` + `extensions.code` hata zarfı (bkz. `EXAMINATIONS_WORKSPACE_API_CONTRACT.md`).

> **Durum:** Sözleşme taslağı. Uygulama başlamadı. Madde 12'deki açık kararlar netleşmeden ilgili kısımlar kesin sayılmaz.

---

## 1) Kavramlar

- **Appointment** plandır ve **değişmez** (`Scheduled → Completed|Cancelled`). Bu işte Appointment varlığı, durumları ve slot kuralları değişmez.
- **Visit** gerçek gelişi temsil eder. Randevulu (`appointmentId` dolu) veya randevusuz (`appointmentId = null`) olabilir. Randevusuz geliş takvime randevu yazmaz.
- **Bakım durumu** (`careStatus`): `Waiting` (Bekliyor) → `InProgress` (Devam ediyor) → `Completed` (Tamamlandı). JSON'da **string** enum.
- **Ödeme göstergesi** ve **açık yatış göstergesi** bakım durumundan **ayrı** alanlardır; hiçbir zaman `careStatus` değerini etkilemez.
- **Yanlış geliş** (`isVoided`): Visit silinmez; gerekçeyle "yanlış geliş" işaretlenir ve kuyruktan/Bugün'den düşer. Bakım durumundan ayrı bir işarettir.

### Visit alanları (`VisitDto`)

| Alan | Tip | Not |
|------|-----|-----|
| `id` | guid | |
| `clinicId` | guid | |
| `petId` | guid | |
| `appointmentId` | guid? | Randevusuz geliş: `null` |
| `responsibleVeterinarianUserId` | guid? | Opsiyonel; verilirse kiracı üyesi ve o kliniğe atanmış kullanıcı olmalı |
| `arrivedAtUtc` | datetime (UTC, `Z`) | Sunucu saatiyle atanır; istemci göndermez |
| `careStatus` | `Waiting` \| `InProgress` \| `Completed` | |
| `startedAtUtc` | datetime? | `InProgress`'e ilk geçiş |
| `completedAtUtc` | datetime? | `Completed`'a geçiş; düzeltmeyle geri alınırsa `null` |
| `isVoided` | bool | |
| `voidReason` | string? | `isVoided` ise dolu |
| `createdByUserId` | guid | Geliş kaydını oluşturan kullanıcı |
| `createdAtUtc` | datetime | |

`tenantId` yanıtta döndürülmez (bağlamdan çözülür). Eşzamanlılık belirteci istemciye **açılmaz**; çakışma sunucuda çözülür (güncelleme çakışmasında durum yeniden okunur; hedef durum zaten sağlanmışsa başarı döner, aksi halde `Visits.ConcurrencyConflict`).

---

## 2) Yetkiler

| İşlem | Policy | Rol bağları (varsayılan seed) |
|-------|--------|-------------------------------|
| Geliş kaydı oluşturma | `Visits.Create` | Admin, Owner, ClinicAdmin, Veteriner, Sekreter |
| Durum geçişi (başlat/tamamla) | `Visits.Update` | Admin, Owner, ClinicAdmin, Veteriner, Sekreter |
| Okuma (detay, Bugün) | `Visits.Read` | Admin, Owner, ClinicAdmin, Veteriner, Sekreter |
| Düzeltme (geri alma, yanlış geliş) | `Visits.Correct` | Admin, Owner, ClinicAdmin |

- `Visits.Read` Bugün yanıtındaki randevu, ödeme ve yatış göstergelerini de kapsar; ayrıca `Appointments.Read`/`Payments.Read` aranmaz.
- Klinik kapsamı mevcut muayene/randevu kuralıyla aynıdır: JWT/header `clinic_id` ile istek `clinicId` uyumsuzsa `Visits.ClinicContextMismatch`; atanmamış klinik → `Clinics.AccessDenied`.
- Kiracı bağlamı yoksa `Tenants.ContextMissing`; pasif kiracı yazması `Tenants.TenantInactive` (403).

---

## 3) Geliş kaydı — `POST /api/v1/visits`

`Visits.Create`. İstek:

```json
{
  "clinicId": "guid?",
  "petId": "guid?",
  "appointmentId": "guid?",
  "responsibleVeterinarianUserId": "guid?"
}
```

Kurallar:

- **Randevulu:** `appointmentId` dolu. `clinicId`/`petId` randevudan türetilir; gönderilirse randevu ile aynı olmalı, değilse `Visits.AppointmentPetClinicMismatch`. Randevu `Scheduled` olmalı: `Cancelled` → `Visits.AppointmentCancelled`; `Completed` ve o randevu için Visit yoksa → `Visits.AppointmentNotScheduled`.
- **Randevusuz:** `appointmentId` yok; `petId` ve aktif klinik (`clinicId` veya bağlam) zorunlu, aksi halde `Visits.Validation`. Randevu **oluşturulmaz**.
- Hayvan ve klinik kiracıya ait olmalı (`Pets.NotFound`, `Clinics.NotFound`, `Appointments.NotFound`).

### Idempotency (tekrar tıklama)

Aynı isteğin tekrarı ikinci Visit **üretmez**; mevcut Visit döner:

1. İstekteki `appointmentId` için **iptal edilmemiş** (yanlış geliş değil) Visit varsa (durumu `Completed` olsa bile) o Visit döner.
2. Aksi halde hayvanın **aktif** Visit'i varsa (`Waiting` veya `InProgress`, yanlış geliş değil) o Visit döner. Mevcut Visit'in `appointmentId` değeri istekten farklıysa da (ör. randevusuz kayıt açıkken o hayvanın randevusu için geliş denendi) yeni Visit açılmaz; istemci yanıttaki `appointmentId` ile farkı görür (bkz. madde 12, K-A).
3. Benzersizlik veritabanı düzeyinde de korunur (madde 5). Eşzamanlı iki istekten kaybeden, kısıt ihlalini yakalayıp kazanan Visit'i okur ve onu döndürür; istemciye hata gitmez.

Yanıt: `VisitCreateResultDto`

```json
{ "created": true, "visit": { /* VisitDto */ } }
```

- Yeni kayıt: **`201 Created`**, `created: true`, `Location` → `GET /visits/{id}`.
- Mevcut kayıt döndü: **`200 OK`**, `created: false`.

---

## 4) Durum geçişi

Yalnızca ileri yönlü, tek adımlı: `Waiting → InProgress → Completed`. Atlama yok; geri alma yalnızca düzeltmeyle (madde 4.3).

### 4.1 Başlat — `POST /api/v1/visits/{id}/start` (`Visits.Update`)

`Waiting → InProgress`. Gövde yok.

### 4.2 Tamamla — `POST /api/v1/visits/{id}/complete` (`Visits.Update`)

`InProgress → Completed`. Gövde yok. Ödeme eksikliği veya açık yatış bu geçişi **engellemez**.

Her ikisi için:

- **Idempotent:** Visit zaten hedef durumdaysa `200` ve mevcut `VisitDto` (değişiklik/yan etki yok). Eşzamanlı çift istekte kaybeden de aynı sonucu alır.
- Diğer tüm uyumsuz durumlar → `Visits.InvalidStatusTransition` (400).
- Yanlış geliş işaretli Visit → `Visits.NotFound` (kuyrukta yok sayılır).
- Yanıt: `200` + `VisitDto`.

### 4.3 Muayeneyle otomatik başlama

`POST /examinations` isteğine opsiyonel `visitId` eklenir (madde 7). Muayene başarıyla oluşunca Visit `Waiting` ise `InProgress` olur (aynı işlem/transaction). `InProgress` ise değişmez.

---

## 5) Veritabanı düzeyinde benzersizlik

Aynı istek tekrarı ve eşzamanlı istekler iki Visit üretemez:

| Kural | Filtreli benzersiz indeks |
|-------|---------------------------|
| Hayvan başına tek **aktif** Visit | `(TenantId, PetId)` where `CareStatus <> Completed AND VoidedAtUtc IS NULL` |
| Randevu başına tek Visit | `(TenantId, AppointmentId)` where `AppointmentId IS NOT NULL AND VoidedAtUtc IS NULL` |

Yanlış geliş işaretlenen Visit iki kuralın dışına çıkar (aynı hayvan/randevu için yeniden geliş açılabilir).

---

## 6) Düzeltme — `POST /api/v1/visits/{id}/corrections`

`Visits.Correct`. Yanlış geliş veya yanlış durum düzeltmesi. **Gerekçe zorunlu.** İstek:

```json
{
  "reason": "string (zorunlu, 5-500 karakter, trim sonrası)",
  "targetCareStatus": "Waiting | InProgress | Completed | null",
  "markAsMistaken": false
}
```

Kurallar:

- `targetCareStatus` ve `markAsMistaken: true` **tam olarak biri** verilmeli; ikisi de ya da hiçbiri yoksa `Visits.Validation`.
- `targetCareStatus`: herhangi bir bakım durumuna (geri veya ileri) düzeltir. Mevcut durumla aynıysa `Visits.Validation`. `Completed`'dan geri alınırken hayvanın başka aktif Visit'i varsa `Visits.DuplicateActiveVisit` (409).
- `markAsMistaken`: Visit yanlış geliş olarak işaretlenir (`isVoided = true`, `voidReason = reason`); zaten işaretliyse `Visits.NotFound`.
- Yanlış geliş işaretli Visit'e bağlı muayene varsa `Visits.HasExaminations` (işaretleme reddedilir; muayene kaydı silinmez/ayrılmaz).
- Yanıt: `200` + `VisitDto`.
- **Audit:** komut `IAuditableRequest` uygular (`AuditAction = "Visit.Correct"`, hedef `VisitId=...`); istek yükü (gerekçe, hedef durum) audit kaydına yazılır, başarısız denemeler de kaydedilir.

---

## 7) Muayene bağlantısı

- `Examination.VisitId` nullable eklenir; eski kayıtlar `null` kalır ve etkilenmez.
- `POST /examinations` isteğine opsiyonel `visitId`. Verilirse:
  - Visit kiracıya/kliniğe ait olmalı (`Visits.NotFound`); yanlış geliş işaretli veya `Completed` Visit'e muayene açılamaz (`Visits.NotOpen`, 409).
  - `petId` verilmişse Visit'in hayvanıyla aynı olmalı; verilmediyse Visit'ten alınır. `appointmentId` verilmişse Visit'in randevusuyla aynı olmalı; verilmediyse Visit'in randevusu kullanılır. Uyuşmazlık → `Examinations.VisitMismatch`.
  - Hasta tekrar seçilmez: yalnızca `visitId` + klinik içerik yeterlidir.
- `GET /examinations/{id}` ve liste yanıtlarına `visitId` (guid?) eklenir. `PUT /examinations/{id}` `visitId`'yi **değiştirmez** (appointmentId gibi).
- **K2-A korunur:** muayene bir randevuya bağlıysa ve randevu `Scheduled` ise muayene oluşturma randevuyu `Completed` yapmaya devam eder; Visit durumundan bağımsızdır. Randevu ile Visit durumları kısa süre ayrışabilir.
- Randevu başına tek muayene kuralı bu işte **yoktur** (K4).

---

## 8) Visit detayı — `GET /api/v1/visits/{id}`

`Visits.Read`. Yanıt `VisitDto`. Bulunamayan/yabancı kiracı/atanmamış klinik → `Visits.NotFound` (404; varlık sızdırılmaz). Yanlış geliş işaretli kayıt okunabilir (`isVoided: true`).

---

## 9) Bugün — `GET /api/v1/visits/today`

`Visits.Read`. Günlük aksiyon yüzeyidir; geçmişe dönük rapor değildir.

Query:

| Parametre | Tip | Not |
|-----------|-----|-----|
| `clinicId` | guid? | Klinik bağlamı yoksa zorunlu: `Visits.ClinicScopeRequired` |
| `localDate` | date? | İstanbul takvim günü; varsayılan bugün (İstanbul). Gün sınırı `Europe/Istanbul` → UTC `[start,end)` (muayene `examinedOnLocalDate` ile aynı) |

Yanıt `TodayDto`:

```json
{
  "date": "2026-10-10",
  "clinicId": "guid",
  "generatedAtUtc": "...",
  "items": [ /* TodayItemDto */ ],
  "activeHospitalizations": [ /* TodayHospitalizationDto */ ]
}
```

### `TodayItemDto` — tek satır = tek hasta gelişi/randevusu

Randevulu Visit **tek satırdır** (randevu ayrıca satır üretmez). Satır kaynakları:

1. O güne ait, yanlış geliş işaretsiz **Visit**'ler (`arrivedAtUtc` gün aralığında).
2. **Devralınan** Visit'ler: yalnızca `localDate` = bugünse, önceki günlerden kalan `Waiting`/`InProgress` Visit'ler (`isCarriedOver: true`).
3. O gün için **Visit'i olmayan, iptal edilmemiş** randevular (`Scheduled`, ayrıca K2-A nedeniyle muayeneyle `Completed` olmuş ama Visit'i olmayanlar). `visitId = null`.

| Alan | Tip | Not |
|------|-----|-----|
| `visitId` | guid? | Planlı satırda `null` |
| `appointmentId` | guid? | Randevusuz Visit'te `null` |
| `petId`, `petName`, `speciesName` | | |
| `clientId`, `clientName`, `clientPhone?` | | |
| `scheduledAtUtc` | datetime? | Randevu varsa |
| `arrivedAtUtc` | datetime? | Visit varsa |
| `careStatus` | enum? | Visit varsa; planlı satırda `null` |
| `appointmentStatus` | `Scheduled` \| `Completed` \| `Cancelled`? | Randevu varsa |
| `responsibleVeterinarianUserId`, `responsibleVeterinarianName` | | Visit varsa, opsiyonel |
| `isCarriedOver` | bool | |
| `paymentIndicator` | `NoPaymentRecorded` \| `PaymentRecorded` | Aşağıya bakın |
| `hasActiveHospitalization` | bool | Hayvanın açık yatışı var mı |

**Ödeme göstergesi dürüstlüğü:** Mevcut ödeme verisi yalnızca *alınan tahsilatı* tutar; borç/bakiye/fatura yoktur. Bu yüzden gösterge "ödendi/borçlu" **demez**: Visit'in randevusuna veya Visit'e bağlı muayenelerine bağlı ödeme kaydı varsa `PaymentRecorded`, yoksa `NoPaymentRecorded`. Bakiye göstergesi Aşama 3 (temel finans) sonrasıdır. Bu alan `careStatus`'u hiçbir koşulda etkilemez.

**Sıra (sunucu belirler):** `Waiting` (`arrivedAtUtc` artan) → `InProgress` (`arrivedAtUtc` artan) → planlı (`scheduledAtUtc` artan) → `Completed` (`arrivedAtUtc` azalan). İstemci gruplama kuralı: `careStatus` dolu ise o gruba; `null` ise "Planlı".

**Sınır:** Sayfalama yok; klinik/gün başına en fazla 500 satır. Aşılırsa fazlası kesilmez, `Visits.TodayLimitExceeded` (400) döner (gerçekçi olmayan senaryo; sessiz kesme yapılmaz).

### `TodayHospitalizationDto`

Kliniğin tarihten bağımsız **aktif** (`dischargedAtUtc = null`) yatışları: `hospitalizationId`, `petId`, `petName`, `clientName`, `admittedAtUtc`, `plannedDischargeAtUtc?`. Ayrı liste olarak döner; `items` içine karıştırılmaz.

### Kapsam dışı (bu sürüm)

- **Açık iş göstergesi** yok: laboratuvar, tedavi, reçete kayıtlarında açık/kapalı durumu tutulmuyor; uydurulmaz (madde 12, S2).
- Mevcut dashboard ve randevu raporları değişmez.

---

## 10) Hata kodları

| Kod | HTTP | Durum |
|-----|------|-------|
| `Visits.Validation` | 400 | Alan/kural doğrulaması (gerekçe eksik/kısa, hedef durum belirsiz, petId yok vb.) |
| `Visits.ClinicContextMismatch` | 400 | İstek `clinicId` ile aktif klinik bağlamı uyuşmuyor |
| `Visits.ClinicScopeRequired` | 400 | Bugün için klinik belirlenemedi |
| `Visits.AppointmentPetClinicMismatch` | 400 | Randevu ile `petId`/`clinicId` uyuşmuyor |
| `Visits.AppointmentCancelled` | 400 | İptal edilmiş randevuya geliş |
| `Visits.AppointmentNotScheduled` | 400 | Tamamlanmış (Visit'siz) randevuya geliş |
| `Visits.InvalidStatusTransition` | 400 | İzin verilmeyen geçiş |
| `Visits.HasExaminations` | 400 | Muayeneli Visit yanlış geliş işaretlenemez |
| `Visits.TodayLimitExceeded` | 400 | Bugün satır sınırı aşıldı |
| `Visits.NotFound` | 404 | Visit yok / yabancı kiracı / yanlış geliş işaretli (geçiş uçlarında) |
| `Visits.DuplicateActiveVisit` | 409 | Düzeltmeyle aynı hayvanda ikinci aktif Visit oluşacaktı |
| `Visits.NotOpen` | 409 | Tamamlanmış/yanlış geliş Visit'e muayene açılmaya çalışıldı |
| `Visits.ConcurrencyConflict` | 409 | Eşzamanlı güncelleme çözülemedi; istek tekrarlanmalı |
| `Examinations.VisitMismatch` | 400 | Muayene isteği Visit'in hayvanı/randevusuyla uyuşmuyor |
| `Clinics.AccessDenied`, `Clinics.NotFound`, `Pets.NotFound`, `Appointments.NotFound`, `Tenants.ContextMissing`, `Tenants.TenantInactive` | mevcut eşleme | Ortak kodlar |

Yetki yoksa mevcut politika yanıtı (`403`). Doğrulama (FluentValidation) hataları mevcut `Validation.FluentValidation` zarfıyla döner.

---

## 11) Veri ve okuma modeli

- Visit **komut veritabanında** yazılır (`Visits` tablosu; tenant+klinik+hayvan+geliş zamanı, yukarıdaki filtreli benzersiz indeksler). Domain kuralları (`Visit` aggregate'i: geçişler, düzeltme, yanlış geliş) entity içindedir.
- Bugün okuması için kaynak kararı **madde 12, D1**'de.

---

## 12) Açık kararlar (uygulamadan önce netleşmeli)

- **D1 — Bugün'ün okuma kaynağı.** İş tanımı "Query DB read modeli/projeksiyonu" istiyor. Koddan bulgular: (a) mevcut randevu projeksiyonu outbox + işçi + yeniden kurma yoluyla ~2.000 satır; Visit için aynısını kopyalamak büyük bir yinelenme olur. (b) Geliş → kuyrukta görünme anlık olmalı; projeksiyon gecikmesi "tekrar tıklama" ve "iki kullanıcı aynı kuyruğu görür" kabulünü zayıflatır. (c) `CreateExaminationCommandHandler` randevuyu `Completed` yapıyor ama randevu outbox olayı **göndermiyor**; Query DB randevu modeli muayeneden sonra `Scheduled` kalıyor (mevcut hata). **Öneri:** Bugün, Visit'leri komut veritabanından (indeksli, gün/klinik başına küçük küme), randevu ve hayvan/sahip bilgisini komut veritabanından aynı sorguda okur; Query DB'ye Visit projeksiyonu bu işe girmez, gerekirse ayrı karar. Karşı seçenek: Query DB read modeli (tutarlılık gecikmesi + K2-A olay eksiği düzeltmesi gerekir).
- **K-A — Farklı niyetli tekrar geliş.** Hayvanın randevusuz aktif Visit'i varken aynı hayvanın randevusu için geliş denenirse bu taslak mevcut Visit'i döndürür (randevu bağlanmaz). Alternatif: mevcut Visit'e randevu bağlama düzeltmesi. Varsayılan: bağlama yok.
- **S2 — Açık iş göstergesi.** Lab/tedavi/reçete kayıtlarında açık-kapalı durumu yok; bu sürümde alan yok. İstenirse önce ürün tanımı gerekir.
- **S3 — Yanlış geliş modeli.** Bakım durumuna 4. durum eklemek yerine ayrı `isVoided` işareti seçildi (ADR "bakım durumu sade 3 durum" ile uyumlu).
