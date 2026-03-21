# Foundation kuralları

Bu belge **Backend.Veteriner** deposunun güncel mimari ve süreç kurallarını tanımlar. Ürün modülleri bu kurallara uyarak foundation üzerine eklenir.

---

## 1. Katman bağımlılık yönü

| Katman | Bağımlılık |
|--------|------------|
| **Backend.Veteriner.Api** | Application, Infrastructure, Domain (doğrudan veya dolaylı) |
| **Backend.Veteriner.Application** | Yalnızca **Domain** (ve gerekli .NET / paket soyutlamaları) |
| **Backend.Veteriner.Domain** | Dış uygulama / altyapı assembly’lerine **bağımlı olmaz** |
| **Backend.Veteriner.Infrastructure** | Application + Domain |

**Kural:** Domain’e EF, HTTP, SMTP, `IConfiguration` vb. sızması yasaktır. Uygulama arabirimleri (`IRepository`, `IOutbox`, `IEmailSender` …) Application’da tanımlanır; implementasyon Infrastructure’da kalır.

---

## 2. Controller kuralları

- Controller’lar **ince** kalır: doğrulama ve iş kuralı **command/query handler**’larda.
- MediatR ile `Send`; yanıt dönüşümü projede kullanılan **Result → HTTP** yardımcıları ile yapılır.
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

- Bir dosyada tek public type (okunabilirlik ve review kolaylığı).
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
  - Foundation’a özgü connection string / veritabanı adları kullanılır,
  - Şema için **`Migrate`** (veya eşdeğer tutarlı yol) ile test veritabanı güncel tutulur.
- Factory / seed: testler birbirini bozmaması için izole DB veya deterministik seed stratejisi kullanır.

---

## 8. Audit, correlation, outbox yaklaşımı

- **Correlation:** HTTP isteği boyunca correlation id üretimi / iletimi middleware ve header standardı ile yapılır.
- **Audit:** `IAuditableRequest` işaretli command’ler (ve pipeline) denetim kaydı üretir; audit writer altyapıda uygulanır.
- **Outbox:** Yan etkiler (e-posta vb.) mümkün olduğunda transactional outbox ile kuyruklanır; işleyici hosted service veya ayrı worker ile tüketilir.

Bu üçü **platform tutarlılığı** için foundation seviyesinde korunur; ürün modülleri aynı pattern’e uyum sağlar.

---

## 9. Domain event outbox — neden şu an park edildi?

**`DomainEventOutboxInterceptor`** (veya eşdeğer otomatik domain event → outbox köprüsü) şu an **bilinçli olarak devre dışı / kullanılmıyor** olarak ele alınır.

**Gerekçe (özet):**

1. **İlk use-case öncesi** tip kaydı, serileştirme sözleşmesi (`$type`, assembly adı, versioning) ve worker tarafı tüketimi netleştirilmelidir.
2. Foundation’da henüz **zorunlu, üretim kritik** bir domain event yayını yoksa interceptor’ı açmak yanlış pozitif outbox mesajları veya kırılgan deserialize yolları doğurabilir.
3. Etkinleştirme; **ilk gerçek domain event** ihtiyacıyla birlikte test edilerek yapılmalıdır (`DomainEventTypeRegistry`, outbox payload, idempotency).

Park kaldırıldığında bu bölüm güncellenmeli ve checklist’e “domain event + outbox” adımları eklenmelidir.

---

## 10. Özet: yapılmaması gerekenler

- Domain’e altyapı referansı eklemek.
- İzin string’lerini controller’da katalog dışı bırakmak.
- Şema için raw SQL ile “sessiz” şema değiştirmek (migration dışı).
- Production secret’larını repoya yazmak.

---

*Bu belge Backend.Veteriner **çekirdeğinin** mevcut sınırlarıyla uyumludur. Repoda bulunmayan veya henüz eklenmemiş ürün modülleri (ör. Contact, Organization, Tasks, veteriner iş domain’i) aktif parça olarak anlatılmaz.*
