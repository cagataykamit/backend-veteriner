# Auth Domain Event / Outbox Genişletme Analizi

**Tarih:** 2025-03-11  
**Kapsam:** Auth closure sonrası – domain event adayları, üretim yeri, outbox uyumu, yol haritası

---

## 1. Genel değerlendirme

- **Mevcut outbox altyapısı aggregate-odaklı ve tutarlı.** Sadece `AggregateRoot` türeyen ve EF change tracker’da olan entity’lerin `DomainEvents` listesi SaveChanges sırasında `DomainEventOutboxInterceptor` ile outbox’a yazılıyor. OutboxProcessor bu kayıtları deserialize edip MediatR ile publish ediyor; retry/dead-letter mevcut.
- **Kritik nokta:** Projede **hiçbir domain entity şu an `AggregateRoot`’tan türemiyor.** `User`, `Permission`, `UserOperationClaim`, `RefreshToken` hepsi sade entity. Bu yüzden **`UserCreatedDomainEvent` tanımlı olsa da hiçbir yerde raise edilmiyor** ve outbox’a hiç düşmüyor. Handler da sadece log atıyor; event akışı fiilen kapalı.
- **Öneri stratejisi:** Gerçekten değer üreten **tek yüksek öncelikli** event: **UserCreated** (onboarding, email verification, welcome flow). Bunun için User’ı aggregate yapıp event’i aggregate içinde üretmek yeterli. Diğer auth mutasyonları (session revoke, permission CRUD, user claim assign/remove, password reset, email verification) için **audit + mevcut handler reaksiyonları (cache invalidation, mail)** çoğu senaryoda yeterli; gereksiz event üretiminden kaçınılmalı. İleride somut tüketici (dış sistem, read model, analytics) çıkarsa event eklenebilir.

---

## 2. Mevcut altyapı değerlendirmesi

### AggregateRoot
- **Yeterlilik:** AddDomainEvent, ClearDomainEvents, DomainEvents koleksiyonu net. EF ile birlikte interceptor tarafından tüketiliyor.
- **Eksik:** Projede hiçbir entity `AggregateRoot` inherit etmiyor; bu yüzden mekanizma kullanılmıyor.

### DomainEventOutboxInterceptor
- **Konum:** SaveChanges öncesi (`SavingChangesAsync`); doğru nokta. Sadece `Entries<AggregateRoot>()` ile event taşıyan aggregate’leri alıyor.
- **Davranış:** Event’leri `OutboxMessage` olarak yazıyor (Type = event FullName, Payload = JSON). TraceId alınıyor; CorrelationId şu an null (isterseniz HttpContext’ten doldurulabilir).
- **Sonuç:** Altyapı auth event’leri için uygun; tek koşul event’in bir **AggregateRoot** üzerinden üretilmesi.

### OutboxProcessor
- **Davranış:** Type’a göre `DomainEventTypeRegistry` ile CLR tipini buluyor; deserialize edip `IMediator.Publish` ile dağıtıyor. Email gibi özel tipler ayrı branch’te.
- **Sonuç:** Operasyonel olarak yeterli; yeni domain event tipleri otomatik registry’ye girer (IDomainEvent implement eden sınıflar).

### DomainEventTypeRegistry
- **Davranış:** Assembly’lerdeki `IDomainEvent` implementasyonlarını FullName ile sözlüğe alıyor.
- **Yönetilebilirlik:** Yeni event sınıfı eklendiğinde ek kayıt gerekmiyor; refletion ile bulunuyor.

### Mevcut altyapının auth event’leri için uygunluğu
- **Evet.** Auth tarafında event’leri outbox’a taşımak için yeterli koşul: ilgili state değişikliği bir **aggregate** üzerinden yapılsın ve event o aggregate içinde **AddDomainEvent** ile eklensin. Şu an bu pattern sadece User için önerilir (UserCreated); diğerleri için audit + doğrudan reaksiyon tercih edilir.

---

## 3. Event aday tablosu

