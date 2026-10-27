# Foundation kuralları

Bu belge **Backend.Veteriner** deposunun güncel mimari ve süreç kurallarını tanımlar. Ürün modülleri bu kurallara uyarak foundation üzerine eklenir.

---

## 1. Katman bağımlılık yönü

| Katman | Bağımlılık |
|--------|------------|
| **Backend.Veteriner.Api** | Application, Infrastructure, Domain (doğrudan veya dolaylı) |
| **Backend.Veteriner.Application** | **Domain** ve gerekli .NET / paket soyutlamaları; EF Core yalnız aşağıdaki sınırla |
| **Backend.Veteriner.Domain** | Dış uygulama / altyapı assembly’lerine **bağımlı olmaz** |
| **Backend.Veteriner.Infrastructure** | Application + Domain |

**Kural:** Domain’e EF, HTTP, SMTP, `IConfiguration` vb. sızması yasaktır. Uygulama arabirimleri (`IRepository`, `IOutbox`, `IEmailSender` …) Application’da tanımlanır; implementasyon Infrastructure’da kalır.

**Application’da EF Core sınırı (karar 2026-10-27):** Application EF Core paketine bağlıdır ve bu bilinçlidir. İzin verilen kullanım:

- Specification ve sorgu ifadeleri: `AsNoTracking`, `Include`, `EF.Functions.Like` / `Collate`.
- EF’nin kayıt istisnalarını iş sonucuna çevirmek: `DbUpdateException`, `DbUpdateConcurrencyException`.

Yasak: `DbContext`, `DbSet`, migration, model yapılandırması ve bağlantı/transaction yönetimi. Bunlar Infrastructure’da kalır; Application veriye repository, specification ve okuma arabirimleri üzerinden erişir.

---

## 2. Controller kuralları

- Controller’lar **ince** kalır: doğrulama ve iş kuralı **command/query handler**’larda.
- Mediator ile `Send`; yanıt dönüşümü projede kullanılan **Result → HTTP** yardımcıları ile yapılır.
- Yetkisiz uçlar `[AllowAnonymous]` ile açıkça işaretlenir; admin/platform uçları **permission policy** ile korunur.
- API **sürümleme** ve route kuralları mevcut Api projesindeki standarda uyulur (kırıcı değişiklik yapılmadan genişletilir).

---

## 3. Command / query / validator klasör standardı

Önerilen yapı (örnek):

```text
{Feature}/
  Commands/
    {UseCase}/
      {Name}Command.cs
      {Name}CommandHandler.cs
      {Name}CommandValidator.cs   # FluentValidation
  Queries/
    {UseCase}/
      {Name}Query.cs
      {Name}QueryHandler.cs
```

**Kurallar:**

- Bir dosyada bir ana public type. Ana tiple birlikte anlamlı olan küçük yardımcı tipler aynı dosyada kalabilir: komutun sonuç/DTO kaydı, aynı komutun handler’ı, yalnız o tipin kullandığı enum veya iç kayıt. Bağımsız kullanılan ya da büyüyen tip kendi dosyasına taşınır (karar 2026-10-27).
- Validator’lar **Application** katmanında; AbstractValidator ile command/query’ye bağlanır.
- Handler’lar `IRequestHandler<,>` implemente eder; yan etkiler için repository / unit of work / outbox arabirimleri kullanılır.

---

## 4. Result kullanımı

- Uygulama katmanı iş sonuçlarını mümkün olduğunca **`Result` / `Result<T>`** ile ifade eder.
- Başarısızlıklar için anlamlı **hata kodu / mesaj** (ve gerekiyorsa metadata) kullanılır; controller’da exception’a düşmeden HTTP eşlemesi yapılır.
- “Bulunamadı”, “çakışma”, “yetkisiz” gibi durumlar **iş kuralı** ile `Result` üzerinden modellenir (gereksiz exception akışı kaçınılır).

---

## 5. PermissionCatalog kuralı

- Tüm sabit izin anahtarları **`PermissionCatalog`** (veya eşdeğer tek kaynak) üzerinden tanımlanır.
- Yeni uç veya use-case için izin ekleniyorsa:
  - Katalog güncellenir,
  - Seed (ör. `PermissionSeeder`) ile veritabanına yansıtılır,
  - Controller / policy string’leri katalog ile **aynı kaynaktan** türetilir (magic string dağınıklığı önlenir).

---

## 6. Seeder ve migration kuralı

