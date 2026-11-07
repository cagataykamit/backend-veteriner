# Otomatik hatırlatıcılar API sözleşmesi — TIER-003

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-003`, ADR-035 (2026-11-06 paket yönü). İlgili: `PLAN_FEATURES_API_CONTRACT.md`, `PORTAL_API_CONTRACT.md` §10 ve §13 (bildirim türü × kanal), `TRIAL_LIMITS_API_CONTRACT.md` K2 (deneme e-posta bütçesi).

> **Durum:** Uygulandı (kod + test), dal `feat/tier-003-otomatik-hatirlatici`. Migration **`AddClinicReminderRules`** (yalnız yerel DB). Yeni izin yok. §5'teki maddeler **çalışma varsayımıdır, kullanıcı onayı bekliyor.**

---

## 1) Kapsam

Klinik başına üç kural. Paket: Profesyonel ve Kurumsal (`PlanFeature.AutomatedReminders`). Kanal yalnız e-posta. Bugünkü randevu ve aşı e-posta hatırlatması (`/reminders/settings`) bu işin dışındadır, her pakette aynen kalır.

| `ruleType` | Ne zaman | N anlamı | Varsayılan N | Sınır | Kategori |
|---|---|---|---|---|---|
| `FollowUp` | Tedavideki kontrol tarihi (`Treatment.FollowUpDateUtc`) önümüzdeki N gün içinde | Kaç gün önce | 3 | 1–30 | Hatırlatma |
| `Debt` | Ödenmemiş en eski ücretin üzerinden N gün geçti | Kaç gün sonra | 30 | 1–365 | Hatırlatma |
| `NoReturn` | Hayvanın son gelişinin üzerinden N gün geçti, planlı randevusu yok | Kaç gün sonra | 180 | 30–730 | **Ticari ileti** |

Kuralların ve sınırların tek yeri `ClinicReminderRuleDefinition`; kategori tek yeri `OwnerNotificationChannelPolicy` (`FollowUpReminder`, `DebtReminder`, `NoReturnReminder`). Bu türler `GET /portal-notification-rules` matrisinde de görünür; push ve WhatsApp `Disabled`.

## 2) Ticari ileti sınırı

`NoReturn` hizmeti tanıtıp geri çağırdığı için ticari elektronik ileti sayılabilir (6563 sayılı Kanun; hukuki görüş alınmadı). Bu yüzden:

- `OwnerNotificationChannelPolicy.MarketingSendingEnabled` `false` olduğu sürece kural açık olsa da **hiç aday aranmaz, e-posta gitmez, günlük yazılmaz.** Yanıtta `sendingAvailable: false` döner, panel bunu açıkça yazar.
- Anahtar açılınca yalnız bu klinikte aktif portal bağında ticari ileti onayı olan (`OwnerAccountClientLink.MarketingConsentAtUtc`) sahibe gider. Açmadan önce İYS kaydı ve metindeki ret/kimlik yükümlülükleri ayrıca karşılanmalıdır; bu iş kapsamında yapılmadı.
- `FollowUp` ve `Debt` mevcut hizmet ilişkisine dair bilgilendirme sayılarak `Reminder` kategorisine alındı. Bu da hukuki görüş değil, çalışma varsayımıdır.

## 3) Uçlar

### `GET /api/v1/reminders/clinics/{clinicId}/rules` — `Reminders.Read`

Paketten bağımsız okunur. Kayıt yoksa kural kapalı ve varsayılan gündedir.

```json
{ "clinicId": "…",
  "rules": [
    { "ruleType": "FollowUp", "isEnabled": false, "days": 3, "defaultDays": 3, "minDays": 1, "maxDays": 30, "sendingAvailable": true, "updatedAtUtc": null },
    { "ruleType": "Debt", "isEnabled": false, "days": 30, "defaultDays": 30, "minDays": 1, "maxDays": 365, "sendingAvailable": true, "updatedAtUtc": null },
    { "ruleType": "NoReturn", "isEnabled": false, "days": 180, "defaultDays": 180, "minDays": 30, "maxDays": 730, "sendingAvailable": false, "updatedAtUtc": null }
  ] }
