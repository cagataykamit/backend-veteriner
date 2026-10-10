# Visit (geliş) ve Bugün API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-009 (vetinity-product), backlog CHECKIN-006/007. Tüm endpoint'ler `api/v1` altında, JSON gövde/query, standart `ProblemDetails` + `extensions.code` hata zarfı (bkz. `EXAMINATIONS_WORKSPACE_API_CONTRACT.md`).

> **Durum:** Sözleşme. Kararlar madde 12'de kayıtlı (D1 komut veritabanı okuması, K-A bağlama yok, S2 açık iş göstergesi yok, S3 `isVoided`, S4 sorumlu hekim seçimi ve görünen ad (madde 3.1)). Uygulama bu sözleşmeye göre yapılır.

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
| `responsibleVeterinarianUserId` | guid? | Opsiyonel; verilirse o kliniğin aktif hekimi olmalı (madde 3.1) |
| `responsibleVeterinarianName` | string? | Hekimin görünen adı; hekim yoksa `null` |
| `arrivedAtUtc` | datetime (UTC, `Z`) | Sunucu saatiyle atanır; istemci göndermez |
| `careStatus` | `Waiting` \| `InProgress` \| `Completed` | |
| `startedAtUtc` | datetime? | `InProgress`'e ilk geçiş |
| `completedAtUtc` | datetime? | `Completed`'a geçiş; düzeltmeyle geri alınırsa `null` |
| `isUrgent` | bool | Acil işareti (madde 4.4); varsayılan `false`. Triage skoru yoktur, yalnızca bayrak |
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
| Düzeltme (durum düzeltme, yanlış geliş, yanlış gelişi geri alma) | `Visits.Correct` | Admin, Owner, ClinicAdmin |

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
  "responsibleVeterinarianUserId": "guid?",
  "isUrgent": false
}
```

Kurallar:

- **Acil (opsiyonel):** `isUrgent` verilmezse `false`. Yalnızca **yeni** kayıtta uygulanır; idempotent tekrar mevcut kaydı döndürür ve `isUrgent` değerini değiştirmez (sonradan işaretleme madde 4.4).
- **Randevulu:** `appointmentId` dolu. `clinicId`/`petId` randevudan türetilir; gönderilirse randevu ile aynı olmalı, değilse `Visits.AppointmentPetClinicMismatch`. Randevu `Scheduled` olmalı: `Cancelled` → `Visits.AppointmentCancelled`; `Completed` ve o randevu için Visit yoksa → `Visits.AppointmentNotScheduled`.
- **Randevusuz:** `appointmentId` yok; `petId` ve aktif klinik (`clinicId` veya bağlam) zorunlu, aksi halde `Visits.Validation`. Randevu **oluşturulmaz**.
- Hayvan ve klinik kiracıya ait olmalı (`Pets.NotFound`, `Clinics.NotFound`, `Appointments.NotFound`).

### 3.1) Sorumlu hekim: seçim ve doğrulama

- **Opsiyonel** (randevulu ve randevusuz gelişte). Randevu hekim alanı taşımadığı için randevudan varsayılan türetilmez; seçilmezse `null` kalır.
- **Hekim tanımı:** kiracıdaki **aktif** kliniğe atanmış (`UserClinic`) ve `Veteriner` operasyon claim'ine sahip kullanıcı. Aynı kural hekim listesi, geliş doğrulaması ve görünen ad çözümünde kullanılır (tek yerde: `IClinicVeterinarianReader`).
- **Seçim listesi:** `GET /api/v1/clinics/{clinicId}/veterinarians` (`Visits.Create`; atanmamış klinik → `403 Clinics.AccessDenied`). Yanıt: `[{ "userId": "guid", "name": "string?" }]`, ada göre sıralı.
- **Doğrulama:** seçilen kullanıcı o klinikte aktif hekim değilse (rol yok, başka klinik, bilinmeyen kimlik) → `400 Visits.Validation`; geliş oluşmaz.
- **Görünen ad:** kullanıcının kayıtlı adı (`PUT /api/v1/me/display-name`, gövde `{ "displayName": "string?" }`, en çok 120 karakter, boş değer adı temizler, `204`); yoksa e-postadan türetilen ad (örn. `ali.veli@…` → `ali.veli`). Aynı kural `GET /me/account-summary` `displayName` alanında da geçerlidir.

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

### 4.4 Acil işareti — `PUT /api/v1/visits/{id}/urgency` (`Visits.Update`)

Gelişte (`POST /visits` `isUrgent`) veya sonradan işaretlenir/kaldırılır. Triage skoru yoktur; yalnızca bayrak. İstek:

```json
{ "isUrgent": true }
```

- **Yetki:** `Visits.Update` (geliş açan `Visits.Create` ile aynı roller zaten bu izne sahiptir; yeni izin yok).
- **Idempotent:** Değer zaten aynıysa `200` ve mevcut `VisitDto` (değişiklik yok). Eşzamanlı çift istekte kaybeden de başarı alır.
- Yalnızca açık (`Waiting`/`InProgress`) kayıt için değiştirilebilir; `Completed` kayıtta değer değişecekse `Visits.NotOpen` (409). Yanlış geliş işaretli kayıt → `Visits.NotFound`.
- `isUrgent` bakım durumunu değiştirmez; yalnızca Bugün sırasını etkiler (madde 9).
- Yanıt: `200` + `VisitDto`.

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

### 6.1 Yanlış gelişi geri alma — `POST /api/v1/visits/{id}/restore`

`Visits.Correct`. Yanlışlıkla `isVoided` işaretlenen geliş kaydını kuyruğa geri getirir. **Gerekçe zorunlu.** İstek:

```json
{ "reason": "string (zorunlu, 5-500 karakter, trim sonrası)" }
```

Neden ayrı uç: `corrections` isteği "hedef durum veya yanlış geliş işareti, tam olarak biri" kuralıyla çalışır; üçüncü bir seçenek bu kuralı bulandırır. Geri alma kendi gövdesi, kendi audit eylemi ve kendi çakışma kuralı olan ayrı bir işlemdir.

Kurallar:

- Yalnızca `isVoided = true` kayıt geri alınır; zaten geçerli kayıt için `Visits.Validation` (400). Başka kiracı veya klinik bağlamı → `Visits.NotFound` (404; varlık sızdırılmaz); kullanıcıya atanmamış klinik → `Clinics.AccessDenied` (403, düzeltme ucuyla aynı).
- Geri alma `isVoided`/`voidReason` değerini temizler; **bakım durumu ve zaman damgaları olduğu gibi kalır** (kayıt, işaretlenmeden önceki durumuna döner). İşaretleme gerekçesi audit kaydında (`Visit.Correct`) korunur.
- **Çakışma (409):** Madde 5'teki filtreli benzersiz indeks kurallarıyla aynı koşullar geri alma anında kontrol edilir:
  - Kayıt `Completed` değilse ve hayvanın başka aktif Visit'i varsa → `Visits.DuplicateActiveVisit`.
  - Kaydın `appointmentId` değeri doluysa ve aynı randevu için başka (yanlış geliş olmayan) Visit varsa → `Visits.DuplicateAppointmentVisit`.
  - Kontrol ile kayıt arasında eşzamanlı bir geliş açılırsa filtreli benzersiz indeks işlemi reddeder; sonuç yine bu iki koddan biridir (bozulan kural yeniden okumayla belirlenir). İki kural hem uygulamada hem veritabanında korunur.
- Yanıt: `200` + `VisitDto` (`isVoided: false`).
- **Audit:** `AuditAction = "Visit.Restore"`, hedef `VisitId=...`; gerekçe audit yüküne yazılır, başarısız denemeler de kaydedilir.
- `GET /visits/{id}` yanlış geliş işaretli kaydı zaten döndürür (madde 8); istemci geri alma düğmesi için `isVoided` alanına bakar.

---

## 7) Muayene bağlantısı

- `Examination.VisitId` nullable eklenir; eski kayıtlar `null` kalır ve etkilenmez.
- `POST /examinations` isteğine opsiyonel `visitId`. Verilirse:
  - Visit kiracıya/kliniğe ait olmalı (`Visits.NotFound`); yanlış geliş işaretli veya `Completed` Visit'e muayene açılamaz (`Visits.NotOpen`, 409).
  - `petId` verilmişse Visit'in hayvanıyla aynı olmalı; verilmediyse Visit'ten alınır. `appointmentId` verilmişse Visit'in randevusuyla aynı olmalı; verilmediyse Visit'in randevusu kullanılır. Uyuşmazlık → `Examinations.VisitMismatch`.
  - Hasta tekrar seçilmez: yalnızca `visitId` + klinik içerik yeterlidir.
- `GET /examinations/{id}` ve liste yanıtlarına `visitId` (guid?) eklenir. `PUT /examinations/{id}` `visitId`'yi **değiştirmez** (appointmentId gibi).
- **K2-A korunur:** muayene bir randevuya bağlıysa ve randevu `Scheduled` ise muayene oluşturma randevuyu `Completed` yapmaya devam eder; Visit durumundan bağımsızdır. Randevu ile Visit durumları kısa süre ayrışabilir.
- Randevu başına tek muayene kuralı bu işte **yoktur** (K4). **Mevcut davranış (belgelenmiş, entegrasyon testiyle doğrulandı):** aynı Visit’e ikinci `POST /examinations` **engellenmez**; `201` döner, Visit `InProgress` kalır ve `GET /examinations?clinicId=…&visitId=…` her iki muayeneyi listeler. İstemci, Visit’in mevcut muayenesini bu filtreyle bulup yeni muayene yerine onu açmalıdır. Sunucu tarafı “Visit başına tek muayene” kuralı ürün kararı gerektirir (K4) ve eklenmemiştir.

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
| `voided` | bool? | Varsayılan `false`. `true` ise **yalnızca** o günün yanlış geliş işaretli Visit'leri döner (madde 9.1) |

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
| `responsibleVeterinarianUserId`, `responsibleVeterinarianName` | guid?, string? | Visit varsa, opsiyonel (madde 3.1) |
| `isCarriedOver` | bool | |
| `paymentIndicator` | `NoPaymentRecorded` \| `PaymentRecorded` | Aşağıya bakın |
| `hasActiveHospitalization` | bool | Hayvanın açık yatışı var mı |
| `isUrgent` | bool | Visit'in acil işareti; planlı (Visit'siz) satırda `false` |
| `isVoided` | bool | Yalnızca `voided=true` yanıtında `true` olabilir; normal Bugün'de her zaman `false` |
| `voidReason` | string? | `isVoided` ise dolu |

**Ödeme göstergesi dürüstlüğü:** Mevcut ödeme verisi yalnızca *alınan tahsilatı* tutar; borç/bakiye/fatura yoktur. Bu yüzden gösterge "ödendi/borçlu" **demez**: Visit'in randevusuna veya Visit'e bağlı muayenelerine bağlı ödeme kaydı varsa `PaymentRecorded`, yoksa `NoPaymentRecorded`. Bakiye göstergesi Aşama 3 (temel finans) sonrasıdır. Bu alan `careStatus`'u hiçbir koşulda etkilemez.

**Sıra (sunucu belirler):** `Waiting` (önce acil, sonra `arrivedAtUtc` artan) → `InProgress` (önce acil, sonra `arrivedAtUtc` artan) → planlı (`scheduledAtUtc` artan) → `Completed` (`arrivedAtUtc` azalan). İstemci gruplama kuralı: `careStatus` dolu ise o gruba; `null` ise "Planlı".

**Sınır:** Sayfalama yok; klinik/gün başına en fazla 500 satır. Aşılırsa fazlası kesilmez, `Visits.TodayLimitExceeded` (400) döner (gerçekçi olmayan senaryo; sessiz kesme yapılmaz).

### 9.1) Yanlış işaretlenenler — `GET /visits/today?voided=true`

Resepsiyonun yanlış işaretlediği kaydı bulup `POST /visits/{id}/restore` ile geri alması için görünüm.

- **Tasarım gerekçesi:** Aynı uç, aynı `TodayItemDto` satırı ve aynı sorgu işleyicisi yeniden kullanılır (istemci aynı satır bileşenini çizer); yeni uç/DTO eklenmez. `includeVoided` gibi bir karışım bayrağı **seçilmedi**: yanlış geliş kayıtlarını normal kuyruğa karıştırmak Bugün'ün aksiyon yüzeyi niteliğini ve sıra/gruplama kurallarını bozar. `voided=true` ayrı bir görünümdür.
- **Kapsam:** Yalnızca `arrivedAtUtc` seçilen gün aralığında olan, `isVoided = true` Visit'ler. Devralınan Visit yok, planlı (Visit'siz) randevu yok, `activeHospitalizations` her zaman boş liste.
- **Sıra:** `arrivedAtUtc` azalan (en son gelen üstte). Gruplama yoktur.
- **Yetki ve kapsam:** `Visits.Read`; klinik/kiracı kuralları normal Bugün ile aynıdır. Geri alma düğmesi `Visits.Correct` gerektirir (sunucu zaten 403 verir).
- `careStatus` işaretlenmeden önceki bakım durumunu gösterir. 500 satır sınırı aynen geçerlidir.

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
| `Visits.DuplicateActiveVisit` | 409 | Düzeltme veya geri almayla aynı hayvanda ikinci aktif Visit oluşacaktı |
| `Visits.DuplicateAppointmentVisit` | 409 | Geri almayla aynı randevuda ikinci Visit oluşacaktı |
| `Visits.NotOpen` | 409 | Tamamlanmış/yanlış geliş Visit'e muayene açılmaya çalışıldı |
| `Visits.ConcurrencyConflict` | 409 | Eşzamanlı güncelleme çözülemedi; istek tekrarlanmalı |
| `Examinations.VisitMismatch` | 400 | Muayene isteği Visit'in hayvanı/randevusuyla uyuşmuyor |
| `Clinics.AccessDenied`, `Clinics.NotFound`, `Pets.NotFound`, `Appointments.NotFound`, `Tenants.ContextMissing`, `Tenants.TenantInactive` | mevcut eşleme | Ortak kodlar |

Yetki yoksa mevcut politika yanıtı (`403`). Doğrulama (FluentValidation) hataları mevcut `Validation.FluentValidation` zarfıyla döner.

---

## 11) Veri ve okuma modeli

- **Yazma ve okuma tek kaynaktan:** Visit **komut veritabanında** (`Visits` tablosu; filtreli benzersiz indeksler madde 5). Domain kuralları `Visit` aggregate'i içindedir. Bugün sorgusu da komut veritabanından okunur: `Visits`, `Appointments`, `Pets`, `Clients`, `Species`, `Payments`, `Examinations`, `Hospitalizations`.
- **Neden Query DB read modeli yok:** Mevcut Query DB hattı her ortamda bayrakla kapalıdır (`QueryReadModels:*` hepsi `false`; `PetProjection` ve `PaymentProjection` `Enabled: false`; Staging'de `QueryConnection` boş). Bugün'ü bu hatta bağlamak dört projeksiyonun (randevu, hayvan, sahip, ödeme) açılıp backfill edilmesini zorunlu kılar ve varsayılan yapılandırmada Bugün'ü çalışmaz bırakır. Mevcut okuma yüzeylerinin deseni gibi komut veritabanı varsayılan yoldur; Query DB yolu ayrı bir iş olarak bayrakla eklenir (okuyucu arayüzü bunu mümkün kılar).
- **Outbox olayı yok:** Visit bu sürümde projeksiyon tüketicisi olmadığı için outbox'a olay yazmaz (tüketicisiz olaylar `OutboxMessages` tablosunda işlenmeden birikir).
- **Tutarlılık:** Bugün yazma ile aynı veritabanından okuduğu için geliş kaydı anında görünür.

---

## 12) Kararlar

- **D1 — Bugün'ün okuma kaynağı: komut veritabanı.** Gerekçe madde 11. Query DB read modeli/projeksiyon (ADR-009 uygulama etkisi) bu işin kapsamından çıkarıldı; Query DB okuması açıldığında ayrı iş olarak eklenir. Not: muayene randevuyu `Completed` yaptığında randevu outbox olayının üretilmemesi mevcut bir hatadır (randevu Query DB read modeli `Scheduled` kalır); Bugün bundan etkilenmez, ayrıca ele alınmalıdır.
- **K-A — Farklı niyetli tekrar geliş:** Hayvanın randevusuz aktif Visit'i varken aynı hayvanın randevusu için geliş denenirse mevcut Visit döner; randevu bağlanmaz.
- **S2 — Açık iş göstergesi:** Lab/tedavi/reçete kayıtlarında açık-kapalı durumu yok; bu sürümde alan yok, uydurulmaz. Önce ürün tanımı gerekir.
- **S3 — Yanlış geliş modeli:** Bakım durumuna 4. durum eklenmez; ayrı `isVoided` işareti (ADR "bakım durumu sade 3 durum" ile uyumlu).
- **S4 — Sorumlu hekim:** Opsiyonel; kiracıdaki aktif kliniğe atanmış `Veteriner` rolündeki kullanıcı. Görünen ad için `User.DisplayName` eklendi (boşsa e-posta türevi). Ayrıntı madde 3.1.

---

## 13) Deploy notu

- **İzinler (seed adımı gerekir):** Yeni `Visits.Read`, `Visits.Create`, `Visits.Update`, `Visits.Correct` izinleri **API açılışında gelmez**: API ne migration ne seed çalıştırır. İzinler yalnızca `dotnet run --project src/Backend.Veteriner.DbMigrator -- seed` (veya `all`) ile `Permissions` tablosuna yazılır ve `RolePermissionBindingSeeder` ile varsayılan rollere (Admin, Owner, ClinicAdmin, Veteriner, Sekreter; madde 2) bağlanır. Seed idempotenttir (mevcut kayıtları çoğaltmaz). Mevcut **özel roller** bu izinleri otomatik almaz; yöneticiler elle atamalıdır.
- **Oturum:** İzinler JWT'ye giriş (login), token yenileme (refresh) ve klinik seçimi (select-clinic) anında DB'den okunarak `permission` claim'i olarak eklenir. Seed sonrası mevcut oturumlar yeni izni **görmez**; kullanıcı çıkış-giriş yapmalıdır (frontend `Visits.Read` claim'ini bu yüzden bulamaz).
- **Migration:** `AddVisits` (`Visits` tablosu, filtreli benzersiz indeksler, `Examinations.VisitId`) `AddUserDisplayName` (`Users.DisplayName`, nullable) ve `AddVisitUrgency` (`Visits.IsUrgent`, `bit NOT NULL`, mevcut kayıtlar `false`) komut veritabanına uygulanmalıdır. Query DB şeması değişmez.
- **Yapılandırma:** Yeni bayrak veya ayar yoktur.

---

## 14) Bilinen eksikler

- **Açık iş göstergesi yok:** Laboratuvar, tedavi ve reçete kayıtlarında açık/kapalı durumu tutulmadığı için Bugün'de gösterge yoktur (S2).
- **Randevu outbox olayı yok:** Muayene randevuyu `Completed` yaptığında `appointment.completed.v1` olayı üretilmez; Query DB randevu read modeli `Scheduled` kalır. Bugün komut veritabanından okuduğu için etkilenmez; Query DB okuması açılmadan önce düzeltilmelidir.
- **Query DB read modeli yok:** Visit için projeksiyon ve Query DB okuması bu sürümde yoktur (D1); bayrakla ayrı iş olarak eklenecektir.
- **Hekim adı ve seçimi:** Randevu hekim alanı taşımaz, bu yüzden randevudan varsayılan hekim türetilmez. Kullanıcıların gerçek adı yalnızca `PUT /me/display-name` ile girilir; girilmeyenlerde e-posta türevi ad görünür. Admin/davet akışında ad girişi yoktur. `GET /tenants/{id}/members` hâlâ e-posta türevi adı döner (bu işte değiştirilmedi).
- **Ödeme göstergesi:** Yalnızca tahsilat kaydının varlığını söyler; borç/bakiye temel finans (Aşama 3) sonrasıdır.
- **Visit başına çoklu muayene:** Aynı Visit’e (ve aynı randevuya) birden fazla muayene açılabilir; tek muayene kuralı ürün kararıdır (K4) ve eklenmemiştir. Çift kayıt riskini istemci `GET /examinations?visitId=` ile mevcut muayeneyi açarak azaltır.
