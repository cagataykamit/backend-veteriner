# Online randevu talebi ve bildirim merkezi — API sözleşmesi

Kaynak karar: ürün deposu `decisions/ADR-042-online-randevu-talebi.md` (K1–K7). Backlog: APPT-001…016, UX-008.
Tüm saatler UTC ISO-8601 taşınır; gösterim ve müsaitlik hesabı İstanbul duvar saatidir. Enum'lar JSON'da sayıdır
(sorgu/route parametrelerinde ad ya da sayı kabul edilir). Hata gövdesi mevcut ProblemDetails (`code` alanı).

## 1. Model özeti

| Varlık | Not |
|---|---|
| `AppointmentReason` | Klinik başına neden: ad, varsayılan süre (5–240 dk), online'da görünür, sıra, aktif, uygun hekimler (boş = tüm hekimler). |
| `Appointment` (+alan) | `reasonId?`, `veterinarianUserId?` eklendi. Hekim bazlı çakışma: `Appointments.VeterinarianTimeConflict` (409). |
| `ClinicOnlineBookingSettings` | Açık/kapalı (**varsayılan kapalı**), `minLeadMinutes` (0–10080, varsayılan 120), `maxAdvanceDays` (1–180, varsayılan 30), 12 karakterlik bağlantı kodu. |
| `AppointmentRequest` | Durum: `0 Pending`, `1 Approved`, `2 Rejected`, `3 Cancelled`, `4 Expired`. Kaynak: `0 Portal`, `1 Link`. İletişim anlığı + durum geçmişi. |
| `ClinicAppointmentRequestEmailTemplate` | Tür: `0 Received`, `1 Approved`, `2 Rescheduled`, `3 Rejected`. Yoksa Vetinity varsayılanı. |
| `UserNotification` | Tür: `0 AppointmentRequestCreated`, `1 AppointmentRequestCancelled`, `2 AppointmentAssigned`. |

İzin: **`AppointmentRequests.Manage`** (kuyruk, onay, red). Varsayılan roller: Admin, Owner, ClinicAdmin, Sekreter.
Neden ve online ayar yazımı mevcut `Clinics.Update` ile.

## 2. Randevu nedenleri — `/api/v1/clinics/{clinicId}/appointment-reasons`

| Uç | Yetki | Açıklama |
|---|---|---|
| `GET` | `Clinics.Read` \| `Clinics.Update` \| `Appointments.Create` \| `Appointments.Reschedule` | Aktif/pasif dahil, sıra sonra ad. |
| `POST` | `Clinics.Update` | 201 + `AppointmentReasonDto`. Aktif ad çakışması 409 `AppointmentReasons.DuplicateName`; hekim olmayan kullanıcı 400 `AppointmentReasons.Validation`. |
| `PUT /{reasonId}` | `Clinics.Update` | Gövde `id` verilirse route ile aynı olmalı (400 `AppointmentReasons.RouteIdMismatch`). |
| `POST /{reasonId}/deactivate` \| `/activate` | `Clinics.Update` | İdempotent; etkinleştirmede ad çakışması 409. |

```json
// AppointmentReasonDto / gövde
{ "id": "…", "clinicId": "…", "name": "Aşı", "defaultDurationMinutes": 15, "isOnlineBookable": true,
  "sortOrder": 0, "isActive": true, "veterinarianUserIds": ["…"] }
```

Randevu oluştur/güncelle (`POST/PUT /api/v1/appointments`) gövdesine `reasonId?` ve `veterinarianUserId?` eklendi.
`durationMinutes` boşsa öncelik: nedenin süresi → klinik varsayılanı. `GET /api/v1/appointments/{id}` yanıtı bu iki alanı döner.
Pasif neden yeni randevuda 400; mevcut randevuda korunur. Hekim kliniğin aktif hekimi (Veteriner claim) olmalı.

## 3. Online ayarlar ve müsaitlik — `/api/v1/clinics/{clinicId}/online-booking`

