# Paket özellikleri API sözleşmesi — TIER-001

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-001`, ADR-035 (paket tablosu), ADR-045 K13 (klinik web sitesi tüm paketlerde), `TRIAL_LIMITS_API_CONTRACT.md` K7. İlgili: `SUBSCRIPTION_PLAN_CATALOG_API_CONTRACT.md` (fiyat ve kullanıcı sınırı), `PORTAL_API_CONTRACT.md`, `ONLINE_APPOINTMENT_API_CONTRACT.md`.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-001-paket-ozellik`. Migration yok, yeni izin yok, yeni uç yok. §4 **çalışma varsayımıdır, kullanıcı onayı bekliyor.**

---

## 1) Kural

| Özellik (`feature`) | Basic | Pro | Premium |
|---|---|---|---|
| `OwnerPortal` | yok | var | var |
| `PortalAppointmentRequests` | yok | var | var |
| `MultiClinic` | yok | yok | var |

| Konu | Kural |
|---|---|
| Tablonun yeri | Yalnız `SubscriptionPlanCatalog` (`PlanEntry.Features`). Yeni üst paket işi `PlanFeature` ile bu tabloya eklenir. |
| Kiracı için karar | Yalnız `TenantPlanFeatureEvaluator`. Abonelik kaydı olmayan kiracı kilitli sayılır. |
| Deneme | Seçilen pakete göre davranır (`OwnerPortal` Basic denemede kilitli); denemede paket ücretsiz değişir. **Tek istisna `MultiClinic`:** denemede şube sayısı sınırlanmaz (K7). |
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

`isEnabled` §2 ile aynı kuraldır (denemede `MultiClinic` açık). Pasif kiracıda `planCode: null`, `planFeatures: []`.

## 4) Paket düşürme — çalışma varsayımı, kullanıcı onayı bekliyor

**Seçilen (B):** düşürme her zaman kabul edilir, hiçbir veri silinmez, yalnız yenisi eklenemez. Kod tarafında düşürmeye yeni kontrol eklenmedi (`ScheduleSubscriptionDowngradeCommandHandler`, denemede `ApplyTrialPlanChangeAsync`); WEB-007 testleri aynen geçerlidir.

| Durum | Davranış |
|---|---|
| Fazla klinik | Aktif kalır, çalışır. Yeni klinik açılamaz; pasife alınan, başka aktif klinik varken geri açılamaz |
| Fazla üye | Kalır; yeni davet `Subscriptions.UserLimitExceeded` |
| Mevcut portal bağı | Açık kalır: okuma, veri talebi, tercihler, hesap silme ve **bağ üzerinden portaldan randevu talebi** çalışır |
| Bekleyen portal daveti | Kabul edilemez (yeni bağ açar), 404. Panelden iptal edilebilir |
| Yeni portal daveti | 403 |
| Klinik portal ayarı, davet iptali, bağ kapatma, gelen kutusu | Açık; klinik mevcut erişimi yönetebilir |

`PortalAppointmentRequests` için bu yüzden ayrı kapı yoktur: bağı olmayan kiracıda bağ kapsamlı uçlar zaten 404, düşen kiracıda mevcut bağ açık kalır. Satır `access-state` ve paket tablosu içindir.

**Bilinen risk:** bir ay üst paket alıp şube açarak ya da portal daveti göndererek düşmek, bunları alt paket fiyatına tutar.

**Reddedilen seçenek (A), onaylanırsa değişecek yerler:**
1. `ScheduleSubscriptionDowngradeCommandHandler` ve `StartSubscriptionCheckoutCommandHandler.ApplyTrialPlanChangeAsync`: aktif klinik ve üye sayısı hedef pakete sığmıyorsa red (`TenantPlanFeatureEvaluator` üzerinden tek kural).
2. `OwnerPortalAccess.ResolveLinkAsync`: pakette `OwnerPortal` yoksa bağ kapsamlı okuma uçları kapanır; veri talebi, tercih ve hesap silme açık kalır.
3. `TenantInviteUserLimitIntegrationTests.ScheduleDowngrade_Should_Not_Check_Member_Count` ters çevrilir.

## 5) Testler

- Application: `TenantPlanFeatureEvaluatorTests` (paket tablosu, gereken paket, kayıtsız kiracı, ilk ve ikinci klinik, deneme), `CreateClinicCommandHandlerTests`, `ClinicSettingsHandlersTests`, `CreateClientPortalInviteCommandHandlerTests`.
- Entegrasyon: `OwnerPortalPlanFeatureIntegrationTests` (Temel red, deneme dahil; Profesyonel davet ve kabul; düşürmede mevcut bağ, bekleyen davet ve fazla klinik; Kurumsal ve denemede ikinci klinik; `access-state`).

## 6) Bilinen sınırlar

- Klinik sayısı kontrolü kullanıcı sınırıyla aynı şekilde kilitsizdir; aynı anda iki istek ikinci kliniği açabilir.
- `TRIAL_LIMITS_API_CONTRACT.md` K7 metnindeki "`Basic` = 1" kullanıcı sınırı eskidir (katalog 3); bu işte değiştirilmedi.