| Event adı | Eklenmeli mi | Event tipi | Kaynak | Kullanım amacı | Öncelik |
|-----------|--------------|------------|--------|-----------------|---------|
| **UserCreatedDomainEvent** | Evet | Domain | User (aggregate) | Onboarding, email verification tetikleme, welcome mail; audit zaten var, event async yan etki için. | Hemen |
| SessionRevokedDomainEvent | Hayır (şimdilik) | - | - | Session revoke audit + handler yeterli; async tüketici yok. | Eklenmemeli |
| AllSessionsRevokedDomainEvent | Hayır (şimdilik) | - | - | Aynı. | Eklenmemeli |
| PermissionCreatedDomainEvent | Hayır (şimdilik) | - | - | Cache invalidation handler’da; audit var. | Eklenmemeli |
| PermissionUpdatedDomainEvent | Hayır (şimdilik) | - | - | Aynı. | Eklenmemeli |
| PermissionDeletedDomainEvent | Hayır (şimdilik) | - | - | Aynı. | Eklenmemeli |
| UserOperationClaimAssignedDomainEvent | Hayır (şimdilik) | - | - | Cache invalidation handler’da; audit var. | Eklenmemeli |
| UserOperationClaimRemovedDomainEvent | Hayır (şimdilik) | - | - | Aynı. | Eklenmemeli |
| UserClaimAddedDomainEvent | Hayır (şimdilik) | - | - | Aynı. | Eklenmemeli |
| UserClaimRemovedDomainEvent | Hayır (şimdilik) | - | - | Aynı. | Eklenmemeli |
| PasswordResetRequestedDomainEvent | Hayır (şimdilik) | - | - | Mail handler’da; audit var. | Eklenmemeli |
| PasswordResetConfirmedDomainEvent | Hayır (şimdilik) | - | - | Aynı. | Eklenmemeli |
| EmailVerificationRequestedDomainEvent | Hayır (şimdilik) | - | - | Aynı. | Eklenmemeli |
| EmailVerificationConfirmedDomainEvent | Hayır (şimdilik) | - | - | Aynı. | Eklenmemeli |

**Özet:** Sadece **UserCreatedDomainEvent** şu an için “hemen eklenmeli”; kaynağı **User** aggregate’i olmalı. Diğerleri için audit ve mevcut handler reaksiyonları yeterli kabul edilir; ileride somut tüketici (entegrasyon, read model, raporlama) gelirse yeniden değerlendirilir.

---

## 4. Event üretim yeri analizi

### Kural
- **Domain event:** Aggregate’in kendi invariant’ı / yaşam döngüsü ile doğrudan ilgili, “bu aggregate’te şu oldu” bilgisini taşıyan olay. Üretim yeri **aggregate içi** (constructor, factory metot, state değiştiren metot). Outbox’a **sadece** bu şekilde üretilen event’ler mevcut interceptor ile gider.
- **Application / integration event:** “Sistem şu işlemi yaptı” bildirimi; aggregate sınırına bağlı değil. İstenirse handler sonunda ayrı bir mekanizma ile (ör. application outbox tablosu veya doğrudan mesaj kuyruğu) yazılabilir; **mevcut** altyapı buna göre tasarlanmamış (interceptor sadece AggregateRoot’lara bakıyor).

### Permission / UserOperationClaim / Session
- Bu mutasyonlar **application-level**: Repository üzerinden entity ekleme/çıkarma/güncelleme; **aggregate root yok**. Permission.Create handler doğrudan `new Permission(...)` + `AddAsync`; UserOperationClaim assign/remove da repository + UnitOfWork. Invariant çoğunlukla “aynı ilişki tekrar eklenmesin” gibi repository/application kurallarıyla korunuyor.
- **Sonuç:** Bunlar için “domain event” aggregate içinde raise edilemez; event istenirse “application event” olur ve mevcut outbox pipeline’ına girmek için ek bir yazım noktası (handler’dan OutboxMessage ekleme vb.) gerekir. Bu dokümanda öneri: **eklemeyin**; audit + handler reaksiyonu yeterli.

### User ve UserCreated
- Kullanıcı oluşturma, **User** aggregate’inin yaşam döngüsünde “doğdu” anına karşılık gelir. Event’in aggregate içinde üretilmesi DDD açısından doğru: **User** aggregate root yapılır, constructor’da (veya `Create` factory’de) `AddDomainEvent(new UserCreatedDomainEvent(Id, Email))` çağrılır. SaveChanges’ta interceptor event’i toplar ve outbox’a yazar; processor sonra MediatR ile publish eder. Handler’da welcome mail / email verification tetiklenebilir.

### Password reset / Email verification
- Bunlar “kullanıcı davranışı” veya “sistem işlemi”; genelde ayrı aggregate değil, handler + mail + audit ile çözülüyor. Event’e dönüştürmek için ya (a) bir aggregate (örn. User) üzerinde “password reset requested” gibi metot + event tanımlanır (modeli şişirir), ya (b) application event olarak handler’dan yazılır. Öneri: **şimdilik event eklenmesin**; audit yeterli.

---

## 5. Event tasarım standardı

### İsimlendirme
- Pattern: `{Aggregate veya kavram}{Olgu}DomainEvent`  
  Örnek: `UserCreatedDomainEvent`, ileride `PermissionCreatedDomainEvent` (eğer eklenirse).
- Namespace: `Backend.Veteriner.Domain.{Aggregate/Concept}.Events` veya `Backend.Veteriner.Domain.Shared.Events`.

