# Oda ve cihaz (randevu kaynakları) — API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: ADR-053 (ürün deposu, **Önerildi**), APPT-025 1. dilim. Genel randevu sözleşmesi
`BACKEND-CONTRACT-STANDARD.md`, takvim `APPOINTMENT_CALENDAR_API_CONTRACT.md`.

**Migration:** `AddAppointmentResources` (yeni tablo `AppointmentResources`; `Appointments`'a `RoomResourceId`, `EquipmentResourceId`
— ikisi de boş olabilir, mevcut satırlar boş kalır). Yeni izin yok.

## 1) Kaynak tanımları

Klinik bazlıdır (K6). Silinmez, pasife alınır. Aktif kaynaklar arasında ad klinik içinde benzersizdir (büyük/küçük harf duyarsız).

| Uç | İzin | Açıklama |
|---|---|---|
| `GET /api/v1/clinics/{clinicId}/appointment-resources` | `Clinics_SchedulingRead` (any-of `Clinics.Read`, `Clinics.Update`, `Appointments.Create`, `Appointments.Reschedule`) | Aktif ve pasif hepsi; sıra: önce odalar, sonra cihazlar; tür içinde `sortOrder`, sonra ad (takvim sütun sırası). |
| `POST /api/v1/clinics/{clinicId}/appointment-resources` | `Clinics.Update` | Yeni kaynak, 201 + kaynak. |
| `PUT /api/v1/clinics/{clinicId}/appointment-resources/{resourceId}` | `Clinics.Update` | Ad, kapasite, sıra. Gövdede `id` verilirse adresle aynı olmalı (`AppointmentResources.RouteIdMismatch`). |
| `POST …/{resourceId}/deactivate` · `POST …/{resourceId}/activate` | `Clinics.Update` | İdempotent. Etkinleştirmede ad aktif bir kaynakla çakışırsa 409. |

Erişim randevu nedenleriyle aynı (`ClinicDefinitionAccess`): klinik kiracıda olmalı, atanmış klinik kapsamındaki kullanıcı o kliniğe
atanmış olmalı (403 `Clinics.AccessDenied`).

Gövde (POST/PUT):

```json
{ "name": "Ameliyathane", "type": "Room", "capacity": 1, "sortOrder": 0 }
```

Yanıt (`AppointmentResourceDto`):

```json
{ "id": "…", "clinicId": "…", "name": "Ameliyathane", "type": "Room", "capacity": 1, "sortOrder": 0, "isActive": true }
```

| Alan | Kural |
|---|---|
| `name` | Zorunlu, en çok 100 karakter, kırpılır. |
| `type` | `Room` (oda) ya da `Equipment` (cihaz); sayı da kabul edilir (1, 2). Yanıtta metin. **Sonradan değiştirilemez** (400 `AppointmentResources.Validation`): randevuda oda ve cihaz ayrı alanlardadır. |
| `capacity` | 1–20 (varsayılan 1). Aynı anda en çok bu kadar planlı randevu kaynağı kullanabilir (K2). Düşürmek mevcut randevuları değiştirmez; takvim aşımı çakışma olarak gösterir. |
| `sortOrder` | Tür içinde sıra. |

Hatalar: 400 `Validation.FluentValidation` / `AppointmentResources.Validation`, 404 `AppointmentResources.NotFound` (başka klinik ya da
kiracı dahil), 409 `AppointmentResources.DuplicateName`.

## 2) Randevuda oda ve cihaz

`POST /api/v1/appointments` ve `PUT /api/v1/appointments/{id}` gövdesine isteğe bağlı iki alan (K3):

| Alan | Tip | Anlam |
|---|---|---|
| `roomResourceId` | `guid?` | Randevunun odası; `type = Room` bir kaynak olmalı. |
| `equipmentResourceId` | `guid?` | Randevunun cihazı; `type = Equipment` bir kaynak olmalı. |

- PUT tam yazımdır (hekim ve nedenle aynı): alan gönderilmezse kaynak kaldırılır. Panelin düzenleme formu mevcut değeri gönderir.
- Kaynak bu kliniğin olmalı (değilse 404 `AppointmentResources.NotFound`), alanın türünde olmalı (400 `Appointments.Validation`), aktif
  olmalı (400 `Appointments.Validation`). Mevcut randevuda zaten bağlı olan kaynak pasifleşse de düzenlemede korunabilir.