- **Migration:** Şema değişiklikleri yalnızca EF migration ile; `EnsureCreated` üretim yolunda kullanılmaz.
- **DataSeeder / PermissionSeeder:** Yalnızca **veri tohumlama**; migration uygulama sorumluluğu uygulama başlangıç orchestration’ında (ör. `Migrate` / deploy pipeline) kalır.
- Yeni izin veya zorunlu başlangıç verisi: seed kodu + gerekirse migration (şema) birlikte planlanır.

---

## 7. Integration test zorunlulukları

- Kritik platform akışları (auth, izin, outbox, audit/correlation davranışı) için **entegrasyon testi** tercih edilir.
- Test ortamı:
  - Veteriner projesine özgü connection string / veritabanı adları kullanılır (ör. `VetinityCommandDb_IntegrationTests_<koşu kimliği>`, `VetinityQueryDb_IntegrationTests_<koşu kimliği>`; yük testi: `VetinityCommandDb_LoadTest`, `VetinityQueryDb_LoadTest`),
  - Şema için **`Migrate`** (veya eşdeğer tutarlı yol) ile test veritabanı güncel tutulur.
- Factory / seed: testler birbirini bozmaması için izole DB veya deterministik seed stratejisi kullanır.

---

## 8. Audit, correlation, outbox yaklaşımı

- **Correlation:** HTTP isteği boyunca correlation id üretimi / iletimi middleware ve header standardı ile yapılır.
- **Audit:** `IAuditableRequest` işaretli command’ler (ve pipeline) denetim kaydı üretir; audit writer altyapıda uygulanır.
- **Transaction:** Repository yazımları (`AddAsync` / `UpdateAsync` / `DeleteAsync`) anında commit eder. Birden fazla yazım yapan veya iç komut gönderen command `ITransactionalRollbackOnFailureRequest` ile işaretlenir; `TransactionBehavior` tek işlem açar, istisnada ve başarısız `Result`’ta geri alır. Tek yazımlı (ekle-ya-da-güncelle dallı) command’ler işaret gerektirmez.
- **Outbox:** Yan etkiler (e-posta vb.) mümkün olduğunda transactional outbox ile kuyruklanır; işleyici hosted service veya ayrı worker ile tüketilir.

Bu dördü **platform tutarlılığı** için foundation seviyesinde korunur; ürün modülleri aynı pattern’e uyum sağlar.

---

## 9. Domain event outbox — neden şu an park edildi?

Otomatik domain event → outbox köprüsü **bilinçli olarak yok**. Domain'de yalnız işaretleyici kaldı: `IDomainEvent` (dış pakete bağımlı değil, Mediator bildirimi türetmez), `DomainEvent` temel kaydı ve `AggregateRoot.AddDomainEvent`. Kayıtlı ama DbContext'e hiç bağlanmamış `DomainEventOutboxInterceptor`, `DomainEventTypeRegistry`, `OutboxProcessor`'daki domain event dalı ve tek üretici `UserCreatedDomainEvent` 2026-10-27'de ölü kod olarak kaldırıldı; `Mediator.Abstractions` paketi Domain'den düştü.

**Gerekçe (özet):**

1. **İlk use-case öncesi** tip kaydı, serileştirme sözleşmesi (`$type`, assembly adı, versioning) ve worker tarafı tüketimi netleştirilmelidir.
2. Foundation’da henüz **zorunlu, üretim kritik** bir domain event yayını yoksa interceptor’ı açmak yanlış pozitif outbox mesajları veya kırılgan deserialize yolları doğurabilir.
3. Etkinleştirme; **ilk gerçek domain event** ihtiyacıyla birlikte test edilerek yapılmalıdır (tip kaydı, outbox payload, idempotency). Mediator bildirimi gerekirse `IDomainEvent`'i Application tarafında sarmalayın; Domain pakete bağlanmaz.

Park kaldırıldığında bu bölüm güncellenmeli ve checklist’e “domain event + outbox” adımları eklenmelidir.

---

## 10. Özet: yapılmaması gerekenler

- Domain’e altyapı referansı eklemek.
- İzin string’lerini controller’da katalog dışı bırakmak.
- Şema için raw SQL ile “sessiz” şema değiştirmek (migration dışı).
- Production secret’larını repoya yazmak.

---

*Bu belge Backend.Veteriner **çekirdeğinin** mevcut sınırlarıyla uyumludur. Repoda bulunmayan veya henüz eklenmemiş ürün modülleri (ör. Contact, Organization, Tasks, veteriner iş domain’i) aktif parça olarak anlatılmaz.*