### Payload
- Sadece **tanımlayıcılar ve iş kuralı için gerekli minimal veri**: Id, aggregate id, ilgili id’ler (UserId, PermissionId vb.).
- **Hassas veri yok:** Şifre, token, hash, e-posta gövdesi, PII (gerekmedikçe) event’te taşınmaz. UserCreated’da Email gerekebilir (welcome/verification için); şifre asla.

### Correlation / actor
- **Event payload’ında zorunlu değil.** Request correlation id ve actor bilgisi zaten HttpContext / ClientContext üzerinden audit’e yazılıyor. OutboxMessage’a CorrelationId interceptor’da (isteğe bağlı) eklenebilir; event kaydının kendisinde actor/correlation taşımak zorunlu olmasın.

### Taşınmaması gerekenler
- Password, password hash, token (refresh, reset, verification), secret, API key, tam PII (gerekmedikçe).

---

## 6. Önceliklendirilmiş yol haritası

### Hemen (Phase 1)
1. **User’ı AggregateRoot yap:** `User : AggregateRoot` (Domain).
2. **UserCreatedDomainEvent’i aggregate’te üret:** Constructor’da (veya `User.Create` factory) `AddDomainEvent(new UserCreatedDomainEvent(Id, Email))`. Şifre veya hash event’e eklenmez.
3. **Mevcut handler’ı koru:** OutboxProcessor event’i publish ettiğinde `UserCreatedDomainEventHandler` çalışacak; istenirse burada email verification isteği veya welcome mail tetiklenir (şu an sadece log da yeterli).

### Sonraki faz (Phase 2 – sadece ihtiyaç olursa)
4. **Session / permission / claim event’leri:** Sadece somut tüketici (dış sistem, analytics, read model) tanımlandığında değerlendirilir. Gerekirse application-level event + outbox’a yazım noktası ayrı tasarlanır (mevcut interceptor dışında).

### Şimdilik eklenmemeli
5. Password reset, email verification, permission CRUD, user claim assign/remove için **yeni event eklenmesin**; audit + mevcut handler reaksiyonları yeterli kabul edilir.

---

## 7. Örnek event ve akış (sadece UserCreated)

### User – AggregateRoot ve event üretimi

```csharp
// Backend.Veteriner.Domain/Users/User.cs
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users.Events;

public class User : AggregateRoot  // AggregateRoot'tan türet
{
    // ... mevcut alanlar ve constructor ...

    public User(string email, string passwordHash)
    {
        Email = email ?? throw new ArgumentNullException(nameof(email));
        PasswordHash = passwordHash ?? throw new ArgumentNullException(nameof(passwordHash));
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;

        AddDomainEvent(new UserCreatedDomainEvent(Id, Email));
    }
}
```

### Akış
1. `AdminCreateUserCommandHandler` → `new User(email, hash)` → User constructor’da `AddDomainEvent(UserCreatedDomainEvent)`.
2. Handler `AddAsync(user)` + `SaveChangesAsync()` → EF SaveChanges.
3. `DomainEventOutboxInterceptor.SavingChangesAsync` → ChangeTracker’da `User` (AggregateRoot) ve `DomainEvents.Count > 0` → event’ler OutboxMessage olarak eklenir → `ClearDomainEvents()`.
4. OutboxProcessor döngüsünde mesaj işlenir → `DomainEventTypeRegistry.Resolve("Backend.Veteriner.Domain.Users.Events.UserCreatedDomainEvent")` → deserialize → `IMediator.Publish(domainEvent)`.
5. `UserCreatedDomainEventHandler` çalışır (log; istenirse email verification / welcome mail).

### UserCreatedDomainEvent (mevcut – değişiklik yok)

```csharp
// Backend.Veteriner.Domain/Users/Events/UserCreatedDomainEvent.cs
public sealed record UserCreatedDomainEvent(
    Guid UserId,
    string Email
) : DomainEvent;
```

Payload’da sadece UserId ve Email; şifre/hash yok. Bu yapı mevcut altyapı ve gizlilik kurallarıyla uyumlu.

---

## 8. Kısa özet

- **Mevcut outbox:** Sadece **AggregateRoot** entity’lerden çıkan domain event’leri topluyor; bu yüzden event’in **aggregate içinde** raise edilmesi gerekiyor.
- **Tek önerilen genişletme:** **User** aggregate root yapılıp **UserCreatedDomainEvent** constructor’da üretilsin; böylece outbox’a yazılır ve async yan etkiler (onboarding, email verification) güvenli şekilde tetiklenir.
- **Diğer auth mutasyonları:** Event eklenmeden **audit + mevcut handler reaksiyonları** (cache invalidation, mail) ile bırakılması önerilir; gereksiz event ve karmaşıklık artışı önlenir. İleride net tüketici ihtiyacı olursa event ve gerekirse application-level outbox genişletmesi ayrıca planlanabilir.
