# Hatırlatmada "onayla / iptal et" bağlantısı — API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: TIER-019 (bağımlılık APPT-013/014); "takvime ekle" TIER-020 (§8). Hata zarfı standart `ProblemDetails` + `code`. Hatırlatma işleyicisi: `ReminderProcessorService` (randevu hatırlatması). İlgili: `ONLINE_APPOINTMENT_API_CONTRACT.md` (portal kimliksiz uçlar), `AUTOMATED_REMINDERS_API_CONTRACT.md`.

## 1) Model

- **Bağlantı** (`AppointmentOwnerLinks`): hatırlatma e-postası başına bir kayıt; tek token iki eylemi (onayla, iptal et) taşır.
  - Token: 256 bit rastgele, URL güvenli (portal davetiyle aynı üretici). Depoda yalnız **SHA-256 özeti** (`TokenHash`, benzersiz); ham token yalnız e-postadadır.
  - Randevuya bağlıdır (`AppointmentId`, `TenantId`). Süre **randevu saatinde** dolar (`ExpiresAtUtc` = gönderimdeki `scheduledAtUtc`).
  - **Tek kullanımlık:** bir eylem bağlantıyı kapatır (`ClosedAtUtc`, `UsedAction`); aynı randevunun diğer açık bağlantıları da kapanır. Kapanan bağlantıyla diğer eylem de yapılamaz.
  - Randevu yeniden zamanlanırsa bağlantı geçersizdir (saat eşleşmez); yeni saat için yeni hatırlatma gerekir.
- **Randevu:** `ownerConfirmedAtUtc` (yeni, boş olabilir). Sahip onayladığında yazılır; **durum değişmez**. Randevu saati değişince temizlenir.
- **Klinik ayarı:** `ClinicAppointmentSettings.ownerCancelLinkEnabled` (varsayılan **açık**; kayıt yoksa da açık). "Onayla" her zaman açıktır.

## 2) E-posta (hatırlatma işleyicisi)

Randevu hatırlatmasına iki satır eklenir: `{PortalBaseUrl}/randevu-yanit?token=…&islem=onayla` ve `…&islem=iptal`. `PortalBaseUrl` boşsa `{App:FrontendBaseUrl}/portal`.

