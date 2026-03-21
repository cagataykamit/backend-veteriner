# Yeni modül (bounded context) checklist

Yeni bir **bounded context** foundation üzerine eklenirken aşağıdaki adımlar sırayla veya paralel iş paketleri olarak izlenmelidir. Maddeler tamamlandıkça işaretlenebilir.

---

## 1. Tasarım

- [ ] **Aggregate sınırı** net: hangi kök entity, hangi child koleksiyonlar, tutarlılık sınırı nerede?
- [ ] **Entity / value object** ayrımı: hangi alanlar immutable değer nesnesi, hangileri entity kimliği?
- [ ] Okuma/yazma modeli: basit CRUD mi, yoksa ayrı read model / rapor ihtiyacı var mı?

---

## 2. Application — command / query listesi

- [ ] Use-case listesi (oluştur, güncelle, sil, listele, detay, durum geçişi …).
- [ ] Her use-case için **Command** veya **Query** + **Handler** (+ gerekirse **Validator**).
- [ ] `Result` / `Result<T>` ile hata senaryoları tanımlandı mı?
- [ ] Gerekirse **IAuditableRequest** işaretleme (denetim gereksinimine göre).

---

## 3. Permission

- [ ] `PermissionCatalog` içine yeni izin anahtarları eklendi.
- [ ] API uçlarında policy / `[Authorize(Policy = …)]` katalog ile uyumlu.
- [ ] Seed (`PermissionSeeder` veya eşdeğer) yeni izinleri DB’ye yazıyor.

---

## 4. Seed güncelleme

- [ ] Sabit roller / claim’ler / başlangıç verisi gerekiyorsa seed sınıfları güncellendi.
- [ ] Test seed’leri (integration) gerekiyorsa `TestDataSeeder` veya factory ile uyumlu.

---

## 5. Repository ve EF configuration

- [ ] Application’da repository **arabirimleri** (gerekirse specification / özel metotlar).
- [ ] Infrastructure’da **implementasyonlar** (`AppDbContext`, concrete repository).
- [ ] `IEntityTypeConfiguration<T>` ile mapping; indeks / FK isimlendirme tutarlı.
- [ ] `AppDbContext`’e `DbSet` ve gerekli `OnModelCreating` bağlantıları.

---

## 6. Controller (API)

- [ ] Route ve API versiyonu mevcut convention ile uyumlu.
- [ ] MediatR `Send`; Result → HTTP mapping.
- [ ] Swagger açıklamaları / response type’lar (mümkün olduğunca eksiksiz).

---

## 7. Application tests

- [ ] Handler unit testleri (mock repository / options).
- [ ] Validator testleri (sınır değerler, hata mesajları).

---

## 8. Integration tests

- [ ] En az bir “happy path” ve bir yetki / doğrulama hatası senaryosu (üçüncü parti maliyetine göre).
- [ ] Test DB / migration stratejisi foundation ile aynı prensipte.

---

## 9. Migration

- [ ] `dotnet ef migrations add …` ile şema değişikliği kayıtlı.
- [ ] Review: geri alma planı, indeks, veri migrasyonu ihtiyacı.
- [ ] CI’da migration’ın uygulanabilirliği doğrulandı.

---

## 10. Observability

- [ ] Kritik path’lerde anlamlı log (PII içermeden).
- [ ] Gerekirse Activity / metrik isimleri **foundation isimlendirme** ile uyumlu (`Backend.Veteriner.*` telemetry kaynakları).
- [ ] Hata ayıklama için correlation id’nin loglarda görünür olduğu doğrulandı.

---

## 11. Domain event (ileride)

> Foundation’da **`DomainEventOutboxInterceptor` şu an park edilmiştir.** İlk domain event use-case’inde aşağılar eklenmelidir:

- [ ] Event tipi ve serileştirme sözleşmesi.
- [ ] `DomainEventTypeRegistry` (veya eşdeğer) ile tip çözümü.
- [ ] Outbox mesaj tipi ve worker işleme.
- [ ] Entegrasyon testi: publish → outbox → işlendi doğrulaması.

---

## Not

Bu checklist **yeni ürün modülü** içindir. Foundation’ın çekirdeğinde (auth, users, permissions, outbox, audit) değişiklik yapılıyorsa ek olarak geriye dönük uyumluluk ve güvenlik review’ı zorunludur.

---

*İlgili kurallar: [`foundation-rules.md`](foundation-rules.md)*
