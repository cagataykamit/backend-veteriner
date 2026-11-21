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
    "vaccineDefinitionId": "…", "vaccineName": "Karma", "notes": null, "timeSpecified": true } ]
```

| Alan | Anlam |
|---|---|
| `kind` | `Planned`: planlı aşı kaydı (`Scheduled`, tarih `dueAtUtc`). `NextDue`: uygulanmış aşının sonraki uygulama tarihi (`Applied` + `dueAtUtc`; E-vet aktarmasının "sonraki aşı tarihi" buradan görünür). `Applied`: o gün uygulanan aşı (tarih `appliedAtUtc`). |
| `state` | Ortak takvim durumu (§6): `Completed` (uygulandı, yeşil), `DueToday` (sarı), `Upcoming` (gelecek, siyah), `Overdue` (kırmızı). Gün sınırı ve gecikme tanımı `VaccinationDueRule` (ADR-028, İstanbul günü): bugünün başından önceki vade gecikmiş, bugün içindeki vade saati geçse de "bugün"; vadesi son 1 takvim ayından eski satır gelmez (aşağıda). Kural `CalendarItemStateRule`. |
| `date` | Satırın göründüğü İstanbul günü. |
| `atUtc` | Vade ya da uygulama anı. Saat yalnız `timeSpecified: true` ise gerçektir (§8.2); değilse istemci satırı "Saatsiz" grubunda gösterir. |

Satır anahtarı `vaccinationId` + `kind`: aynı kayıt aralıkta hem uygulandığı gün hem sonraki tarihi için iki satır verebilir.
Sıralama `atUtc`, hayvan adı, aşı adı.

Kurallar (tek yer `VaccinationCalendarRules`):

- **Kapsam:** yalnız istenen kliniğin aşı kayıtları; `states` (tekrarlı) verilirse yalnız o durumlar (§6); iptal edilen kayıt girmez. İçe aktarılan kayıt (hatırlatması serbest bırakılmamış
  olsa da) görünür; aktarma kuralı değişmedi.
- **Gecikmiş aşı:** yalnız vadesi görüntülenen aralıktaysa gelir; bugüne taşınmaz, ayrı bir "eski gecikmişler" listesi yoktur.
  Gecikmiş aşı listesi için mevcut `GET /vaccinations?onlyOverdue=true`.
- **1 aylık gecikmiş penceresi (2026-11-21):** bugün T (İstanbul günü) ise vadesi `T.AddMonths(-1)` gününden önce olan `Planned`/`NextDue`
  satırı hiç gelmez (ne `Overdue` ne `Upcoming`; ay özeti de saymaz). Takvim ayıdır, sabit gün sayısı değil (31 Mart → 28/29 Şubat).
  `Applied` geçmişi bu kurala girmez. Kural `VaccinationDueRule.OverdueWindow` + `VaccinationCalendarRules.IsShown`; randevu durumu
  (`CalendarItemStateRule.ForAppointment`) değişmez, randevuda alt sınır yoktur.
- **Karşılanmış sonraki tarih:** aynı hayvana aynı aşı (aşı tanımı kimliği; tanımsız eski kayıtta ad, büyük/küçük harf duyarsız) bu
  uygulamadan sonra yeniden uygulanmışsa `NextDue` satırı çıkmaz. Yeniden uygulama kullanıcının okuyabildiği tüm kliniklerden sayılır.
  Planlı kayıt bu kurala girmez; uygulanınca durumu `Applied` olur.
- **Vefat:** bakım dışı hayvanın (`Pet.ReceivesCare` değil) `Planned`/`NextDue` satırı çıkmaz; `Applied` geçmişi görünür.
- **Randevu değildir:** saat dilimini doldurmaz, çakışma (`ClinicOverlapBlocks`) ve "Boş saat bul" (`AppointmentAvailabilityCalculator`)
  hesabına girmez, randevu hatırlatması üretmez. Aşı hatırlatması değişmedi (`ReminderProcessorService`).
- **Hekim:** aşı kaydında hekim tutulmaz; hekim sütunlu görünümde satır ayrı "Aşılar" sütunundadır (§8).

Panelde tıklama: `Planned` → aşı düzenleme (uygulandı yap); `NextDue` → yeni aşı (hasta ve aşı dolu); `Applied` → aşı ayrıntısı;
her vadeli satırda "Randevu ver" → yeni randevu (`?petId&clientId&date&type=Vaccination&notes=`).

### 5.3 Testler

- Application: `VaccinationCalendarRulesTests`, `GetVaccinationsCalendarQueryHandlerTests`, `GetAppointmentsCalendarQueryHandlerTests`
  (neden adı ve not; okuma modeli yolunda tamamlama).
- Entegrasyon: `VaccinationsCalendarEndpointTests` (kaynaklar ve durumlar, içe aktarılan sonraki tarih, karşılanmış tarih, iptal ve
  başka klinik dışarıda, gecikmiş, 1 aydan eski gecikmiş dışarıda, yetki, 45 gün sınırı).

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

## 7) Ay görünümü: gün özeti (2026-11-16)

Kaynak: kullanıcı isteği 2026-11-16 (Ay / Hafta / Gün). Ay hücresi kart değil özet gösterir; planlı aşılarla bir günde onlarca kayıt
olabildiği için istemci kayıtları çekip saymaz. Migration yok, yeni izin yok.

| Uç | İzin | Parametreler |
|---|---|---|
| `GET /api/v1/appointments/calendar/summary` | `Appointments.Read` | `calendar` ile aynı: `dateFromUtc`, `dateToUtc` (en çok 45 gün; 6 haftalık ay ızgarası 42 gün), `clinicId`, `veterinarianUserId` / `withoutVeterinarian`, `appointmentTypes`, `states` |
| `GET /api/v1/vaccinations/calendar/summary` | `Vaccinations.Read` | `calendar` ile aynı: `dateFromUtc`, `dateToUtc`, `clinicId`, `states` |

Yanıt (yalnız kaydı olan günler, tarih sırasıyla):

```json
[ { "date": "2026-11-16", "total": 8, "dueToday": 5, "upcoming": 0, "overdue": 1, "completed": 2 } ]
```

- Sayılar §6.1 durumlarıdır; `total` dört durumun toplamıdır. İptal ve gelmedi randevu sayılmaz (durumu yoktur).
- Kural tek yerde kalsın diye özet işleyicisi aynı takvim sorgusunu gönderir (doğrulama, kapsam, filtre, karşılanmış sonraki tarih,
  vefat kuralı orada) ve gün + durum bazında sayar (`CalendarDaySummary`). Hata kodları `calendar` ile aynı.
- Testler: Application `CalendarSummaryTests`; entegrasyon `AppointmentCalendarFilterEndpointTests` (özet ve filtre).

## 8) Takvim yeniden tasarımı: aşı saati, randevu durumu, günlük kapasite (2026-11-18)

Kaynak: kullanıcı seçimi 2026-11-18 (ürün deposu `ux/takvim-yeniden-tasarim.md`): gün görünümünde hekim sütunlarının yanında saatli
"Aşılar" sütunu, hafta görünümünde gün sütunlarında kartlar, ay görünümünde doluluk haritası. Eklemeli; eski istemci etkilenmez.
**Migration var** (`AddVaccinationDueTimeSpecified`); yeni izin yok.

### 8.1 Aşı vadesinin saati (`Vaccination.DueTimeSpecified`)

- `Vaccination`'a `DueTimeSpecified` (bool, varsayılan `false`) eklendi. `true`: `DueAtUtc`'nin saati gerçektir (takvimde saatiyle
  durur). `false`: yalnız gün bilinir (saat gösterilmez, "Saatsiz" grubu). Mevcut kayıtlar `false` kalır. Vadesiz kayıtta her zaman
  `false`.
- **İçe aktarma:** `SonrakiAsiTarihi` saat eki taşırsa (`yyyy-aa-gg SS:dd`, `gg.aa.yyyy SS:dd`; bölüm 4.4 biçimleri) saat korunur ve
  bayrak `true` olur; saat eki yoksa vade bugünkü gibi günün 12:00'si (İstanbul) ve bayrak `false`. E-vet dönüştürücüsü "Yapılacak"
  aşı görevinin başlangıç saatini artık `SonrakiAsiTarihi`'ne yazar (önce yalnız gün yazılıyordu). E-vet görev saati gerçek saat sayılır
  (kullanıcı kararı 2026-11-18; "00:00 ise saatsiz" kuralı kırılgan olduğu için elendi).
- **Panel (2026-11-20 değişti):** `POST /vaccinations` ve `PUT /vaccinations/{id}` gövdesine isteğe bağlı `dueTimeSpecified` (bool,
  varsayılan `false`) eklendi. Panel formu vadeyi tarih **ve saat** olarak ister; panel, kullanıcının girdiği ya da değiştirdiği vade
  için `true` gönderir. Önce bayrak panelden hep `false` yazılıyordu: formda girilen saat kaydediliyor ama takvimde "Saatsiz"
  görünüyordu (pilot hazırlık listesi kapı 3).
  - Oluşturma: bayrak `dueTimeSpecified && dueAtUtc != null`.
  - Düzenleme: `dueTimeSpecified: true` ise bayrak `true`; verilmezse vade değişmedikçe mevcut bayrak korunur, vade değişirse
    `false` olur (eski istemci). Kural tek yer `Vaccination.UpdateDetails`.
  - Aşı planı uygulama (`VACCINATION_SCHEDULE_API_CONTRACT.md`) gün bazlıdır, bayrak `false` kalır.
- Vade, gecikme, hatırlatma, çakışma ve boş saat kuralları değişmedi: hepsi gün bazlıdır (ADR-028). Saat yalnız sıralama ve gösterim
  içindir.
- Yayındaki eski aktarmalarda saat yoktur. Güncelleme aktarması (ADR-051) mevcut aşıyı mükerrer sayıp atladığı için saati getirmez;
  saat ancak aktarmanın geri alınıp yeniden yapılmasıyla ya da ayrı bir düzeltme işiyle gelir (canlı veriye dokunduğu için
  ayrı onay).

### 8.2 `GET /api/v1/vaccinations/calendar` yanıtına `timeSpecified`

| Alan | Tip | Anlam |
|---|---|---|
| `timeSpecified` | `bool` | `atUtc` saati gerçek mi. `Planned` ve `NextDue` satırında kaydın `DueTimeSpecified` değeri; `Applied` satırında her zaman `false` (uygulanan aşılar takvimde tek satıra toplanır, saati gösterilmez). |

`false` olan vadeli satırı istemci gün ızgarasında "Saatsiz" başlığında gösterir; `true` olan satır "Aşılar" sütununda saatiyle durur.
Aşı yine randevu değildir: saat dilimini doldurmaz (§5.2).

### 8.3 `GET /api/v1/appointments/calendar` yanıtına `state`

| Alan | Tip | Anlam |
|---|---|---|
| `state` | `string?` (`DueToday`, `Upcoming`, `Overdue`, `Completed`; aşı takvimiyle aynı yazım) | §6.1 durumu; iptal ve gelmedi randevuda `null`. Kural `CalendarItemStateRule.ForAppointment` (filtre ve özetle aynı yer). |

Komut veritabanı ve okuma modeli yolları aynı değeri verir.

### 8.4 `GET /api/v1/appointments/calendar/capacity` (`Appointments.Read`)

Ay görünümünde doluluk rengi için günlük kapasite: o günün çalışma saatlerine kaç randevu sığar.

| Parametre | Anlam |
|---|---|
| `dateFromUtc`, `dateToUtc` | Zorunlu, `[from, to)`, en çok 45 gün (`calendar` ile aynı). |
| `clinicId` | Yoksa aktif klinik; hatalar `calendar` ile aynı (`Appointments.ClinicScopeRequired`, `Appointments.ClinicContextMismatch`, `Clinics.AccessDenied`). |
| `veterinarianUserId` | Verilirse tek hekimin kapasitesi (takvim hekim filtresiyle aynı görünüm). |

Yanıt (aralıktaki her İstanbul günü, tarih sırasıyla; kapalı gün `0`):

```json
[ { "date": "2026-11-16", "capacity": 32 } ]
```

- Hesap `AppointmentAvailabilityCalculator.DailyCapacity` (müsaitlik hesabıyla aynı adım ve mola kuralı): o günün çalışma saatleri
  (mola hariç) içinde kliniğin varsayılan randevu süresiyle (`appointment-settings.defaultAppointmentDurationMinutes`) slot aralığına
  hizalı kaç başlangıç varsa × kaynak sayısı. Kaynak: klinikteki hekim sayısı; hekim yoksa ya da `veterinarianUserId` verildiyse 1.
- Doluluk = o günün randevu sayısı (§7 özet `total`) / `capacity`; istemci hesaplar. Kapasite `0` ise gün kapalıdır.
- Hekim bazında çalışma saati ve tarihe özel kapalı gün modeli yok (§4); kapasite haftanın gününe göre aynıdır.
- Klinik çalışma saati kısıtını kapattıysa kapasite her gün 08:00-20:00 penceresinden hesaplanır, kapalı gün yoktur (§9).

### 8.5 Testler

- Domain: `VaccinationDueTimeTests` (oluşturma, içe aktarma, düzenlemede koruma ve sıfırlama).
- Application: `AppointmentAvailabilityCalculatorTests` (günlük kapasite: kapalı gün, mola, hekim sayısı), `GetAppointmentsCalendarQueryHandlerTests`
  (`state`), `GetVaccinationsCalendarQueryHandlerTests` (`timeSpecified`), `ImportValueParsersTests` (saat eki), E-vet dönüştürücü
  testleri (sonraki aşı tarihinde saat), içe aktarma doğrulama testleri (saatli vade).
- Entegrasyon: `AppointmentCalendarFilterEndpointTests` (kapasite ucu).

## 9) Çalışma saati kısıtı (2026-11-20)

Klinik başına tek ayar: `ClinicAppointmentSettings.EnforceWorkingHours` (varsayılan `true`, mevcut davranış). Kural tek yer
`AppointmentWorkingHoursValidation`. Migration: `AddClinicEnforceWorkingHours` (sütun `bit NOT NULL`, mevcut satırlar `true`).

### 9.1 `GET/PUT /api/v1/clinics/{id}/appointment-settings`

- Yanıta `enforceWorkingHours: boolean` eklendi (kayıt yoksa `true`).
- İstekte `enforceWorkingHours` isteğe bağlı: gönderilmezse mevcut değer korunur (eski istemciler; `ownerCancelLinkEnabled` ve
  `waitlistAutoOfferEnabled` ile aynı kural). Yetki değişmedi (`Clinics.Update`).

### 9.2 Kısıt kapalıyken (`enforceWorkingHours = false`)

- Panelden oluşturma (`POST /appointments`), düzenleme (`PUT /appointments/{id}`), yeniden zamanlama ve bekleme listesi teklifi çalışma
  saati, kapalı gün ve mola denetlemez; `Appointments.ClinicClosed`, `Appointments.BreakTimeConflict` ve çalışma saati kaynaklı
  `Appointments.OutsideWorkingHours` dönmez. Slot hizası, çakışma ve geçmiş/gelecek sınırları aynen geçerlidir.
- Randevu yine tek bir yerel gün içinde olmalıdır (aksi halde `Appointments.OutsideWorkingHours`, "tek bir yerel gün"); 23:30'da 60
  dakikalık randevu verilemez.
- Günlük kapasite (§8.4) kapalı gün mantığı kullanmaz: pencere her gün 08:00-20:00, molasız
  (`ClinicWorkingHoursDefaults.UnrestrictedWeek`). Kapasite `0` yalnız kısıt açıkken kapalı gün anlamına gelir.
- "Boş saat bul" (`GET clinics/{id}/online-booking/availability`) değişmedi: tanımlı çalışma saatlerinde arar.
- Online randevu (portal, klinik sitesi, Veteriner bul, en yakın boş saat) bu ayardan **etkilenmez**; tanımlı çalışma saatlerine göre
  sürer.
- İstemci: takvimde kapalı gün taraması ve mola gölgesi gösterilmez; gün/hafta ızgarası 00-24 kaydırılabilir, açılışta 08:00 civarı
  görünür; yeni/düzenle formunda günün tüm saatleri seçilebilir.

### 9.3 Testler

- Application: `CreateAppointmentCommandHandlerTests` (kısıt kapalı: kapalı gün ve molada başarı, gün aşımı yine red),
  `UpdateAppointmentCommandHandlerTests`, `AppointmentLifecycleCommandHandlerTests` (yeniden zamanlama),
  `UpdateClinicAppointmentSettingsCommandHandlerTests` (alan gönderilmezse korunur), `AppointmentAvailabilityWorkingHoursRestrictionTests`
  (kapasite penceresi; online müsaitlik tanımlı haftaya göre).
- Entegrasyon: `AppointmentWorkingHoursRestrictionIntegrationTests` (red → ayarı kapat → aynı randevu 201, kapasite pozitif).