| Uç | Yetki | Açıklama |
|---|---|---|
| `GET` | `Clinics.Read` | Kayıt yoksa `isEnabled=false`, `bookingCode=null`. |
| `PUT` | `Clinics.Update` | `{ isEnabled, minLeadMinutes, maxAdvanceDays }`; ilk kayıtta kod üretilir. Geçersiz aralık 400 `OnlineBooking.Validation`. |
| `POST /regenerate-code` | `Clinics.Update` | Yeni kod; eski bağlantı hemen 404. |
| `GET /availability?reasonId&from&days` | §2 GET ile aynı | Personel görünümü: `[{ date, slots: [{ startUtc, endUtc, freeVeterinarianUserIds }] }]`. Online sınırlar uygulanmaz; geçmiş saat dönmez. `days` 1–14 (varsayılan 7). |

```json
// ClinicOnlineBookingSettingsDto
{ "clinicId": "…", "isEnabled": true, "minLeadMinutes": 120, "maxAdvanceDays": 30,
  "bookingCode": "k7m2p9xq4r8t", "bookingUrl": "https://portal…/randevu/k7m2p9xq4r8t", "bookingCodeGeneratedAtUtc": "…" }
```

**Müsaitlik kuralı (tek yer: `AppointmentAvailabilityCalculator`):** çalışma saatleri (mola hariç) ∩ nedenin uygun
hekimlerinden en az biri boş − çakışan planlı randevular. Adım = nedenin süresi, klinik slot aralığının katına yukarı
yuvarlanır ve açılıştan başlar (randevu oluşturma kuralı slot hizası ister). Klinik "üst üste binme" kapalıysa klinikteki
her randevu saati doldurur. Hekimin başka klinikteki randevusu da onu meşgul eder; hekimsiz randevu bir kapasite tüketir.
Klinikte hekim yoksa klinik tek kaynaktır. **Bekleyen talepler saati bloke etmez.**

## 4. Personel talep kuyruğu — `/api/v1/appointment-requests` (`AppointmentRequests.Manage`)

| Uç | Açıklama |
|---|---|
| `GET ?clinicId&status&fromUtc&toUtc&page&pageSize` | `PagedResult<AppointmentRequestListItemDto>`; `pageSize` ≤ 500. Aktif klinik bağlamı varsa o klinik; yoksa erişilen klinikler. Bekleyenler en yakın saat önce, diğerleri en yeni önce. Takvim katmanı: `status=Pending&fromUtc&toUtc`. Atanmamış klinik `clinicId` ile 403. |
| `GET /{id}` | `AppointmentRequestDetailDto`; kapsam dışı 404 `AppointmentRequests.NotFound`. |
| `GET /{id}/email-preview?kind&scheduledAtUtc&rejectionReason` | `{ kind, subject, body }` — gönderilecek metin (düzenlenebilir). |
| `POST /{id}/approve` | Gövde aşağıda; yanıt güncel detay. |
| `POST /{id}/reject` | `{ reason (3–500), email? }`; yanıt güncel detay. |

```json
// AppointmentRequestListItemDto
{ "id": "…", "clinicId": "…", "status": 0, "source": 1, "reasonId": "…", "reasonName": "Aşı",
  "requestedStartUtc": "…", "durationMinutes": 30, "contactName": "Ayşe Yılmaz", "contactEmail": "ayse@…",
  "contactPhone": "905551234567", "clientId": null, "clientName": null, "petId": null, "petName": "Pamuk",
  "createdAtUtc": "…", "appointmentId": null }

// AppointmentRequestDetailDto
{ "summary": { …ListItem… }, "petSpecies": "Kedi", "petBreed": null, "ownerNote": "…",
  "approvedStartUtc": null, "approvedVeterinarianUserId": null, "wasRescheduled": false, "rejectionReason": null,
  "closedAtUtc": null,
  "history": [{ "fromStatus": null, "toStatus": 0, "actorKind": 0, "actorUserId": null, "actorName": null, "note": null, "atUtc": "…" }],
  "clientMatches": [{ "clientId": "…", "fullName": "…", "phone": "…", "email": "…", "pets": [{ "id": "…", "name": "…" }] }],
  "requestedSlotFree": true, "freeVeterinarianUserIds": ["…"] }

// approve gövdesi (hepsi isteğe bağlı)
{ "clientId": "…", "petId": "…", "scheduledAtUtc": "…", "veterinarianUserId": "…", "durationMinutes": 30,
  "email": { "subject": "…", "body": "…" } }
```

