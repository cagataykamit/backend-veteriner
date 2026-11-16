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

## 5) Aşı katmanı ve ayrıntı balonu (2026-11-16)

Kaynak: kullanıcı kararı 2026-11-16 (E-vet takvimine bakarak): aşılar takvimde randevu olarak değil, ayrı bir katman olarak görünür;
kartın üzerine gelince ayrıntı balonu. Migration yok, yeni izin yok.

### 5.1 Randevu takvimi yanıtına eklenenler

`AppointmentCalendarItemDto`'ya üç alan eklendi (eklemeli; eski istemci etkilenmez):

| Alan | Tip | Anlam |
|---|---|---|
| `reasonId` | `guid?` | Randevu nedeni; nedensiz randevuda `null`. |
| `reasonName` | `string?` | Nedenin adı (pasif neden de adıyla); bulunamazsa `null`. Balonda tür yerine bu yazılır. |
| `notes` | `string?` | Randevu notu (balonda). |

Okuma modeli yolu (`QueryReadModels:AppointmentsEnabled`) bunları taşımadığı için hekimle birlikte aynı istekte komut veritabanından
tamamlanır (`AppointmentCalendarDetailsByIdsSpec`); iki yol aynı yanıtı verir.

### 5.2 `GET /api/v1/vaccinations/calendar` (`Vaccinations.Read`)

Parametreler randevu takvimiyle aynı: `dateFromUtc`, `dateToUtc` (zorunlu, `[from, to)`, en çok 45 gün), `clinicId` (yoksa aktif klinik;
ikisi de yoksa 400 `Vaccinations.ClinicScopeRequired`; bağlamla çelişirse 400 `Vaccinations.ClinicContextMismatch`; atanmamış klinik
403 `Clinics.AccessDenied`). Hatalı aralık 400 (`Validation.FluentValidation`). Yanıt dizi:

```json
[ { "vaccinationId": "…", "kind": "NextDue", "state": "DueToday", "date": "2026-11-16", "atUtc": "2026-11-16T09:00:00Z",
    "clinicId": "…", "petId": "…", "petName": "Pamuk", "clientId": "…", "clientName": "Ayşe Yılmaz",
    "vaccineDefinitionId": "…", "vaccineName": "Karma", "notes": null } ]
```

| Alan | Anlam |
|---|---|
| `kind` | `Planned`: planlı aşı kaydı (`Scheduled`, tarih `dueAtUtc`). `NextDue`: uygulanmış aşının sonraki uygulama tarihi (`Applied` + `dueAtUtc`; E-vet aktarmasının "sonraki aşı tarihi" buradan görünür). `Applied`: o gün uygulanan aşı (tarih `appliedAtUtc`). |
| `state` | Ortak takvim durumu (§6): `Completed` (uygulandı, yeşil), `DueToday` (sarı), `Upcoming` (gelecek, siyah), `Overdue` (kırmızı). Gün sınırı ve gecikme tanımı `VaccinationDueRule` (ADR-028, İstanbul günü): bugünün başından önceki vade gecikmiş, bugün içindeki vade saati geçse de "bugün". Kural `CalendarItemStateRule`. |
| `date` | Satırın göründüğü İstanbul günü. |
| `atUtc` | Vade ya da uygulama anı. Vadeli satırda saat anlamlı değildir; istemci satırı günün üst şeridinde (tüm gün) gösterir. |

Satır anahtarı `vaccinationId` + `kind`: aynı kayıt aralıkta hem uygulandığı gün hem sonraki tarihi için iki satır verebilir.
Sıralama `atUtc`, hayvan adı, aşı adı.

Kurallar (tek yer `VaccinationCalendarRules`):

- **Kapsam:** yalnız istenen kliniğin aşı kayıtları; `states` (tekrarlı) verilirse yalnız o durumlar (§6); iptal edilen kayıt girmez. İçe aktarılan kayıt (hatırlatması serbest bırakılmamış
  olsa da) görünür; aktarma kuralı değişmedi.
- **Gecikmiş aşı:** yalnız vadesi görüntülenen aralıktaysa gelir; bugüne taşınmaz, ayrı bir "eski gecikmişler" listesi yoktur.
  Gecikmiş aşı listesi için mevcut `GET /vaccinations?onlyOverdue=true`.
- **Karşılanmış sonraki tarih:** aynı hayvana aynı aşı (aşı tanımı kimliği; tanımsız eski kayıtta ad, büyük/küçük harf duyarsız) bu
  uygulamadan sonra yeniden uygulanmışsa `NextDue` satırı çıkmaz. Yeniden uygulama kullanıcının okuyabildiği tüm kliniklerden sayılır.
  Planlı kayıt bu kurala girmez; uygulanınca durumu `Applied` olur.