- İptal kapalıysa iptal adresi yerine "Gelemeyecekseniz iptal için lütfen kliniği arayın[: telefon]" satırı çıkar.
- Metin hizmet bilgilendirmesidir, ticari ileti değildir (HUKUKI-ONAY-LISTESI A4 varsayımı); tanıtım içermez.
- Bağlantı, e-posta (outbox) ve gönderim günlüğüyle aynı kayıtta yazılır; tekrar engeli (dedupe) çakışmasında bağlantı da yazılmaz.
- Paket kilidi yoktur (randevu hatırlatması Temel'dedir).

## 3) Uçlar (kimliksiz) — `/api/portal/v1/appointment-link`

Hız sınırı `portal-auth` (IP başına). Token **gövdede** taşınır (`{ "token": "…" }`): istek yolunda, erişim günlüğünde ve denetim kaydında görünmez. Portal sayfası `/randevu-yanit` açılınca yalnız `preview` çağrılır; eylem sayfadaki düğmeyle POST edilir. E-posta güvenlik tarayıcısının bağlantıyı açması randevuyu değiştirmez.

### `POST /preview` → `200`

```json
{ "clinicName": "Merkez", "scheduledAtUtc": "2026-11-09T07:00:00Z", "petName": "Pamuk", "cancelEnabled": true }
```

Asgari veri: telefon, not, teşhis, sahip bilgisi yoktur. Durumu değiştirmez (denetime yazılmaz).

### `POST /confirm` → `204`

`ownerConfirmedAtUtc` yazılır, bağlantılar kapanır. Durum `Scheduled` kalır.

### `POST /cancel` → `204`

Mevcut iptal geçişi (`Scheduled → Cancelled`); not satırı `İptal: sahip e-postadan iptal etti`. Klinik içi iptalle aynı olay (`appointment.cancelled.v1`) üretilir. Kliniğe bildirim: tür `AppointmentCancelledByOwner` (sayısal `4`), hedef `Appointment` + randevu kimliği; alıcı kliniğe erişen `Appointments.Cancel` yetkililer.

### Hatalar

| Kod | HTTP | Durum |
|-----|------|-------|
| `AppointmentLinks.NotFound` | 404 | Token bilinmiyor, boş/çok uzun, kullanılmış, süresi dolmuş; randevu yeniden zamanlanmış, iptal, tamamlanmış ya da gelmedi; kiracı/klinik pasif ya da abonelik salt okunur. **Sebep ayırt edilmez** (tek metin). |
| `AppointmentLinks.CancelDisabled` | 409 | Klinik iptal bağlantısını kapattı (yalnız geçerli token ile). Bağlantı tüketilmez; onay yapılabilir. |
| `Appointments.ConcurrencyConflict` | 409 | Eşzamanlı değişiklik; tekrar denenir. |

## 4) Denetim

Her eylem denemesi (başarılı/başarısız) yazılır: `Appointment.OwnerLink.Confirm` / `Appointment.OwnerLink.Cancel`, `TargetType=Appointment`, `TargetId` randevu (token geçersizse boş), `ActorUserId` boş, `RequestPayload = {"actorKind":"OwnerLink"}` — aktör "hasta sahibi (bağlantı)". Token yazılmaz.

## 5) Panel tüketicileri

- `GET /api/v1/visits/today`: satırda `ownerConfirmedAtUtc` (randevusuz satırda `null`).
- `GET /api/v1/appointments/{id}`: `ownerConfirmedAtUtc`.
- `GET/PUT /api/v1/clinics/{id}/appointment-settings`: `ownerCancelLinkEnabled`. PUT'ta alan gönderilmezse mevcut değer korunur.
- Bildirim merkezi: yeni tür `AppointmentCancelledByOwner`.
- İptal edilen randevu Bugün'de görünmez (mevcut kural); boşalan saat takvimde görünür.

## 6) Migration

`AddAppointmentOwnerLinks` (komut DB): `AppointmentOwnerLinks` tablosu, `Appointments.OwnerConfirmedAtUtc`, `ClinicAppointmentSettings.OwnerCancelLinkEnabled` (mevcut satırlar `1`). Query DB değişmez.

## 7) Bilinen sınır

Ham token e-postanın outbox yükünde (komut DB) gönderim sonrası da durur; portal daveti ve kod e-postalarıyla aynı desendir. Token tek kullanımlıktır ve randevu saatinde geçersizleşir.

## 8) Takvime ekle (TIER-020)

Randevu hatırlatma e-postası ve online randevu talebinin onay e-postası (`Approved` ve `Rescheduled`) "takvime ekle" satırları taşır; portalda `/randevu-yanit` sayfası ve Randevularım listesi aynı çıktıyı verir. Paket kilidi yoktur. Metin hizmet bilgilendirmesidir, tanıtım içermez.

### 8.1 Tek kaynak ve asgari veri

Üretim tek sınıftadır: `Application/Appointments/Calendar/AppointmentCalendar` (.ics, Google bağlantısı, e-posta satırları); etkinlik verisi `AppointmentCalendarEvent.Create`.

| Alan | Değer |
|------|-------|
| Başlık | `{Klinik adı} randevusu` |
| Başlangıç / bitiş | Randevu saati (UTC) + randevu süresi (geçersizse varsayılan 30 dk) |
| Konum | Kliniğin adresi; ilçe ve il adreste yazılı değilse sona eklenir. Hiçbiri yoksa alan yok. |
| Açıklama | `Hayvan: …` ve `Klinik telefonu: …` (boş olan satır yok) |

Teşhis, randevu notu, hekim adı ve sahip bilgisi girmez.

### 8.2 .ics dosyası (RFC 5545)

- Tek `VEVENT`; `VERSION:2.0`, `PRODID:-//Vetinity//Randevu//TR`, `CALSCALE:GREGORIAN`, `METHOD:PUBLISH`.
- `UID`: `{SHA-256("vetinity-appointment:" + randevuId) ilk 16 bayt, hex}@vetinity.com`. Randevuya bağlı ve sabittir; randevu kimliğini açık etmez. Hatırlatma ve onay dosyaları takvimde aynı etkinliği günceller.
- `DTSTAMP` üretim anı; `DTSTART`, `DTEND` **UTC** (`…Z`).
- `SUMMARY`, `LOCATION`, `DESCRIPTION` TEXT kaçışlı: `\\`, `\;`, `\,`, satır sonu `\n`; diğer kontrol karakterleri atılır.
- Satırlar CRLF ile biter; 75 oktette katlanır (devam satırı tek boşlukla başlar), çok baytlı UTF-8 karakter bölünmez. Dosya UTF-8, BOM yok.
- `VALARM`: `ACTION:DISPLAY`, `TRIGGER:-PT1H` (1 saat önce).
- Yanıt: `200`, `Content-Type: text/calendar; charset=utf-8`, `Content-Disposition: attachment; filename=randevu.ics`, `Cache-Control: no-store`.

**Saat dilimi kararı:** dosyada ve Google bağlantısında UTC. `TZID=Europe/Istanbul` + `VTIMEZONE` seçilmedi: Türkiye 2016'dan beri sabit UTC+3'tür, elle yazılmış VTIMEZONE bloğu hatalı olabilir ve bazı istemciler onu yok sayar. UTC'yi her istemci kendi saat dilimine doğru çevirir. Gösterim (e-posta metni, portal) İstanbul saatidir.

### 8.3 Google Takvim bağlantısı

`https://calendar.google.com/calendar/render?action=TEMPLATE&text=…&dates={başlangıç}Z/{bitiş}Z&details=…&location=…&ctz=Europe/Istanbul`. Değerler URL kaçışlıdır; boş açıklama/konumda parametre yoktur. Token taşımaz.

### 8.4 .ics nasıl verilir: e-postada bağlantı

E-posta outbox'ı (`EmailOutboxPayload`) ek taşımaz; ek yalnız anlık gönderici (`IEmailSenderImmediate`) üzerindedir. Bu yüzden .ics **e-postaya bağlantı** olarak girer:

```
Randevuyu takviminize eklemek için:
Google Takvim: https://calendar.google.com/calendar/render?action=TEMPLATE&…
Diğer takvimler (.ics dosyası): {App:BaseUrl}/api/portal/v1/appointment-link/calendar.ics?token=…
```

- Token mevcut TIER-019 deseni ve tablosudur (`AppointmentOwnerLinks`); ikinci mekanizma yoktur. Yeni alan `Purpose`: `0 Respond` (onayla/iptal; takvimi de verir), `1 Calendar` (yalnız takvim). Takvim tokeni GET adresinde taşındığı için ayrı üretilir ve **eylem yapamaz**: onayla/iptal tokeni API istek adresine hiç girmez.
- Hatırlatma e-postası iki bağlantı yazar (yanıt + takvim); onay e-postası yalnız takvim bağlantısı yazar (onay e-postasında onayla/iptal yoktur). Bağlantılar e-postayla aynı kayıtta yazılır; tekrar engelinde hiçbiri yazılmaz.
- Süre randevu saatinde dolar (`ExpiresAtUtc`). Yeniden zamanlanan, iptal edilen, tamamlanan randevuda geçersizdir (TIER-019 kuralı).
- Takvim okumadır: **tek kullanım kuralı uygulanmaz**; onayla kapanmış yanıt bağlantısı da takvim dosyasını vermeye devam eder. Bir eylem takvim bağlantısını kapatmaz.
- `App:BaseUrl` (API kökü, ör. `https://api.vetinity.com`) boşsa takvim bağlantısı üretilmez; e-postada yalnız Google satırı kalır. Üretim ortamında bu ayar verilmelidir.
- Onay e-postasında takvim satırları şablonun (ya da personelin düzenlediği metnin) sonuna eklenir; personel bunları düzenlemez. `GET /appointment-requests/{id}/email-preview` bu satırları göstermez.

### 8.5 Uçlar

Hepsi kimliksiz, `/api/portal/v1/appointment-link`, hız sınırı `portal-auth` (IP başına). Durum değiştirmez, bağlantıyı tüketmez, denetime yazılmaz.

| Uç | Token | Kullanım |
|----|-------|----------|
| `GET /calendar.ics?token=…` | Sorgu dizesinde | E-postadaki bağlantı (takvim tokeni) |
| `POST /calendar` `{ "token": "…" }` | Gövdede | `/randevu-yanit` sayfasındaki ".ics indir"; onaydan sonra da çalışır |

Hata: `404 AppointmentLinks.NotFound` (sebep ayırt edilmez; §3 ile aynı kapılar).

`POST /preview` yanıtına `googleCalendarUrl` (metin) eklendi. Bu bağlantı kliniğin adresini ve telefonunu taşır (kliniğin herkese açık iletişim bilgisi; sahip bilgisi değil).

### 8.6 Portal (oturumlu) — Randevularım

- `GET /api/portal/v1/links/{linkId}/appointments` satırında `googleCalendarUrl` (metin ya da `null`): yalnız planlı ve saati gelmemiş randevuda doludur (`Appointment.CanOwnerRespond` ile aynı kural).
- `GET /api/portal/v1/links/{linkId}/appointments/{appointmentId}/calendar.ics` → `200` .ics. Randevu bağın müşterisinin değilse ya da takvime eklenemezse `404 Portal.Appointments.NotFound` (ayırt edilmez). Hız sınırı `portal`; okuma denetimi `Portal.Read.AppointmentCalendar`.

### 8.7 Migration

`AddAppointmentOwnerLinkPurpose` (komut DB): `AppointmentOwnerLinks.Purpose int not null default 0`. Mevcut satırlar yanıt bağlantısıdır. Query DB değişmez.

### 8.8 Bilinen sınır

- Yeniden zamanlanan randevuya yeni hatırlatma gitmez (TIER-019 boşluğu sürüyor); eski takvim bağlantısı geçersizdir, sahip yeni saati portaldan ekleyebilir.
- Takvim tokeni e-postanın outbox yükünde durur (§7 ile aynı).
