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
| `AppointmentRequest` | Durum: `0 Pending`, `1 Approved`, `2 Rejected`, `3 Cancelled`, `4 Expired`. Giriş kapısı (`source`): `0 Portal`, `1 Link`. Kaynak (`channel`, §14): `0 ClinicLink`, `1 ClinicSite`, `2 OwnerPortal`, `3 Phone`, `4 VetFinder`, eski talepte `null`. İletişim anlığı + durum geçmişi. |
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
| `PUT` | `Clinics.Update` | `{ isEnabled, minLeadMinutes, maxAdvanceDays, showNextAvailableSlots? }`; ilk kayıtta kod üretilir. Geçersiz aralık 400 `OnlineBooking.Validation`. `showNextAvailableSlots` yoksa mevcut değer korunur (§12). |
| `POST /regenerate-code` | `Clinics.Update` | Yeni kod; eski bağlantı hemen 404. |
| `GET /availability?reasonId&from&days` | §2 GET ile aynı | Personel görünümü: `[{ date, slots: [{ startUtc, endUtc, freeVeterinarianUserIds }] }]`. Online sınırlar uygulanmaz; geçmiş saat dönmez. `days` 1–14 (varsayılan 7). `reasonId` isteğe bağlı: boşsa "Diğer" (§11). |