- **Vefat:** bakım dışı hayvanın (`Pet.ReceivesCare` değil) `Planned`/`NextDue` satırı çıkmaz; `Applied` geçmişi görünür.
- **Randevu değildir:** saat dilimini doldurmaz, çakışma (`ClinicOverlapBlocks`) ve "Boş saat bul" (`AppointmentAvailabilityCalculator`)
  hesabına girmez, randevu hatırlatması üretmez. Aşı hatırlatması değişmedi (`ReminderProcessorService`).
- **Hekim:** aşı kaydında hekim tutulmaz; hekim sütunlu görünümde satır günün üst şeridindedir.

Panelde tıklama: `Planned` → aşı düzenleme (uygulandı yap); `NextDue` → yeni aşı (hasta ve aşı dolu); `Applied` → aşı ayrıntısı;
her vadeli satırda "Randevu ver" → yeni randevu (`?petId&clientId&date&type=Vaccination&notes=`).

### 5.3 Testler

- Application: `VaccinationCalendarRulesTests`, `GetVaccinationsCalendarQueryHandlerTests`, `GetAppointmentsCalendarQueryHandlerTests`
  (neden adı ve not; okuma modeli yolunda tamamlama).
- Entegrasyon: `VaccinationsCalendarEndpointTests` (kaynaklar ve durumlar, içe aktarılan sonraki tarih, karşılanmış tarih, iptal ve
  başka klinik dışarıda, gecikmiş, yetki, 45 gün sınırı).

## 6) Takvim filtreleri (2026-11-16)

Kaynak: kullanıcı isteği 2026-11-16 ("takvimde filtreleme yok, rakipte var"). Filtreler sunucuda uygulanır; panel seçimleri adres
çubuğunda tutar. Migration yok, yeni izin yok. Eski istemci etkilenmez (tüm parametreler isteğe bağlı).

### 6.1 Ortak durum dili (`CalendarItemState`)

Renk açıklaması ve "Durum" filtresi randevu ve aşıda aynıdır. Kural tek yer `CalendarItemStateRule` (İstanbul günü, ADR-028):

| Değer | Randevu | Aşı |
|---|---|---|
| `DueToday` | Planlı, bugün | Vadesi bugün (planlı ya da sonraki) |
| `Upcoming` | Planlı, yarın ya da sonra | Vadesi yarın ya da sonra |
| `Overdue` | Planlı, günü geçmiş (gelmedi işaretlenmemiş) | Vadesi geçmiş, yapılmamış |
| `Completed` | Tamamlandı | O gün uygulandı (`kind: Applied`) |

İptal ve gelmedi randevu hiçbir duruma girmez: durum filtresi seçiliyken görünmez, seçili değilken görünür (bugünkü gibi soluk).

### 6.2 `GET /api/v1/appointments/calendar` ek parametreleri

| Parametre | Anlam |
|---|---|
| `veterinarianUserId` | Yalnız bu hekimin randevuları. |
| `withoutVeterinarian=true` | Yalnız hekimsiz randevular. `veterinarianUserId` ile birlikte 400 (`Validation.FluentValidation`). |
| `appointmentTypes` (tekrarlı) | Yalnız bu türler (`Vaccination`, `Checkup`… ya da sayı). |
| `states` (tekrarlı) | Yalnız bu durumlar (§6.1). |

Filtreler birlikte (VE) uygulanır. Aralık en çok 45 gün olduğundan filtre sunucuda bellekte uygulanır (`AppointmentCalendarFilter`);
komut veritabanı ve okuma modeli yolları aynı kuralı kullanır. Mevcut `status` parametresi (hazır listeler) değişmedi.

### 6.3 `GET /api/v1/vaccinations/calendar` ek parametresi

`states` (tekrarlı): yalnız bu durumlar. Aşı kaydında hekim ve tür yoktur; panel hekim filtresi bir hekime ayarlıyken ya da tür
filtresinde "Planlı aşılar" seçili değilken aşı katmanını istemez (aşı "hekim atanmamış" sayılır).

### 6.4 Şube

Takvim aktif klinik bağlamıyla çalışır; bağlamla çelişen `clinicId` 400 `*.ClinicContextMismatch` (değişmedi). Panelde "Şube" seçimi
yalnız birden çok kliniğe erişen kullanıcıda görünür ve aktif kliniği değiştirir (`POST /auth/select-clinic`, üst çubuktaki klinik
değiştiriciyle aynı yol); takvim aynı görünüm ve filtrelerle yeni klinikte açılır. Birden çok kliniği aynı takvimde gösterme yok.

### 6.5 Testler

- Application: `AppointmentCalendarFilterTests` (hekim, hekimsiz, tür, gün bazlı durum, iptal/gelmedi, VE), `GetAppointmentsCalendarQueryHandlerTests`
  (komut ve okuma modeli yolunda filtre), `GetVaccinationsCalendarQueryHandlerTests` (durum filtresi).
- Entegrasyon: `AppointmentCalendarFilterEndpointTests` (tekrarlı parametreler HTTP üzerinden, birlikte hekim + hekimsiz 400, aşı `states`).
