# Otomatik hatırlatıcılar API sözleşmesi — TIER-003

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-003`, ADR-035 (2026-11-06 paket yönü). İlgili: `PLAN_FEATURES_API_CONTRACT.md`, `PORTAL_API_CONTRACT.md` §10 ve §13 (bildirim türü × kanal), `TRIAL_LIMITS_API_CONTRACT.md` K2 (deneme e-posta bütçesi).

> **Durum:** Uygulandı (kod + test), dal `feat/tier-003-otomatik-hatirlatici`. Migration **`AddClinicReminderRules`** (yalnız yerel DB). Yeni izin yok. §5'teki maddeler 2026-11-08'de kullanıcı kararı oldu. §2 ticari ileti kapısı ADR-046 ile değişti (dal `feat/kararlar-2026-11-08`, migration **`AddMarketingGateAndOptOutLinks`**, yeni izin yok).

---

## 1) Kapsam

Klinik başına üç kural. Paket: Profesyonel ve Kurumsal (`PlanFeature.AutomatedReminders`). Kanal yalnız e-posta. Bugünkü randevu ve aşı e-posta hatırlatması (`/reminders/settings`) bu işin dışındadır, her pakette aynen kalır.

| `ruleType` | Ne zaman | N anlamı | Varsayılan N | Sınır | Kategori |
|---|---|---|---|---|---|
| `FollowUp` | Tedavideki kontrol tarihi (`Treatment.FollowUpDateUtc`) önümüzdeki N gün içinde | Kaç gün önce | 3 | 1–30 | Hatırlatma |
| `Debt` | Ödenmemiş en eski ücretin üzerinden N gün geçti | Kaç gün sonra | 30 | 1–365 | Hatırlatma |
| `NoReturn` | Hayvanın son gelişinin üzerinden N gün geçti, planlı randevusu yok | Kaç gün sonra | 180 | 30–730 | **Ticari ileti** |

Kuralların ve sınırların tek yeri `ClinicReminderRuleDefinition`; kategori tek yeri `OwnerNotificationChannelPolicy` (`FollowUpReminder`, `DebtReminder`, `NoReturnReminder`). Bu türler `GET /portal-notification-rules` matrisinde de görünür; push ve WhatsApp `Disabled`.

## 2) Ticari ileti (ADR-046, kullanıcı kararı 2026-11-08)

`NoReturn` hizmeti tanıtıp geri çağırdığı için ticari elektronik ileti sayılır (6563 sayılı Kanun). Genel `MarketingSendingEnabled` sabiti kaldırıldı; yerine **klinik başına kapı** geldi (`ClinicMarketingSettings`, kayıt yoksa kapalı).

| Konu | Kural |
|---|---|
| Kapı | Varsayılan kapalı. Klinik yöneticisi açarken beyanı onaylar: "Kliniğimiz İYS'ye kayıtlıdır; onay ve retleri İYS'ye işlemek kliniğin yükümlülüğüdür." Beyan kimin (`declaredByUserId`), ne zaman (`declaredAtUtc`) ve hangi metin sürümüyle (`declarationVersion`) verdiğiyle tutulur; her açılış beyanı yeniler. Açma ve kapatma denetim kaydına yazılır (`Reminders.MarketingGate.Update`). Yetki `Reminders.Manage`; paket aranmaz |
| Kapı kapalıyken | Kural açılabilir; aday aranmaz, e-posta gitmez, günlük yazılmaz. `sendingAvailable: false` |
| Alıcı | Yalnız bu klinikte aktif portal bağında ticari ileti onayı olan (`OwnerAccountClientLink.MarketingConsentAtUtc`) sahip. Onayı olmayan sahibe hiçbir zaman gitmez |
| Konu satırı | `Tanıtım: {klinik} sizi bekliyor` (nitelik ibaresi, İYS araştırması A5 madde 4) |
| Metin | Kliniğin adı, varsa telefonu; "Bu ticari elektronik ileti, onay vermeniz üzerine {klinik} tarafından gönderilmiştir." ve ret bağlantısı. Tek yer `NoReturnMarketingEmail` |
| Ret bağlantısı | İleti başına `MarketingOptOutLinks` kaydı; ham token yalnız e-postada, depoda SHA-256 özeti (TIER-019 deseni). Tek kullanımlık değil, süresi dolmaz. Adres `{PortalBaseUrl}/ticari-ileti-ret?token=…`; sayfayı açmak durumu değiştirmez, ret POST ile yapılır |
| Ret | Bağdaki onay geri alınır, ret zamanı `MarketingOptOutAtUtc`'ye yazılır (portaldan geri çekmede de). Denetim `Portal.MarketingOptOut` (aktör hasta sahibi, token yazılmaz). Kiracı salt okunur, bağ kapalı ya da kapı kapalı olsa da ret çalışır |
| Hatırlatmalar | `FollowUp`, `Debt`, randevu ve aşı hatırlatmaları kapıdan bağımsızdır, tanıtım içermez |

**Çalışma varsayımları:** kimlikte yalnız klinik adı ve telefonu (MERSİS/VKN alanı yok; hukukçuya soruldu, HUKUKI-ONAY-LISTESI); ret kanalı ayrıca tutulmaz (yalnız zaman); onayın kanal, kaynak ve metin sürümü modeli genişletilmedi; İYS'ye yazma yok, CSV dışa aktarma sonraki iş. Hukukçu onayına kadar canlıda kapı açılmamalıdır (ADR-046 Açık).

### Uçlar

- `PUT /api/v1/reminders/clinics/{clinicId}/marketing-gate` — `Reminders.Manage`. Gövde `{ "isEnabled": true, "iysDeclarationAccepted": true }`. Yanıt `ClinicMarketingGateDto`:

```json
{ "isEnabled": true, "declarationText": "Kliniğimiz İYS'ye kayıtlıdır; onay ve retleri İYS'ye işlemek kliniğin yükümlülüğüdür.",
  "declarationVersion": "2026-11-08", "declaredAtUtc": "…", "declaredByUserId": "…", "updatedAtUtc": "…" }
