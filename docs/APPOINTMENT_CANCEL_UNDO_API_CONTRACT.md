# Randevu iptalini geri alma ("Geri al") API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: kullanıcı dostu denetim B2 (2026-11-15), randevu iptal bildiriminde 10 saniyelik "Geri al". Genel randevu sözleşmesi `BACKEND-CONTRACT-STANDARD.md`; hata zarfı standart `ProblemDetails` + `extensions.code`. Migration yok.

## 1) Kural (tek yer)

`Appointment.CancellationUndoWindow` = **10 sn**: istemcinin "Geri al" gösterdiği süre.
`Appointment.CancellationUndoLimit` = pencere + **5 sn** ağ payı: sunucunun geri almayı kabul ettiği son an ve bekleme listesinin iptali "kesinleşmiş" saydığı an.

- İptal anı randevunun `ChangedAtUtc` alanıdır (kayıtta her değişiklikte sunucu saatiyle yazılır; iptalden sonra randevu başka bir uçla değiştirilemez).
- Yalnız `Cancelled` randevu geri alınır; geri alma `Cancelled → Scheduled` yapar.
- İptal gerekçesi notun sonuna "İptal: …" satırı olarak eklenmişse geri almada bu satır silinir (randevu iptal öncesi hâline döner). İz audit kaydındadır.

## 2) Uçlar

### `POST /api/v1/appointments/{id}/cancel` — `Appointments.Cancel` (değişti)

Gövde değişmedi: `{ "reason": "string?" }`. Yanıt artık **`200 OK`** (önce `204`):

```json
{ "undoWindowSeconds": 10 }
```

- İstemci "Geri al"ı bu süre kadar gösterir; süreyi kendisi sabitlemez.
- Audit: `Appointment.Cancel` (yeni; gerekçe yüke yazılır).
- Diğer davranış aynı: yalnız `Scheduled` iptal edilir, değilse `400 Appointments.InvalidStatusTransition`; `404 Appointments.NotFound`; `403 Clinics.AccessDenied`; `409 Appointments.ConcurrencyConflict`.

### `POST /api/v1/appointments/{id}/cancel/revert` — `Appointments.Cancel` (yeni)

Gövde yok. `204 NoContent`. `Cancelled → Scheduled`.

Sıra ve hatalar:

| Durum | HTTP | Kod |
|---|---|---|
| Randevu yok / başka kiracı / klinik bağlamı dışında | 404 | `Appointments.NotFound` |
| Kliniğe atanmamış kullanıcı | 403 | `Clinics.AccessDenied` |
| Randevu `Cancelled` değil | 400 | `Appointments.InvalidStatusTransition` |
| Süre doldu (`now > ChangedAtUtc + CancellationUndoLimit`) | 409 | `Appointments.CancellationUndoExpired` |
| Boşalan saat bekleme listesinde birine teklif edildi (açık, kabul edilmiş ya da "saat doldu" teklif) | 409 | `Appointments.CancellationUndoBlocked` |
| Bu arada saate başka randevu girdi | 409 | `Appointments.ClinicTimeConflict` (üst üste binmeye izin yoksa), `Appointments.PetTimeConflict`, `Appointments.VeterinarianTimeConflict` |
| Eşzamanlı değişiklik | 409 | `Appointments.ConcurrencyConflict` |

- Slot hizası ve çalışma saati yeniden denetlenmez: randevu bu kurallarla zaten açılmıştı, 10 sn içinde ayar değişikliği geri almayı engellememeli.
- Audit: `Appointment.CancelRevert`.
- Olay: `appointment.updated.v1` (tam anlık görüntü, `status = Scheduled`); okuma projeksiyonu ve Bugün sayaçları buradan güncellenir.

## 3) Yan etkiler

- **Hatırlatmalar:** İşleyici her turda adayları `Status = Scheduled` ve gönderim günlüğü olmayan randevulardan seçer. Geri alınan randevu sonraki turda kendiliğinden yeniden aday olur. İptalden önce gönderilmiş hatırlatma tekrar gönderilmez, çünkü aynı saat için günlük var.
- **Bekleme listesi (TIER-018):** Otomatik işleyici boşalan saati ancak `ChangedAtUtc <= now − CancellationUndoLimit` olduğunda ele alır. Geri alma süresi içinde otomatik teklif gitmez. Personel aynı süre içinde elle teklif gönderirse geri alma `409 Appointments.CancellationUndoBlocked` döner; teklif geçerli kalır.
- **Hasta sahibine iptal e-postası:** Personelin iptalinde sahibe ileti gönderilmiyor (yalnız sahibin bağlantıyla iptalinde kliniğe bildirim var). Erteleme gerekmedi.
- **Sahibin bağlantıyla iptali** (`POST /api/portal/v1/appointment-link/cancel`) bu uçla geri alınabilir, ama pratikte personelin göreceği bir "Geri al" yok. Kural kim iptal ettiğine bakmaz.

## 4) İstemci (panel)

- İptal başarılı olunca bildirim: "Randevu iptal edildi." + **Geri al** düğmesi. Bildirim `undoWindowSeconds` süre açık kalır.
- Geri al → `POST …/cancel/revert`. Başarılıysa "İptal geri alındı." ve ekran yenilenir. `409` gelirse sunucu metni gösterilir (ör. "Geri alma süresi doldu.").
