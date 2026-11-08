# Paket özellikleri API sözleşmesi — TIER-001

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-001`, ADR-035 (paket tablosu), ADR-045 K13 (klinik web sitesi tüm paketlerde), `TRIAL_LIMITS_API_CONTRACT.md` K7. İlgili: `SUBSCRIPTION_PLAN_CATALOG_API_CONTRACT.md` (fiyat ve kullanıcı sınırı), `PORTAL_API_CONTRACT.md`, `ONLINE_APPOINTMENT_API_CONTRACT.md`.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-001-paket-ozellik`. Migration yok, yeni izin yok, yeni uç yok. §4 2026-11-08 kullanıcı kararıyla seçenek A'ya geçti (dal `feat/kararlar-2026-11-08`); dönem sonu davranışı çalışma varsayımıdır.

---

## 1) Kural

| Özellik (`feature`) | Basic | Pro | Premium |
|---|---|---|---|
| `OwnerPortal` | yok | var | var |
| `PortalAppointmentRequests` | yok | var | var |
| `MultiClinic` | yok | yok | var |
| `Waitlist` (TIER-018, `WAITLIST_API_CONTRACT.md`) | yok | var | var |

| Konu | Kural |
|---|---|
| Tablonun yeri | Yalnız `SubscriptionPlanCatalog` (`PlanEntry.Features`). Yeni üst paket işi `PlanFeature` ile bu tabloya eklenir. |
| Kiracı için karar | Yalnız `TenantPlanFeatureEvaluator`. Abonelik kaydı olmayan kiracı kilitli sayılır. |
| Deneme | Seçilen pakete göre davranır, `MultiClinic` dahil (2026-11-08 kullanıcı kararı; K7 istisnası kalktı): Temel ve Profesyonel denemede ikinci aktif klinik açılamaz, Kurumsal denemede açılır. Denemede paket ücretsiz değişir; değişim sığma kuralına bağlıdır (§4). |
| Abonelik durumu | Paket kuralı durumdan bağımsızdır; salt okunur kiracıda yazma kapısı (`Subscriptions.TenantReadOnly`) ayrıca çalışır. |
| Kilitlenmeyenler | Yasal kayıt defterleri, KVKK araçları (veri talebi gelen kutusu dahil), klinik web sitesi (ADR-045 K13), kliniğe özel kimliksiz online randevu bağlantısı (ADR-042). Bu tablonun dışındadır. |
| Site metni | `pricing-plan.utils.ts` `PRICING_FEATURE_ROWS` pazarlama metnidir; tablo değişirse elle eşitlenir. |

## 2) Hata: `Subscriptions.FeatureNotInPlan` (403)

Standart `ProblemDetails`, `extensions` alanları:

```json
{ "status": 403, "code": "Subscriptions.FeatureNotInPlan",
  "detail": "Bu özellik mevcut paketinizde yok; üst pakete geçerek kullanabilirsiniz.",
  "feature": "OwnerPortal", "currentPlanCode": "Basic", "requiredPlanCode": "Pro" }
```

`currentPlanCode` abonelik kaydı yoksa `null`. `requiredPlanCode` özelliği içeren en küçük pakettir.

| Uç | Koşul | Yanıt |
|---|---|---|
| `POST /api/v1/clients/{clientId}/portal-invites` | Pakette `OwnerPortal` yok | 403 `Subscriptions.FeatureNotInPlan`, davet ve e-posta oluşmaz |
| `GET /api/v1/clients/{clientId}/whatsapp-link?kind=PortalInvite` | Pakette `OwnerPortal` yok | 403 aynı kod |
| `GET /api/v1/clients/{clientId}/portal-access` | Pakette `OwnerPortal` yok | 200, `canInvite: false`, `inviteBlockedCode: "Subscriptions.FeatureNotInPlan"` (diğer engellerden önce); bağ ve davet listeleri aynen döner |
| `GET /api/portal/v1/invites/{token}`, `POST …/accept` | Pakette `OwnerPortal` yok | 404 `Portal.Invites.NotFound` (sebep hasta sahibine ayırt edilmez; ticari bilgi portala çıkmaz) |
| `POST /api/v1/clinics` | `MultiClinic` yok ve aktif klinik var | 403 aynı kod, `requiredPlanCode: "Premium"` |
| `POST /api/v1/clinics/{id}/activate` | `MultiClinic` yok, klinik pasif ve başka aktif klinik var | 403 aynı kod. Zaten aktif klinikte idempotent yanıt değişmez |

