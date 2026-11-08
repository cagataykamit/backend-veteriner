# Hatırlatmada "onayla / iptal et" bağlantısı — API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: TIER-019 (bağımlılık APPT-013/014). Hata zarfı standart `ProblemDetails` + `code`. Hatırlatma işleyicisi: `ReminderProcessorService` (randevu hatırlatması). İlgili: `ONLINE_APPOINTMENT_API_CONTRACT.md` (portal kimliksiz uçlar), `AUTOMATED_REMINDERS_API_CONTRACT.md`.

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