Onay kuralları: randevu mevcut `CreateAppointmentCommand` ile oluşur (çalışma saati, slot hizası, klinik/hayvan/hekim
çakışması) ve talep aynı transaction'da `Approved` olur; biri başarısızsa hiçbiri kalmaz. `scheduledAtUtc` istenen saatten
farklıysa `wasRescheduled=true` ve `Rescheduled` e-postası gider. Hekim verilmezse o saatte boş uygun hekim atanır; uygun
hekim tanımlı ama hepsi doluysa 409 `AppointmentRequests.SlotUnavailable`. Müşteri/hayvan: portal talebinde bağın müşterisi
(başka müşteriye onaylanamaz); bağlantı talebinde personel mevcut müşteriyi seçer ya da önce hızlı kayıtla oluşturur
(`POST /api/v1/clients/quick-register`, CLIENTS_QUICK_REGISTER_API_CONTRACT.md). Eksikse 400 `AppointmentRequests.Validation`.
Beklemede olmayan talep 409 `AppointmentRequests.NotPending`; eşzamanlı karar 409 `AppointmentRequests.ConcurrencyConflict`.
Her geçiş geçmişe ve denetim kaydına (`AppointmentRequest.Approve/Reject/...`) yazılır.

## 5. Kliniğe özel bağlantı (kimliksiz) — `/api/portal/v1/booking/{bookingCode}`

IP başına hız sınırı `portal-auth` (10 istek/dk). Kod bilinmiyor, online randevu kapalı, klinik/kiracı pasif ya da abonelik
salt okunur ise sebep ayırt edilmeden 404 `OnlineBooking.NotFound`.

| Uç | Açıklama |
|---|---|
| `GET` | `OnlineBookingPageDto`: `{ clinicName, clinicCity, clinicPhone, clinicAddress, minLeadMinutes, maxAdvanceDays, maxPendingRequests, reasons: [{ id, name, durationMinutes }] }` (yalnız aktif + online nedenler). |
| `GET /availability?reasonId&from&days` | `[{ date, slots: [{ startUtc, endUtc }] }]`; online sınırlar uygulanır, hekim bilgisi dönmez. |
| `POST /verification-code` | `{ email }` → 204. 6 haneli kod, 10 dk, 5 deneme, 60 sn yeniden gönderim beklemesi (portal kod kuralları). |
| `POST /requests` | Gövde aşağıda → 201 `{ id, status, requestedStartUtc }`. |

```json
{ "email": "…", "verificationCode": "123456", "contactName": "…", "phone": "05551234567",
  "petName": "Pamuk", "petSpecies": "Kedi", "petBreed": "Tekir", "reasonId": "…", "requestedStartUtc": "…", "note": "…" }
```

Kurallar: saat müsait listede olmalı (409 `AppointmentRequests.SlotUnavailable`); aynı e-posta + klinik için en fazla 3
bekleyen talep (409 `AppointmentRequests.PendingLimitReached`); kod hatalı/süresi dolmuş 400 `OnlineBooking.InvalidCode`
(kod en son tüketilir; saat/form hatasında aynı kodla tekrar denenebilir). Telefon Türkiye cep telefonu olmalı. Müşteri,
hasta ya da portal hesabı **oluşmaz** (ADR-031 K3). Kayıtta sahibe "talep alındı" e-postası ve kuyruk yetkisi olanlara
bildirim gider.

## 6. Portal (oturumlu) — `/api/portal/v1/links/{linkId}` (`PortalOwner`, hız sınırı `portal`)

| Uç | Açıklama |
|---|---|
| `GET /booking` | §5 sayfa DTO'su (bağın kliniği; kapalıysa 404 `OnlineBooking.NotFound`). |
| `GET /booking/availability?reasonId&from&days` | §5 ile aynı. |
| `GET /appointment-requests` | Bağın müşterisinin bu klinikteki portal talepleri (son 50): `[{ id, status, reasonName, requestedStartUtc, durationMinutes, petId, petName, approvedStartUtc, wasRescheduled, rejectionReason, createdAtUtc, canCancel }]`. |
| `POST /appointment-requests` | `{ petId? , petName?, petSpecies?, petBreed?, reasonId, requestedStartUtc, phone?, note? }` → 201. `petId` bağın müşterisinin hayvanı olmalı (değilse 404); yoksa `petName` zorunlu (klinik kaydı onayda oluşturur). Telefon boşsa müşteri kaydındaki. İletişim adı/e-posta müşteri kaydından ve hesaptan. |
| `POST /appointment-requests/{id}/cancel` | Yalnız bekleyen; aksi 409 `AppointmentRequests.NotPending`. Kuyruk yetkisi olanlara bildirim. |