```json
// ClinicOnlineBookingSettingsDto
{ "clinicId": "…", "isEnabled": true, "minLeadMinutes": 120, "maxAdvanceDays": 30, "showNextAvailableSlots": true,
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
| `GET /channel-summary?from&to&clinicId` | Kaynağa göre özet (§14). |
| `GET /{id}` | `AppointmentRequestDetailDto`; kapsam dışı 404 `AppointmentRequests.NotFound`. |
| `GET /{id}/email-preview?kind&scheduledAtUtc&rejectionReason` | `{ kind, subject, body }` — gönderilecek metin (düzenlenebilir). |
| `POST /{id}/approve` | Gövde aşağıda; yanıt güncel detay. |
| `POST /{id}/reject` | `{ reason (3–500), email? }`; yanıt güncel detay. |

```json
// AppointmentRequestListItemDto
// reasonId null ise "Diğer / emin değilim" talebi (§11); channel null ise kaynak bilinmiyor (§14)
{ "id": "…", "clinicId": "…", "status": 0, "source": 1, "channel": 1, "reasonId": "…", "reasonName": "Aşı",
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

Takvime ekle (TIER-020): `Approved` ve `Rescheduled` e-postasının sonuna (şablon ya da düzenlenen metinden sonra) Google
Takvim ve .ics bağlantısı eklenir; `email-preview` bu satırları göstermez. Takvim bağlantısı onayla aynı transaction'da
yazılır ve yalnız takvim dosyası verir. Ayrıntı `APPOINTMENT_OWNER_LINK_API_CONTRACT.md` §8.

## 5. Kliniğe özel bağlantı (kimliksiz) — `/api/portal/v1/booking/{bookingCode}`

IP başına hız sınırı `portal-auth` (10 istek/dk). Kod bilinmiyor, online randevu kapalı, klinik/kiracı pasif ya da abonelik
salt okunur ise sebep ayırt edilmeden 404 `OnlineBooking.NotFound`.

| Uç | Açıklama |
|---|---|
| `GET` | `OnlineBookingPageDto`: `{ clinicName, clinicCity, clinicPhone, clinicAddress, minLeadMinutes, maxAdvanceDays, maxPendingRequests, reasons: [{ id, name, durationMinutes, requiresNote }] }` (aktif + online nedenler; sonda her zaman "Diğer", §11). |
| `GET /availability?reasonId&from&days` | `[{ date, slots: [{ startUtc, endUtc }] }]`; online sınırlar uygulanır, hekim bilgisi dönmez. `reasonId` isteğe bağlı: boşsa "Diğer" (§11). |
| `POST /verification-code` | `{ email }` → 204. 6 haneli kod, 10 dk, 5 deneme, 60 sn yeniden gönderim beklemesi (portal kod kuralları). |
| `POST /requests` | Gövde aşağıda → 201 `{ id, status, requestedStartUtc }`. |

```json
{ "email": "…", "verificationCode": "123456", "contactName": "…", "phone": "05551234567",
  "petName": "Pamuk", "petSpecies": "Kedi", "petBreed": "Tekir", "reasonId": "…", "requestedStartUtc": "…", "note": "…",
  "channel": "site" }
// reasonId null/yoksa "Diğer": note zorunlu (§11). channel isteğe bağlı kaynak değeri (§14)
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
| `POST /appointment-requests` | `{ petId? , petName?, petSpecies?, petBreed?, reasonId?, requestedStartUtc, phone?, note? }` → 201. `reasonId` boşsa "Diğer" ve `note` zorunlu (§11). `petId` bağın müşterisinin hayvanı olmalı (değilse 404); yoksa `petName` zorunlu (klinik kaydı onayda oluşturur). Telefon boşsa müşteri kaydındaki. İletişim adı/e-posta müşteri kaydından ve hesaptan. |
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

`AppointmentRequestReasonOptional` (§11) — `AppointmentRequests.ReasonId` nullable (FK `AppointmentReasons`'a nullable kalır).
Paylaşılan veritabanına uygulanmadı.

`AddOnlineBookingShowNextAvailableSlots` (§12) — `ClinicOnlineBookingSettings.ShowNextAvailableSlots` (`bit`, mevcut satırlar
`1`). Yalnız yerel geliştirme veritabanına uygulandı; paylaşılan ya da üretim veritabanına uygulanmadı.

`AddAppointmentRequestChannel` (§14) — `AppointmentRequests.Channel` (`int` nullable; mevcut satırlar `NULL` = bilinmiyor, geriye
dönük doldurma yok) ve özet için `IX_AppointmentRequests_TenantId_ClinicId_CreatedAtUtc`. Yalnız yerel geliştirme veritabanına
uygulandı; paylaşılan ya da üretim veritabanına uygulanmadı.

## 11. Ek (2026-11-03): "Diğer / emin değilim" (ADR-042 ek md. 1-2)

Sistem her klinikte sağlar; klinik tanımlamaz, kapatamaz. Ad ve süre tek yerde: `AppointmentRequest.OtherReasonName`,
`AppointmentRequest.OtherReasonDurationMinutes` (30).

| Yer | Sözleşme |
|---|---|
| Sayfa (§5, §6 `GET /booking`) | `reasons[]` öğesi `{ id: Guid \| null, name, durationMinutes, requiresNote }`. Kliniğin nedenleri `requiresNote=false`; listenin **sonunda her zaman** `{ id: null, name: "Diğer / emin değilim", durationMinutes: 30, requiresNote: true }` — klinik hiç neden tanımlamamış olsa da. |
| Müsaitlik (§5, §6 ve §3 personel) | `reasonId` isteğe bağlı. Yoksa süre 30 dk, klinikteki tüm hekimler uygun. Doluysa önceki davranış (online uçlarda aktif + online görünür neden, personel ucunda kliniğin nedeni; yoksa 404 `AppointmentReasons.NotFound`). `00000000-…` kimliği 400 doğrulama hatası. |
| Talep (§5 `POST /requests`, §6 `POST /appointment-requests`) | `reasonId` isteğe bağlı (yok ya da `null`). Yoksa `note` zorunlu: kırpılmış en az 3 karakter (üst sınır 500); değilse 400 `AppointmentRequests.NoteRequired`, mesaj "Ne için geldiğinizi kısaca yazın." Kural domain'dedir; saat/bekleyen talep sınırı denetiminden sonra çalışır (bağlantıda doğrulama kodu tüketilmez). |
| Personel (§4) | `AppointmentRequestListItemDto.reasonId` `null` olabilir; `reasonName` "Diğer / emin değilim", `durationMinutes` 30. Detayda `requestedSlotFree`/`freeVeterinarianUserIds` 30 dk ve tüm hekimlerle hesaplanır. Onayda randevu nedensiz (`reasonId=null`) ve talebin süresiyle (ya da gövdedeki `durationMinutes`) oluşur; hekim verilmezse o saatte boş herhangi bir hekim atanır. |
| Portal taleplerim (§6) | Değişmedi; `reasonName` "Diğer / emin değilim". |

## 12. Ek (2026-11-08): herkese açık en yakın müsait saat (TIER-017)

Kliniğin online randevuya açtığı en yakın boş saatler kimliksiz yüzeylerde görünür; saat seçilen kişi §5 bağlantı formuna gider,
talep klinik onaylayınca kesinleşir (§4). Yeni onay akışı ve ikinci bir müsaitlik hesabı **yoktur**.

**Kural (tek yer: `AppointmentRequestIntake.GetNextSlotsAsync`):** §5 `GET /availability` ucunun "Diğer / emin değilim" hesabı
(§11: 30 dk, klinikteki tüm hekimler; §3 müsaitlik kuralı, online en erken/en geç sınırlar dahil). En erken talep anının İstanbul
takvim gününden başlar, en çok 14 gün; ilk `count` saat (varsayılan 6, en çok 12) zaman sırasıyla. Bekleyen talepler saati bloke etmez
(§3), dolu saat dönmez. Görünürlük `ClinicOnlineBookingSettings.PublishesNextAvailableSlots`: online randevu açık **ve**
`showNextAvailableSlots` (varsayılan açık; §3 `PUT` ile kapatılır). Paket kilidi yoktur.

### `GET /api/v1/public/sites/{slug}/next-available-slots?count=6`

Klinik sitesi ucu (CLINIC_SITE_API_CONTRACT.md §6). Kimliksiz; hız sınırı sitenin `public-site` politikası (IP başına dakikada 120).
Site JSON'u gibi önbellek başlığı taşımaz: dolan saat ve kapanan site gecikmesiz düşer.

```json
{ "slots": [ { "date": "2026-11-09", "startUtc": "2026-11-09T06:00:00Z" }, { "date": "2026-11-09", "startUtc": "2026-11-09T06:30:00Z" } ] }
```

Yalnız `date` (İstanbul takvim günü, `yyyy-MM-dd`) ve `startUtc` döner. Bitiş, süre, hekim, dolu saat, randevu, talep, kiracı ve
klinik kimliği dönmez.

| Durum | Yanıt |
|---|---|
| Adres yok ya da biçimsiz, kiracı/klinik pasif, site hiç yayınlanmamış | 404 `PublicSites.NotFound` (site JSON'uyla aynı kapı) |
| Site geçici kapalı ya da abonelikten kapalı | 200 `{ "slots": [] }` (neden ayırt edilmez) |
| Online randevu kapalı, klinik gösterimi kapattı | 200 `{ "slots": [] }` |
| Açık sitenin eski adresi | Güncel adresteki gibi yanıtlanır |
| `count` 1–12 dışı | 400 doğrulama hatası |

Site saat seçilince bağlantıya `?saat={startUtc}` ekler; form neden seçilince o saat o nedenin müsait listesindeyse seçili gelir,
değilse kişi başka saat seçer (talep yine §5 kurallarıyla, `requestedStartUtc` müsait listede olmalı).

**Veteriner bul kartı (TIER-015):** kart aynı yolu kullanır (`OnlineBookingClinicResolver.ResolveByClinicAsync` →
`AppointmentRequestIntake.GetNextSlotsAsync`, aynı `NextAvailableSlotDto` biçimi) ama kendi ucundan: `GET /api/v1/public/vet-finder/next-slots`
(VET_FINDER_API_CONTRACT.md §5; bir istekte bir sayfa kart, klinik başına 60 sn sunucu önbelleği). Bu uç kart için çağrılmaz.

## 13. Ek (2026-11-08): hatırlatmada onayla / iptal et (TIER-019)

Randevu hatırlatma e-postasındaki tek kullanımlık "onayla / iptal et" bağlantısı ve portalın kimliksiz `/randevu-yanit` sayfası
ayrı sözleşmededir: `APPOINTMENT_OWNER_LINK_API_CONTRACT.md` (uçlar `/api/portal/v1/appointment-link/*`, hız sınırı `portal-auth`).
Bildirim merkezine yeni tür: `AppointmentCancelledByOwner` (sayısal `4`; hedef `Appointment`).

## 14. Ek (2026-11-08): talep kaynağı ve kaynak özeti (TIER-016)

Klinik talebin nereden geldiğini ve vitrinin ne getirdiğini sayıyla görür. Kaynak **istatistiktir**: talep kuralları (müsaitlik,
bekleyen talep sınırı, doğrulama kodu, onay) kaynağa bakmaz. Kişisel veri değildir; yönlendiren sayfa adresi, IP ya da tarayıcı
bilgisi saklanmaz. Paket kilidi yoktur.

**Değerler (`AppointmentRequestChannel`, JSON'da sayı, stabil):**

| Sayı | Ad | Panel etiketi | Kim üretir |
|---|---|---|---|
| `0` | `ClinicLink` | Klinik bağlantısı | §5 bağlantı ucu; `channel` yok, boş ya da izinli değil |
| `1` | `ClinicSite` | Kendi sitesi | §5 bağlantı ucu, `channel: "site"` (site bağlantıya `?kaynak=site` ekler) |
| `2` | `OwnerPortal` | Portal | §6 portal ucu, her zaman (gövdede kaynak alanı yok; gönderilse yok sayılır) |
| `3` | `Phone` | Telefon / elle | Henüz üretilmez: personelin talep girdiği bir yol yok |
| `4` | `VetFinder` | Veteriner bul | §5 bağlantı ucu, `channel: "veteriner-bul"` (kart bağlantıya `?kaynak=veteriner-bul` ekler; VET_FINDER_API_CONTRACT.md §6) |
| `null` | — | Bilinmiyor | TIER-016 öncesi talepler (geriye dönük doldurulmaz) |

**Kural (tek yer: `AppointmentRequestChannels.ForLink`, domain):** §5 `POST /requests` gövdesindeki `channel` serbest metin olarak
saklanmaz. Yalnız izinli değer (`site`, `veteriner-bul`; büyük/küçük harf ve baş/son boşluk önemsiz) eşlenir; bilinmeyen, boş, sayı ya
da başka kapının değeri (`portal`, `telefon`) `0 ClinicLink` olur ve talep **reddedilmez**. Değer kimliksiz istemciden geldiği için
kötüye kullanılabilir (ör. doğrudan bağlantı `site` diye gösterilebilir); bu yalnız özetteki sayıları etkiler.

**Akış:** site Randevu bağlantısına `?kaynak=site` ekler (TIER-017 `?saat=` ile birlikte olabilir). Portal `/randevu/{kod}` formu
adresteki `kaynak` değerini olduğu gibi `channel` alanına koyar; doğrulamayı sunucu yapar.

### `GET /api/v1/appointment-requests/channel-summary?from=2026-11-01&to=2026-11-30&clinicId=`

`AppointmentRequests.Manage`. Kapsam §4 kuyruğuyla aynı (aktif klinik bağlamı; yoksa erişilen klinikler; atanmamış `clinicId` 403).
`from`/`to` İstanbul takvim günleri, ikisi de dahil, **talebin oluşturulma tarihine** göre. Tarih aralığı kuralı raporlarla aynı
(`RegisterReportFilter.RangeError`: zorunlu, sıralı, en çok 366 gün; aksi 400 `Registers.DateRangeInvalid` / `Registers.RangeTooLong`).

```json
{ "from": "2026-11-01", "to": "2026-11-30",
  "rows": [ { "channel": 0, "total": 2, "approved": 0 }, { "channel": 1, "total": 5, "approved": 3 },
            { "channel": 2, "total": 1, "approved": 1 }, { "channel": null, "total": 4, "approved": 2 } ],
  "total": 12, "approved": 6 }
```

Satırlar kaynak sayısına göre sıralı, `null` (bilinmiyor) sonda; dönemde talebi olmayan kaynak satırı dönmez. `approved` o
taleplerden bugün `Approved` durumunda olanlardır (onaylanıp randevusu sonra iptal edilen talep `Approved` kalır).

§4 liste ve detay (`summary`) satırına `channel` (sayı ya da `null`) eklendi; `source` (giriş kapısı) değişmedi.
