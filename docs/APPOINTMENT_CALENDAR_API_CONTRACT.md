# Saat ızgaralı randevu takvimi — API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: kullanıcı dostu denetim B1 (2026-11-15). Kullanıcının seçtiği kurgu: gün görünümünde hekim
sütunları, yanında "Boş saat bul". Genel randevu sözleşmesi `BACKEND-CONTRACT-STANDARD.md`. Migration yok (Query DB şeması da değişmedi).

## 1) Takvim yanıtında hekim

`GET /api/v1/appointments/calendar` (`Appointments.Read`): parametreler değişmedi. `AppointmentCalendarItemDto`'ya iki alan eklendi:

| Alan | Tip | Anlam |
|---|---|---|
| `veterinarianUserId` | `guid?` | Randevunun hekimi; hekimsiz randevuda `null` (takvimde "Hekim seçilmedi" sütunu). |
| `veterinarianName` | `string?` | Hekimin görünen adı (`IClinicVeterinarianReader.GetNamesAsync`); bulunamazsa `null`. |

- Komut veritabanı yolu hekimi doğrudan okur. Okuma modeli yolu (`QueryReadModels:AppointmentsEnabled`, bugün her ortamda kapalı) hekim
  taşımadığı için kimlikler aynı istekte komut veritabanından tamamlanır; iki yol aynı yanıtı verir.

## 2) Çakışma kuralı (değişti; kural tek yer `AppointmentSlotRules.ClinicOverlapBlocks`)

Klinik ayarı `allowOverlappingAppointments = false` iken:

- **Hekimi atanmış randevu:** çakışma hekim bazındadır. Aynı saatte farklı hekimlerin randevusu çakışma değildir. Aynı hekim →
  `409 Appointments.VeterinarianTimeConflict`.
- **Hekimsiz randevu:** bugünkü kural aynen: klinikte üst üste binen herhangi bir planlı randevu → `409 Appointments.ClinicTimeConflict`.
- Hasta çakışması her durumda: `409 Appointments.PetTimeConflict`.

`allowOverlappingAppointments = true` iken klinik çakışması hiç denetlenmez (değişmedi).

Kuralı oluşturma, düzenleme (PUT), yeniden zamanlama, bekleme listesi teklif kabulü ve iptali geri alma ortak
`AppointmentSlotRules.EnsureNoConflictAsync` ile; müsaitlik hesabı (`AppointmentAvailabilityCalculator`, online randevu ve "Boş saat bul")
aynı `ClinicOverlapBlocks` ile uygular. Sıra: klinik → hasta → hekim.

## 3) Hekim listesi

`GET /api/v1/clinics/{clinicId}/veterinarians`: yetki artık any-of `Visits.Create`, `Appointments.Read`, `Appointments.Create`,
`Appointments.Reschedule`, `Clinics.Read`, `Clinics.Update` (politika `Clinics_VeterinariansRead`; önce yalnız `Visits.Create`). Yanıt
değişmedi: `[{ userId, name? }]`. Takvim boş hekimin de sütununu bu listeden çizer.

## 4) İstemcinin kullandığı mevcut uçlar (değişmedi)

- Çalışma saatleri: `GET /api/v1/clinics/{id}/working-hours` (taralı kapalı saat ve mola). Hekim bazında çalışma saati ve tarihe özel
  kapalı gün modeli yok; ilk sürümde klinik saatleri yeter.
- Slot aralığı: `GET /api/v1/clinics/{id}/appointment-settings` (`slotIntervalMinutes`; tıklanan hücre bu aralığa hizalı).
- "Boş saat bul": `GET /api/v1/clinics/{clinicId}/online-booking/availability?reasonId&from&days` (personel, en çok 14 gün;
  `reasonId` boşsa 30 dk, tüm hekimler). Yanıttaki `freeVeterinarianUserIds` ilk boş hekimi önerir.
- Boş hücreden ya da boş saatten yeni randevu: panel `/panel/appointments/new?date=yyyy-MM-dd&time=HH:mm&vet={userId}`.