```

| Kod | HTTP | Durum |
|---|---|---|
| `Reminders.MarketingGate.Validation.DeclarationRequired` | 400 | `isEnabled: true` ve beyan onaylanmadı |
| `Clinics.NotFound` / `Clinics.AccessDenied` | 404 / 403 | Kural uçlarıyla aynı |

- `POST /api/portal/v1/marketing-opt-out/preview` (kimliksiz, `portal-auth` hız sınırı) — gövde `{ "token": "…" }` → `{ "clinicName": "Merkez", "isOptedOut": false }`. Durumu değiştirmez.
- `POST /api/portal/v1/marketing-opt-out/confirm` — aynı gövde → 204; tekrar çağrı da 204. Bilinmeyen token 404 `MarketingOptOut.NotFound`. GET yoktur (405).
## 3) Uçlar

### `GET /api/v1/reminders/clinics/{clinicId}/rules` — `Reminders.Read`

Paketten bağımsız okunur. Kayıt yoksa kural kapalı ve varsayılan gündedir. `sendingAvailable` ticari kuralda kliniğin ticari ileti kapısını yansıtır (§2); `marketingGate` kapının durumudur.

```json
{ "clinicId": "…",
  "rules": [
    { "ruleType": "FollowUp", "isEnabled": false, "days": 3, "defaultDays": 3, "minDays": 1, "maxDays": 30, "sendingAvailable": true, "updatedAtUtc": null },
    { "ruleType": "Debt", "isEnabled": false, "days": 30, "defaultDays": 30, "minDays": 1, "maxDays": 365, "sendingAvailable": true, "updatedAtUtc": null },
    { "ruleType": "NoReturn", "isEnabled": false, "days": 180, "defaultDays": 180, "minDays": 30, "maxDays": 730, "sendingAvailable": false, "updatedAtUtc": null }
  ],
  "marketingGate": { "isEnabled": false, "declarationText": "…", "declarationVersion": "2026-11-08", "declaredAtUtc": null, "declaredByUserId": null, "updatedAtUtc": null } }
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

## 5) Çalışma varsayımları (2026-11-08 kullanıcı kararıyla kabul)

1. Tüm kurallar varsayılan kapalı; klinik açınca çalışır.
2. Açınca geçmiş kayıtlara toplu ileti gitmez (`EnabledAtUtc`).
3. Borç iletisinde tutar yazılmaz.
4. `FollowUp` ve `Debt` ticari ileti sayılmaz, `NoReturn` sayılır (§2). `NoReturn` yalnız kliniğin ticari ileti kapısı açıkken ve onaylı sahibe gider (2026-11-08, ADR-046).
5. Kural listesi ve varsayılan N değerleri ajan önerisidir.

## 6) Testler

- Domain: `ClinicReminderRuleTests`, `Channel_Policy_Matrix` yeni satırları.
- Application: `TenantPlanFeatureEvaluatorTests` paket tablosu.
- Entegrasyon: `AutomatedReminderRulesProcessorTests` (kural başına gönderim, tekrar engeli, tercih, paket, kanal, birikmiş kayıt; kapalı klinik kapısında `NoReturn` gitmez, hatırlatma kapıdan bağımsız, açık kapıda yalnız onaylı sahibe ticari konu satırı, klinik adı ve ret bağlantısıyla tek ileti, retten sonra gitmez), `ClinicReminderRulesEndpointTests` (kapı beyan ister, açma/kapatma denetimi, yetki), `OwnerPortalIntegrationTests` ret bağlantısı (GET ve önizleme durumu değiştirmez, POST onayı kaldırır ve ret zamanını yazar, tekrar ret, bilinmeyen token).

## 7) Bilinen sınırlar

- Hatırlatma e-postalarında tek tıkla çıkış bağlantısı yoktur; çıkış klinik ya da portal tercihiyle yapılır. Ticari iletide ret bağlantısı vardır (§2).
- Aynı sahibin iki hayvanı ayrı `FollowUp`/`NoReturn` iletisi alabilir (olay başına).