`status` (liste ve 201 yanıtı; §5 bağlantı ucunun 201 yanıtı dahil) portal sözleşmesine uygun olarak **metindir**: `Pending` · `Approved` · `Rejected` · `Cancelled` · `Expired` (P1 Dalga 4b düzeltmesi; önce sayı dönüyordu, `PORTAL_API_CONTRACT.md` §13.4).

## 7. Talep e-posta şablonları — `/api/v1/clinics/{clinicId}/appointment-request-email-templates`

| Uç | Yetki | Açıklama |
|---|---|---|
| `GET` | `Clinics.Read` | `{ templates: [{ kind, subject, body, isDefault }], placeholders: [...] }` (dört tür). |
| `PUT /{kind}` | `Clinics.Update` | `{ subject (≤200), body (≤4000) }`. |
| `DELETE /{kind}` | `Clinics.Update` | Varsayılana döner. |
| `POST /{kind}/preview` | `Clinics.Read` | `{ subject, body }` → örnek değerlerle doldurulmuş `{ kind, subject, body }`. |

Yer tutucular: `{{ownerName}} {{clinicName}} {{clinicPhone}} {{date}} {{time}} {{reason}} {{petName}} {{rejectionReason}}`.
Metin düz yazıdır. Varsayılan metinler "talep ≠ kesin randevu" ayrımını söyler; sağlık bilgisi içermez.

## 8. Bildirim merkezi — `/api/v1/notifications` (oturumdaki kullanıcı)

| Uç | Açıklama |
|---|---|
| `GET ?type&unreadOnly&page&pageSize` | `PagedResult<{ id, type, title, body, targetType, targetId, clinicId, createdAtUtc, readAtUtc }>` yeniden eskiye; `pageSize` ≤ 100. |
| `GET /unread-count` | `{ count }` (rozet). |
| `POST /{id}/read` | 204; başkasının bildirimi 404 `Notifications.NotFound`. |
| `POST /read-all?type` | 204 (en çok 500 kayıt bir çağrıda). |

`targetType`: `AppointmentRequest` (kuyruktaki talep) ya da `Appointment`. Alıcılar: yeni talep/iptal → kliniğe erişen
(atanmış ya da kiracı geneli rol) ve `AppointmentRequests.Manage` izni rolünden gelen kullanıcılar; randevu hekime atanınca
→ hekim (işlemi yapan hekimin kendisiyse bildirim yok). Bildirim merkezi açık işler listesinin yerini almaz.

Ek (P1 Dalga 4b, PORTAL-009): tür `PortalDataRequestCreated` (3), `targetType` `PortalDataRequest` — portaldan KVKK veri
talebi gelince kliniğe erişen `OwnerPortal.Manage` izinli kullanıcılara (`PORTAL_API_CONTRACT.md` §11).

## 9. Arka plan

`AppointmentRequestExpiryHostedService` (ayar `AppointmentRequestExpiry`: `Enabled`, `InitialDelaySeconds=120`,
`IntervalMinutes=15`, `BatchSize=200`): istenen saati geçmiş bekleyen talepleri `Expired` yapar (geçmişte `System`).

## 10. Migration

`AddOnlineAppointmentRequestsAndNotifications` — `Appointments.ReasonId/VeterinarianUserId`, `AppointmentReasons`,
`AppointmentReasonVeterinarians`, `ClinicOnlineBookingSettings`, `AppointmentRequests`, `AppointmentRequestStatusChanges`,
`AppointmentRequestEmailVerifications`, `ClinicAppointmentRequestEmailTemplates`, `UserNotifications`. Yerel/paylaşılan
veritabanına uygulanmadı.
