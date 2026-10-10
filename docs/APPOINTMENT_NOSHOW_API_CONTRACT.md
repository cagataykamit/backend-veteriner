# Randevu "Gelmedi" (NoShow) API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: CHECKIN-010, `CHECKIN-010-NOSHOW-DESIGN.md` (seçenek B). Genel randevu sözleşmesi `BACKEND-CONTRACT-STANDARD.md` içindedir; hata zarfı standart `ProblemDetails` + `extensions.code`.

## 1) Durum modeli

`AppointmentStatus` (JSON numeric int): `Scheduled=0`, `Completed=1`, `Cancelled=2`, **`NoShow=3`** (yeni).

- `NoShow` randevu sonuç durumudur: `Scheduled → NoShow`. `Completed`/`Cancelled` gibi terminaldir; düzenlenemez, yeniden zamanlanamaz, iptal/tamamlanamaz.
- Tek çıkış: gerekçeli **geri alma** (`NoShow → Scheduled`) veya hastanın **gelişi** (madde 4).
- `NoShow` slot tutmaz, hatırlatma almaz, gecikmiş-planlı uyarısında sayılmaz (hepsi yalnızca `Scheduled` bakar).

## 2) Yetki

Yeni izin **`Appointments.NoShow`**; varsayılan rol bağları `Appointments.Cancel` ile aynıdır (Admin, Owner, ClinicAdmin, Sekreter; Veteriner'de yok). Gerekçe: durum geçişleri randevu izinleriyle yönetilir (`Cancel`/`Complete`/`Reschedule`); `Visits.Update` Visit içindir ve randevu durumunu yönetmez, kullanmak izin ayrımını bulandırırdı. **Seed gerekir** (`DbMigrator -- seed`); mevcut özel roller elle atanmalı; oturum yenilenmeli.

## 3) Uçlar

### `POST /api/v1/appointments/{id}/no-show` — `Appointments.NoShow`

Gövde (opsiyonel): `{ "reason": "string? (en çok 500)" }`. `204 NoContent`.

- Yalnızca `Scheduled` **ve** `scheduledAtUtc <=` şimdi. Gelecek randevu → `400 Appointments.NoShowNotYetDue`.
- Randevu için yanlış geliş işaretsiz Visit varsa → `400 Appointments.HasVisit` (hasta gelmiş).
- Zaten `NoShow` ise `204` (idempotent, olay üretilmez). `Completed`/`Cancelled` → `400 Appointments.InvalidStatusTransition`.
- Diğer: `404 Appointments.NotFound` (yabancı kiracı/klinik bağlamı), `403 Clinics.AccessDenied` (atanmamış klinik), `409 Appointments.ConcurrencyConflict`.
- Audit: `Appointment.NoShow` (gerekçe yüke yazılır).

### `POST /api/v1/appointments/{id}/no-show/revert` — `Appointments.NoShow`

Gövde: `{ "reason": "string (zorunlu, 5-500)" }`. `204 NoContent`. `NoShow → Scheduled`.

- `NoShow` değilse `400 Appointments.InvalidStatusTransition`; gerekçe eksik/kısa `400 Appointments.Validation`.
- Geri alma zaman/çakışma kontrolü yapmaz (geçmiş saatli randevu; slot kuralı yalnızca yeni/yeniden zamanlanan randevuda işler).
- Audit: `Appointment.NoShowRevert`.

## 4) Geç gelen hasta

`POST /visits` ile `NoShow` randevu için geliş açılırsa **geliş engellenmez**: randevu aynı işlemde `Scheduled`'a döner ve normal akış sürer (muayene randevuyu `Completed` yapar). Ayrı bir geri alma çağrısı gerekmez; iz Visit kaydıdır.

## 5) Durum geçişi bypass'ı kapatıldı

`PUT /api/v1/appointments/{id}` (`Appointments.Reschedule`) ve `POST /api/v1/appointments` (`Appointments.Create`) artık `Status` ile geçiş yaptıramaz:

- `PUT`: `status` mevcut durumla aynı olmalıdır (`Scheduled` kayıtta `Scheduled`); farklı değer → `400 Appointments.InvalidStatusTransition`. Tamamlama/iptal/gelmedi yalnızca kendi uçlarıyla.
- `POST`: `status` verilmezse veya `Scheduled` ise kabul; diğer değerler `400 Appointments.Validation`.

## 6) Tüketiciler

- **Liste/detay/takvim:** `status` değeri `3` dönebilir; `GET /appointments?status=3` filtreler. İstemci `NoShow` için etiket ("Gelmedi") ve görünüm tanımlamalıdır.
- **Randevu raporu** (`/reports/appointments`, CSV, XLSX): yanıt ve dosyada `noShow` sayısı/"Gelmedi" etiketi; `total = scheduled + completed + cancelled + noShow`.
- **Dashboard özeti:** `noShowTodayCount` (bugünün `NoShow` randevuları). Gecikmiş-planlı uyarısı `NoShow`'u saymaz.
- **Bugün** (`VISITS_API_CONTRACT.md`): `NoShow` randevu Visit'siz planlı satırdır, `appointmentStatus: "NoShow"`; sırada **en sonda** ("Gelmedi" grubu) yer alır ki aynı gün geri alınabilsin.
- **Muayene:** `NoShow` randevuya doğrudan muayene açılamaz (`Cancelled` gibi reddedilir); hasta geldiyse önce geliş açılır (madde 4).
- **Olay/projeksiyon:** Durum değişimleri `appointment.updated.v1` olayıyla yayılır (tam anlık görüntü, `status` içinde); Query DB günlük istatistiğine `NoShowCount` eklenir (Query DB şema migration'ı gerekir; okuma bayrakları kapalıdır).

## 7) Hata kodları (yeni)

| Kod | HTTP | Durum |
|-----|------|-------|
| `Appointments.NoShowNotYetDue` | 400 | Henüz saati gelmemiş randevu |
| `Appointments.HasVisit` | 400 | Randevuya bağlı geliş kaydı var |