Kayıt (`owner-signup`) ilk kliniği her pakette oluşturur. Pasif klinik sayılmaz: tek aktif klinik pasife alınınca yenisi açılabilir.

## 3) `GET /api/v1/tenants/{tenantId}/access-state` (değişti, geriye uyumlu)

İzin gerektirmez; panel kilit ekranı buradan okur. İki yeni alan:

```json
{ "tenantId": "…", "isReadOnly": false, "reasonCode": null, "message": null,
  "planCode": "Pro",
  "planFeatures": [
    { "feature": "OwnerPortal", "isEnabled": true, "requiredPlanCode": "Pro" },
    { "feature": "PortalAppointmentRequests", "isEnabled": true, "requiredPlanCode": "Pro" },
    { "feature": "MultiClinic", "isEnabled": false, "requiredPlanCode": "Premium" }
  ] }
```

`isEnabled` §2 ile aynı kuraldır (deneme dahil yalnız paket belirler). Pasif kiracıda `planCode: null`, `planFeatures: []`.

## 4) Paket değişimi — seçenek A (kullanıcı kararı, ADR-035 2026-11-08)

Önceki çalışma varsayımı (B: düşürme her zaman kabul, fazla klinik ve üye kalır, mevcut portal bağı tam açık) kaldırıldı. Kural tek yerde: `TenantPlanFeatureEvaluator.GetPlanFitAsync` / `EnsureFitsPlanAsync`.

**Sığma kuralı:** hedef paketin aktif klinik sınırı (`MultiClinic` yoksa 1, varsa sınırsız) ve kullanıcı sınırı (`SubscriptionPlanCatalog.MaxUsers`). Kullanıcı sayımı davet kuralıyla aynıdır: üye + süresi dolmamış bekleyen davet (`TenantSubscriptionSeatEvaluator`). Pasif klinik sayılmaz. Hiçbir veri silinmez.

| Nerede | Davranış |
|---|---|
| `POST /api/v1/tenants/{id}/subscription-plan-change/downgrade` (`ScheduleSubscriptionDowngradeCommandHandler`) | Sığmıyorsa 409 `Subscriptions.PlanLimitsExceeded`, kayıt oluşmaz, açık eski kayıt iptal edilmez |
| `POST …/subscription-checkout` (`StartSubscriptionCheckoutCommandHandler`): denemede paket değişimi (`ApplyTrialPlanChangeAsync`), ücretli yükseltme, aylıktan yıllığa, süresi dolmuş abonelikte yenileme | Aynı kontrol, aynı 409; oturum açılmaz, plan değişmez |
| Dönem sonu (`ScheduledPlanChangeProcessorHostedService.ProcessDueOnceAsync`) | **Çalışma varsayımı:** planlandıktan sonra sınır aşıldıysa düşürme uygulanmaz, kayıt `Rejected` (4) olur, abonelik mevcut pakette kalır. Panel nedeni abonelik özetinden okur (aşağıda). E-posta gönderilmez: abonelik olaylarında kiracıya e-posta deseni yok. Sığarsa **yalnız paket değişir** (`TenantSubscription.ApplyScheduledDowngrade`): durum, dönem türü ve `ActivatedAtUtc` değişmez, ödemesiz yeni dönem açılmaz. Otomatik yenileme yok (BILL-001): dönem bittiği için abonelik salt okunurdur, kiracı yeni paketle yenileme ödemesi başlatır. Denemede uygulanan düşürme denemeyi ücretli döneme çevirmez |

Hata gövdesi (standart `ProblemDetails`, alanlar üst düzeyde):

```json
{ "status": 409, "code": "Subscriptions.PlanLimitsExceeded",
  "detail": "Seçilen paket mevcut kullanımınıza sığmıyor; önce fazla klinikleri pasife alın ya da kullanıcıları çıkarın.",
  "targetPlanCode": "Basic", "maxActiveClinics": 1, "maxUsers": 3,
  "activeClinicCount": 2, "memberCount": 5, "pendingInviteCount": 0,
  "excessClinicCount": 1, "excessUserCount": 2 }
```

