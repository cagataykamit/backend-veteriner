# Bekleme listesi — API sözleşmesi (TIER-018)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-018`, ADR-035 (paket; kullanıcı kararı 2026-11-09: Profesyonel ve Kurumsal), ADR-046 (ticari ileti). İlgili: `APPOINTMENT_OWNER_LINK_API_CONTRACT.md` (bağlantı tablosu ve token deseni), `PLAN_FEATURES_API_CONTRACT.md` (paket kapısı), `ONLINE_APPOINTMENT_API_CONTRACT.md` (müsaitlik kuralları). Hata zarfı standart `ProblemDetails` + `code`.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-018-bekleme-listesi`. Migration `AddWaitlist` (yalnız yerel DB). Yeni izin yok.

## 1) Kural özeti

| Konu | Kural |
|---|---|
| Paket | `PlanFeature.Waitlist`: Profesyonel ve Kurumsal. Temel'de yazma uçları ve boşalan saat bilgisi 403 `Subscriptions.FeatureNotInPlan`; liste ve kayıt geçmişi her pakette okunur (paket düşünce kayıt silinmez). Paket düşünce işleyici durur, açık teklifin kabul bağlantısı 404 döner. |
| Yetki | Okuma `Appointments.Read`, yazma (ekleme, kapatma, sıra, elle teklif) `Appointments.Create`. Klinik erişimi randevu kuralıyla aynı (atanmış klinik). |
| Ekleme | Yalnız klinik personeli panelden ekler. **Çalışma varsayımı:** bu dilimde hasta sahibinin kendi kendine ekleme yolu yok. |
| Sıra | Önce eklenme zamanı (`SortKey` = eklenme anı). Klinik bir kaydı bir üste ya da alta kaydırabilir (komşuyla yer değiştirir) ya da kaydı kapatabilir. |
| Saat boşalması | Yalnız **randevu iptali** (`Scheduled → Cancelled`; klinik içi ya da sahibin e-posta bağlantısı). Gelmedi saat boşaltmaz. Boşalan saat = iptal edilen randevunun saati, süresi ve hekimi. |
| Uygunluk | Kayıt sırada; istenen hekim varsa saatin hekimiyle aynı (hekimsiz saat hekim isteyen kayda uymaz); yerel gün tarih aralığında ve seçili günlerde; saat tercihi varsa randevu tamamen içinde; kaydın süresi (neden seçildiyse nedenin varsayılan süresi) saate sığar; hayvan bakımda; bu saat için daha önce teklif almamış; tarih aralığı geçmemiş. Kural tek yerde: `WaitlistEntry.Matches`. |
| Teklif süresi | `WaitlistOffer.ComputeExpiry`: gönderimden 2 saat sonra; randevuya 2 saatten az kaldıysa randevudan 1 saat önce. Bitiş 15 dakikadan yakınsa teklif gönderilmez. Hepsi **çalışma varsayımı**. |
| Tek açık teklif | Aynı saat için ve aynı kayıt için aynı anda en çok bir açık teklif; saat en çok bir kez kabul edilir. Veritabanında süzülmüş benzersiz indeks (`UX_WaitlistOffers_Slot_Open`, `UX_WaitlistOffers_Entry_Open`, `UX_WaitlistOffers_Slot_Accepted`) ve satır sürümü. |
| Sıradakine geçiş | Süre dolunca ya da sahip reddedince işleyicinin sonraki turunda sıradaki uygun kayda. Kabul edilen ya da "saat doldu" kapanan saat yeniden teklif edilmez. |
| Otomatik / elle | `ClinicAppointmentSettings.waitlistAutoOfferEnabled` (varsayılan **açık**, çalışma varsayımı). Kapalıysa işleyici o klinikte teklif açmaz; personel `POST …/offers` ile elle gönderir (açıkken de gönderebilir). |
| İleti türü | İşlem/hizmet iletisidir, ticari değildir (ADR-046). Tür `OwnerNotificationType.WaitlistOffer` (16), kategori hatırlatma: `Client.AcceptsReminders` (personel ve portal tercihi aynı bayrak) kapalıysa gitmez. Yalnız e-posta (tek kullanımlık bağlantı push'a yazılmaz). Tanıtım içermez. |
| E-postasız kayıt | Teklif gitmez; kayıt sırası atlanır. Klinik listede ve boşalan saat bilgisinde `offerBlocker` ile uyarılır; elle teklif 409 `WaitlistOffers.RecipientUnreachable`. |
| Eski iptaller | İşleyici yalnız son 3 gün içinde değişmiş (iptal edilmiş) randevuları otomatik teklif eder (çalışma varsayımı). Daha eskisi elle gönderilebilir. |

## 2) Panel uçları — `/api/v1/waitlist-entries`

### `GET /?clinicId=&veterinarianUserId=&status=` → `200 WaitlistEntryDto[]`

Filtre: klinik, istenen hekim, durum (`Waiting` | `Booked` | `Closed`). Sıradakiler önce ve sırayla. En çok 500 satır (sayfalama yok; çalışma varsayımı). Her pakette.

```json
{ "id": "…", "clinicId": "…", "clientId": "…", "clientName": "Ayşe Yılmaz", "petId": "…", "petName": "Pamuk",
  "preferredVeterinarianUserId": null, "preferredVeterinarianName": null,
  "appointmentType": 2, "reasonId": null, "reasonName": null, "durationMinutes": null,
  "earliestDate": "2026-11-09", "latestDate": "2026-11-30", "preferredDays": "Monday, Wednesday",
  "preferredFromTime": "09:00:00", "preferredToTime": "12:00:00", "note": "Erken gelmek istiyor",
  "status": "Waiting", "position": 1, "isPastRange": false, "offerBlocker": "None",
  "createdAtUtc": "…", "closedAtUtc": null, "closeReason": null, "bookedAppointmentId": null,
  "openOffer": { "id": "…", "status": "Open", "sourceAppointmentId": "…", "slotStartUtc": "…", "durationMinutes": 30,
                 "veterinarianUserId": null, "isAutomatic": true, "createdAtUtc": "…", "expiresAtUtc": "…",
                 "closedAtUtc": null, "appointmentId": null } }
```

`preferredDays`: `None` (her gün) ya da bayrak listesi (`Monday` … `Sunday`). `offerBlocker`: `None` | `NoEmail` | `RemindersOff`. `position` yalnız sıradaki kayıtta dolu.

### `GET /{id}` → `200 { entry, events[], offers[] }`

Geçmiş olayları (`type`): `Added`, `OfferSent`, `OfferExpired`, `OfferDeclined`, `OfferAccepted`, `OfferSlotTaken`, `OfferWithdrawn`, `Closed`, `MovedUp`, `MovedDown`; her biri `occurredAtUtc`, `actorUserId`/`actorName` (sahip ve işleyici için boş), `offerId`, `offerSlotStartUtc`. Her pakette.

### `POST /` → `200 WaitlistEntryDto` (Profesyonel, Kurumsal)

```json
{ "clinicId": "…", "clientId": "…", "petId": "…", "preferredVeterinarianUserId": null, "appointmentType": 2,
  "reasonId": null, "durationMinutes": null, "earliestDate": "2026-11-09", "latestDate": "2026-11-30",
  "preferredDays": "None", "preferredFromTime": null, "preferredToTime": null, "note": null }
```

Kurallar: hayvan müşterinin ve bakımda; istenen hekim kliniğin aktif hekimi; neden kliniğin aktif nedeni (süre boşsa nedenin varsayılan süresi); başlangıç bugünden önce olamaz, aralık en çok 180 gün; saat aralığının bitişi başlangıçtan sonra; not en çok 500 karakter. Hata `400 Waitlist.Validation`, `404 Pets.NotFound`.

### `POST /{id}/close` `{ "reason": "…" }` → `200`

Kayıt `Closed`; açık teklif `Withdrawn`, kabul bağlantısı kapanır. Sırada değilse `409 Waitlist.EntryNotOpen`. Her pakette (paket düşünce de kapatılabilir).

### `POST /{id}/move` `{ "direction": "Up" | "Down" }` → `200` (Profesyonel, Kurumsal)

Komşu yoksa değişiklik olmadan `200`. Eşzamanlı değişiklik `409 WaitlistOffers.ConcurrencyConflict`.

### `POST /{id}/offers` `{ "sourceAppointmentId": "…" }` → `200 WaitlistOfferDto` (Profesyonel, Kurumsal)

Elle teklif. Hatalar (409): `WaitlistOffers.SlotNotReleased` (randevu iptal edilmemiş ya da saati geçmiş), `WaitlistOffers.SlotTaken` (saat kabul edildi ya da doldu), `WaitlistOffers.OfferAlreadyOpen`, `WaitlistOffers.EntryNotMatching` (kayıt saate uymuyor ya da bu saat için teklif aldı), `WaitlistOffers.RecipientUnreachable`, `WaitlistOffers.TooLate`; saat randevuya uygun değilse randevu kodları (`Appointments.ClinicTimeConflict`, `Appointments.PetTimeConflict`, `Appointments.VeterinarianTimeConflict`, `Appointments.OutsideWorkingHours`…).

### `GET /slots/{appointmentId}` → `200 WaitlistSlotDto` (Profesyonel, Kurumsal)

İptal ekranı ve elle teklif için: `isReleased`, `autoOfferEnabled`, `eligibleCount` (teklif alabilecek), `blockedCount` (uyan ama e-postası ya da tercihi olmayan), `openOffer`, `isBooked`, `candidates[]` (`entryId`, `position`, `petName`, `clientName`, `offerBlocker`). Başka kiracının randevusunda `404 Appointments.NotFound`.

### `GET /today?clinicId=` → `200 { waitingCount, releasedSlots: WaitlistSlotDto[] }` (Profesyonel, Kurumsal)

Günün kalan saatlerinde (İstanbul günü) iptal edilmiş randevular ve sıradaki kayıt sayısı.

### Klinik ayarı

`GET/PUT /api/v1/clinics/{id}/appointment-settings`: `waitlistAutoOfferEnabled`. PUT'ta gönderilmezse mevcut değer korunur.

## 3) Teklif e-postası

Konu `Boşalan randevu saati`. Metin: sahip adı, klinik adı, hayvan adı, saat (İstanbul), `{PortalBaseUrl}/bekleme-teklifi?token=…` bağlantısı, teklifin bitişi, "yalnız bir kez kullanılabilir", yanıt verilmezse sıradakine geçildiği ve listedeki yerin korunduğu. Teşhis, not, hekim adı yoktur. E-posta outbox'a teklif, bağlantı ve geçmiş olayıyla aynı kayıtta yazılır.

## 4) Kabul bağlantısı (kimliksiz) — `/api/portal/v1/waitlist-offer`

TIER-019 tablosu ve deseni: `AppointmentOwnerLinks`, yeni amaç `Purpose = 2 (WaitlistOffer)`, yeni alan `WaitlistOfferId`; `AppointmentId` saati boşaltan (iptal edilmiş) randevudur. Token 256 bit, depoda yalnız SHA-256 özeti, süre teklifin bitişi, tek kullanımlık. Bu bağlantı randevu onayla/iptal ve takvim uçlarında 404'tür. Token gövdede (`{ "token": "…" }`), hız sınırı `portal-auth`.

| Uç | Yanıt | Not |
|---|---|---|
| `POST /preview` | `200 { clinicName, scheduledAtUtc, durationMinutes, petName, expiresAtUtc }` | Durumu değiştirmez, denetime yazılmaz. Portal sayfası `/bekleme-teklifi` açılınca yalnız bu çağrılır. |
| `POST /accept` | `204` | Saat hâlâ uygunsa (klinik içi randevu kuralları, `AppointmentSlotRules`) randevu oluşur (`Scheduled`, not "Bekleme listesinden (sahip teklifi kabul etti)", kaydın türü ve nedeni, saatin hekimi ve süresi), `appointment.created.v1` olayı yazılır, kayıt `Booked`, teklif `Accepted`, bağlantı kapanır; klinik bildirim merkezine `WaitlistOfferAccepted` (5), alıcı kliniğe erişen `Appointments.Create` yetkililer. |
| `POST /decline` | `204` | Teklif `Declined`; kayıt sırada kalır; sonraki turda sıradakine geçilir. |

| Kod | HTTP | Durum |
|---|---|---|
| `WaitlistOfferLinks.NotFound` | 404 | Token bilinmiyor, boş/çok uzun, kullanılmış, süresi dolmuş; teklif ya da kayıt kapanmış; kiracı/klinik pasif, abonelik salt okunur, pakette bekleme listesi yok. **Sebep ayırt edilmez.** |
| `WaitlistOffers.SlotTaken` | 409 | Kabul anında saat doluydu: randevu oluşmaz, teklif `SlotTaken` kapanır, sıradakine teklif açılmaz; kayıt sırada kalır. |
| `WaitlistOffers.ConcurrencyConflict` | 409 | Aynı teklif aynı anda yanıtlandı; hiçbir şey yazılmaz (randevu dahil). |

**Eşzamanlılık:** iki kabul aynı teklifte satır sürümüne, aynı saatte `UX_WaitlistOffers_Slot_Accepted` indeksine takılır; kaybeden istek tek `SaveChanges` olduğu için randevu da yazmaz.

**Denetim:** `Waitlist.Offer.Accept` / `Waitlist.Offer.Decline`, `TargetType=WaitlistOffer`, `TargetId` teklif (token geçersizse boş), `ActorUserId` boş, `RequestPayload={"actorKind":"OwnerLink"}`. Panel komutları `AuditBehavior` ile: `Waitlist.Entry.Create`, `Waitlist.Entry.Close`, `Waitlist.Entry.Move`, `Waitlist.Offer.Send`.

## 5) İşleyici

`WaitlistOfferProcessor` (barındırıcı `WaitlistOfferProcessorHostedService`, ayar `Waitlist:Processor:Enabled`, `IntervalSeconds` varsayılan 60). Her tur, sıradaki kaydı ya da açık teklifi olan kiracılarda (aktif, yazılabilir abonelik, paket özelliği açık): (1) süresi dolan açık teklifleri `Expired` kapatır, bağlantıyı kapatır, geçmişe yazar; (2) otomatik teklifi açık ve aktif kliniklerde boşalan saatleri (saati `şimdi + 1 saat 15 dk`'dan sonra, son 3 günde iptal) sırayla dolaşır; saat için açık, kabul edilmiş ya da "saat doldu" teklif yoksa sıradaki uygun ve ulaşılabilir kayda teklif gönderir. Hayvanın o saatte başka randevusu varsa sıradakine geçer; saat kendisi uygun değilse (klinik/hekim çakışması, çalışma saati) kimseye teklif açmaz. Entegrasyon testinde kapalıdır, turlar elle çalıştırılır.

## 6) Migration

`AddWaitlist` (komut DB): `WaitlistEntries`, `WaitlistOffers`, `WaitlistEntryEvents` tabloları; `AppointmentOwnerLinks.WaitlistOfferId` (boş olabilir, süzülmüş indeks, FK `NoAction`); `ClinicAppointmentSettings.WaitlistAutoOfferEnabled` (mevcut satırlar `1`). Query DB değişmez.

## 7) Testler

- Domain: `WaitlistTests` (uygunluk, tarih ve saat doğrulaması, sıra değişimi, teklif süresi ve tek geçiş, bağlantının randevu eylemi ve takvim vermemesi, ileti kategorisi).
- Application: `WaitlistOfferRulesTests` (e-posta ve tercih, boşalan saat tanımı, e-posta içeriği), `UpdateClinicAppointmentSettingsCommandHandlerTests` (ayar korunur), `TenantPlanFeatureEvaluatorTests`.
- Entegrasyon: `WaitlistIntegrationTests` (iptalde sıradakine teklif ve tek açık teklif, süre dolumu ve retle sıradakine geçiş, sayfa bilgisinin durumu değiştirmemesi ve kabulde randevu, bildirim, denetim; eşzamanlı dört kabulde tek randevu; saat dolmuşken kabul; tercih kapalı ve e-postasız kayıt; otomatik teklif kapalıyken elle teklif; kapatmada geri çekme ve sıra kaydırma; Temel'de 403; kiracı izolasyonu).

## 8) Bilinen sınırlar

- Bugün ekranı ve iptal ekranı bilgisi hesaplanır; sayfalama yok (liste 500 satır).
- Kabul ile personelin aynı anda aynı saate elle randevu açması, mevcut randevu oluşturma kuralı gibi kilitsizdir (klinik/hekim çakışma denetimi okumaya dayanır); bekleme listesi kabulleri kendi aralarında veritabanı indeksiyle korunur.
- Ham token e-postanın outbox yükünde durur (TIER-019 §7 ile aynı).
- Hasta sahibinin portaldan kendini listeye eklemesi ve SMS/WhatsApp kanalı yok.