- `Guid.Empty` 400 (`Validation.FluentValidation`).
- `GET /api/v1/appointments/{id}` yanıtına `roomResourceId`, `equipmentResourceId` eklendi (adlar kaynak listesinden).
- Yeniden zamanlama (`/reschedule`) ve iptali geri alma randevunun kaynaklarını korur ve aynı çakışma kuralına girer.
- Online talep onayı, bekleme listesi teklifi ve içe aktarma kaynak atamaz (K7).

## 3) Çakışma kuralı (K4)

Kaynak bazında, **engeldir**: aralıktaki eşzamanlı planlı randevu sayısı + 1 kapasiteyi aşarsa `409 Appointments.ResourceTimeConflict`
(ileti kaynağın adını içerir). Eşzamanlılık tepe değeridir: kapasite 2 iken 10:00–10:30 ve 10:30–11:00 randevuları 10:00–11:00'e üçüncü
bir randevuyu engellemez. Biten ve başlayan randevu çakışmaz. Yalnız `Scheduled` randevular sayılır.

- Kural tek yer: `AppointmentResource.HasRoomFor` (hesap) ve `AppointmentAssignment.EnsureResourcesAsync` (yükleme ve hata).
- Sıra `AppointmentSlotRules.EnsureNoConflictAsync`: klinik → hasta → hekim → **oda ve cihaz**; çalışma saati en son.
- Klinik ayarı `allowOverlappingAppointments` bu kuralı **etkilemez** (hekim kuralı gibi).
- Kontrol transaction içinde kiracı takvim kilidiyle yapılır (`LockScheduleAsync`); eşzamanlı iki istek aynı kaynağı alamaz.

## 4) Takvim yanıtı

`GET /api/v1/appointments/calendar`: `AppointmentCalendarItemDto`'ya eklemeli dört alan:

| Alan | Tip | Anlam |
|---|---|---|
| `roomResourceId` | `guid?` | Oda; yoksa `null`. |
| `roomResourceName` | `string?` | Odanın adı (pasif oda da adıyla). |
| `equipmentResourceId` | `guid?` | Cihaz; yoksa `null`. |
| `equipmentResourceName` | `string?` | Cihazın adı. |

Okuma modeli yolu (`QueryReadModels:AppointmentsEnabled`) bunları taşımaz; hekim gibi komut veritabanından tamamlanır
(`AppointmentCalendarDetailsByIdsSpec`). Takvim filtrelerine kaynak filtresi eklenmedi (panel kaynak sütunlarını bu alanlardan çizer).

## 5) Kapsam dışı (sonraki dilimler)

- "Boş saat bul" ve online müsaitlik (`AppointmentAvailabilityCalculator`) kaynağa bakmaz (K9).
- Randevu nedenine varsayılan kaynak (APPT-007), online talepte kaynak (K7).
- Randevuda birden çok cihaz.
- Günlük kapasite ucu (`calendar/capacity`) hekim sayısıyla hesaplar; kaynak görünümü için ayrı kapasite yok.

## 6) Tüm veri dışa aktarma

`randevu_kaynaklari.csv` (Id, KlinikId, Ad, Tur, Kapasite, Etkin) ve `randevular.csv`'ye `OdaId`, `CihazId` (bkz. `DATA_EXPORT_API_CONTRACT.md`).

## 7) Testler

- Domain: `AppointmentResourceTests` (ad, tür, kapasite sınırı, tür değişmez, tepe eşzamanlılık, kenar teması, randevu alanları).
- Application: `AppointmentResourceAssignmentTests` (bulunamadı, yanlış tür, pasif ve korunan, kapasite 1 ve 2),
  `GetAppointmentsCalendarQueryHandlerTests` (okuma modeli yolunda kaynak adları).
- Entegrasyon: `AppointmentResourceIntegrationTests` (tanım uçları, sıra, ad çakışması, tür değişmez, yetki; farklı hekimde aynı oda 409,
  üst üste binme açıkken de 409, oda alanında cihaz 400, yeniden zamanlamada 409; kapasite 2; pasif kaynak yeni randevuda 400, mevcutta
  korunur; takvimde adlar).