`maxActiveClinics: null` sınırsızdır. `excessUserCount = max(0, memberCount + pendingInviteCount − maxUsers)`; bekleyen davet iptali de sayıyı düşürür. Panel düz Türkçe cümleyi bu alanlardan kurar (paket adı panelin metnidir).

`GET /api/v1/tenants/{id}/subscription-summary` (geriye uyumlu ek): açık planlı değişiklik yoksa ve en son kayıt `Rejected` ise `rejectedPlanChange` döner; içinde `fit` (yukarıdaki alanlar, **bugünkü** sayılarla). Yeni düşürme planlanınca alan kaybolur. `pendingPlanChange` değişmedi (yalnız `Scheduled`). Ret zamanı ayrıca tutulmaz; `effectiveAtUtc` uygulanma denemesinin zamanıdır (çalışma varsayımı, migration yok).

| Durum (portalsız pakete inince) | Davranış |
|---|---|
| Mevcut portal bağı | Okuma ve hizmet uçları kapanır (404 `Portal.Links.NotFound`, sebep ayırt edilmez); veri talebi, bildirim ve hatırlatma tercihleri, aydınlatma metni ve hesap silme açık. `GET /me` bağında `isPortalServiceAvailable: false`. Ayrıntı `PORTAL_API_CONTRACT.md` §12.1 |
| Bekleyen portal daveti | Kabul edilemez, 404. Panelden iptal edilebilir |
| Yeni portal daveti | 403 `Subscriptions.FeatureNotInPlan` |
| Klinik portal ayarı, davet iptali, bağ kapatma, gelen kutusu | Açık; klinik mevcut erişimi yönetebilir |
| Önceden (B döneminde) sınırı aşmış kiracı | Klinik ve üyeler silinmez; yeni klinik ve davet reddedilir, bir sonraki paket değişiminde sığma kuralı uygulanır |

`PortalAppointmentRequests` için ayrı kapı yoktur: bağ üzerinden talep `OwnerPortal` kapısıyla kapanır. Satır `access-state` ve paket tablosu içindir.

**Kapanan risk:** üst paket alıp şube açarak ya da portal daveti göndererek düşmek artık mümkün değil.
## 5) Testler

- Application: `TenantPlanFeatureEvaluatorTests` (paket tablosu, gereken paket, kayıtsız kiracı, ilk ve ikinci klinik, deneme, sığma kuralı ve bekleyen davet), `StartSubscriptionCheckoutCommandHandlerTests` (denemede değişimde ve yenilemede sığma), `CreateClinicCommandHandlerTests`, `ClinicSettingsHandlersTests`, `CreateClientPortalInviteCommandHandlerTests`.
- Entegrasyon: `OwnerPortalPlanFeatureIntegrationTests` (Temel red, deneme dahil; Profesyonel davet ve kabul; portalsız pakette okuma kapalı, veri talebi, tercihler ve hesap silme açık, yükseltince geri gelir; bekleyen davet ve fazla klinik; Temel ve Profesyonel denemede ikinci klinik reddi, Kurumsal denemede kabul; denemede eski iki klinik kalır; `access-state`), `TenantInviteUserLimitIntegrationTests` (sığmayan düşürmede 409 ve sayılar, bekleyen davet ve fazla klinik, sığan düşürmede kabul, dönem sonu ret ve uygulama, dönem sonunda ödemesiz dönem açılmaz ve abonelik salt okunur kalır, denemede düşürme denemeyi korur, özetteki `rejectedPlanChange`).

## 6) Bilinen sınırlar

- Klinik sayısı kontrolü kullanıcı sınırıyla aynı şekilde kilitsizdir; aynı anda iki istek ikinci kliniği açabilir.
- Sığma kontrolü de kilitsizdir: düşürme planlanırken ve uygulanırken eşzamanlı klinik açma ya da davet sınırı aşabilir; dönem sonu yeniden kontrolü planlanmış düşürmeyi bu yüzden korur.