```

### `PUT /api/v1/reminders/clinics/{clinicId}/rules` — `Reminders.Manage`

Gövde `{ "rules": [ { "ruleType": "Debt", "isEnabled": true, "days": 30 } ] }`. Listede olmayan kural değişmez. Yanıt GET şeklindedir. Denetim `Reminders.Rules.Update`, hedef `ClinicId=…`. Salt okunur kiracıda yazma kapısı geçerlidir.

| Kod | HTTP | Durum |
|---|---|---|
| `Subscriptions.FeatureNotInPlan` | 403 | Listede `isEnabled: true` var ve pakette özellik yok (`requiredPlanCode: "Pro"`). Kapatmak ve gün değiştirmek pakete bakmaz |
| `Reminders.Rules.Validation.Days` | 400 | Gün sınır dışı |
| `Validation.FluentValidation` | 400 | Boş liste, bilinmeyen kural, aynı kural iki kez |
| `Clinics.NotFound` | 404 | Klinik bu kiracıda yok |
| `Clinics.AccessDenied` | 403 | Kliniğe atanmamış kullanıcı |

`GET /reminders/logs` yeni değerleri de döner: `reminderType` `FollowUp`, `Debt`, `NoReturn`; `sourceEntityType` `Treatment`, `Charge`, `Pet`.

## 4) İşleyici

Mevcut `ReminderProcessorService` turuna eklendi (`ReminderProcessorService.AutomatedRules.cs`); ayrı zamanlayıcı yoktur (`Reminders:Processor`, 5 dk). Kuyruk mevcut outbox hattıdır.

| Konu | Kural |
|---|---|
| Çalışma koşulu | Kural açık, klinik ve kiracı aktif, abonelik yazılabilir (`ReadOnly` ve iptalde çalışmaz), pakette özellik var, kiracının e-posta kanalı açık (`TenantReminderSettings` yoksa açık) |
| Paket düşünce | Kural silinmez, yalnız çalışmaz; yükseltince kaldığı yerden sürer |
| Dışlama | `Client.AcceptsReminders` ve `Pet.ReceivesCare` (mevcut tek kural). Dışlanan kayıt günlüğe yazılmaz. Hatırlatma tercihi KVKK Ret ile ve portaldan kapanır |
| Tekrar engeli | `ReminderDispatchLog` benzersiz anahtarı: `followup:{treatmentId}:{yyyyMMdd}` (kontrol tarihi değişince yeni ileti), `debt:{chargeId}`, `noreturn:{petId}:{yyyyMMdd}` |
| `FollowUp` | Hayvanın gelecekte planlı randevusu varsa gitmez (randevu hatırlatması yeter) |
| `Debt` | Müşteri başına yalnız en eski ödenmemiş ücret aday olur; o ödenene kadar yinelenmez. Bağlanmamış ödemesi ya da açık kredisi olan müşteriye ve ücreti vefat eden hayvana ait kayda gitmez |
| `NoReturn` | Son geliş kiracı genelindedir (başka şubeye gelen dönmüş sayılır); planlı randevusu olana gitmez |
| Birikmiş kayıt | `EnabledAtUtc`'den önce vadesi dolan `Debt` ve `NoReturn` kaydına ileti gitmez |
| Metin | Asgari veri: teşhis, tedavi adı ve **tutar yazılmaz**; sonunda "almak istemiyorsanız kliniğinize bildirebilirsiniz" notu |
| Deneme | Pakete göre davranır; `TRIAL_LIMITS` K2 günlük 20 e-posta bütçesini randevu ve aşıyla paylaşır |
| E-posta yok | Günlüğe `Skipped` yazılır |

## 5) Çalışma varsayımları, kullanıcı onayı bekliyor

1. Tüm kurallar varsayılan kapalı; klinik açınca çalışır.
2. Açınca geçmiş kayıtlara toplu ileti gitmez (`EnabledAtUtc`).
3. Borç iletisinde tutar yazılmaz.
4. `FollowUp` ve `Debt` ticari ileti sayılmaz, `NoReturn` sayılır (§2). Sonuçta `NoReturn` bugün e-posta göndermez.
5. Kural listesi ve varsayılan N değerleri ajan önerisidir.

## 6) Testler

- Domain: `ClinicReminderRuleTests`, `Channel_Policy_Matrix` yeni satırları.
- Application: `TenantPlanFeatureEvaluatorTests` paket tablosu.
- Entegrasyon: `AutomatedReminderRulesProcessorTests` (kural başına gönderim, tekrar engeli, tercih, paket, kanal, birikmiş kayıt, ticari ileti kapısı), `ClinicReminderRulesEndpointTests`.

## 7) Bilinen sınırlar

- `NoReturn` gönderim yolu merkezi anahtar kapalıyken yalnız test üzerinden (`ProcessNoReturnRuleAsync`) çalıştırıldı.
- E-postada tek tıkla çıkış bağlantısı yoktur; çıkış klinik ya da portal tercihiyle yapılır (mevcut randevu ve aşı iletileriyle aynı).
- Aynı sahibin iki hayvanı ayrı `FollowUp`/`NoReturn` iletisi alabilir (olay başına).
