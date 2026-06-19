# CQRS-12B-1 — Client Read-Model Schema Foundation

## Kapsam

Bu faz **yalnızca** Client CQRS read-model'i için Query DB tarafında minimal ve güvenli şema temelini hazırlar.
Yazma davranışı, handler routing, feature flag, event emission ve projection processor bu fazın **dışındadır**.

## Yapılanlar

- Query DB `ClientReadModel` entity'si eklendi.
- EF configuration (`ClientReadModelConfiguration`) mevcut read-model desenine uygun eklendi.
- `QueryDbContext`'e `ClientReadModels` DbSet'i eklendi.
- Query DB migration oluşturuldu: `20260619192529_AddClientReadModel`.

## Tablo: `ClientReadModels`

| Kolon | Tip | Null | Not |
|-------|-----|------|-----|
| `ClientId` | `uniqueidentifier` | Hayır | PK |
| `TenantId` | `uniqueidentifier` | Hayır | Zorunlu, tenant scope |
| `FullName` | `nvarchar(300)` | Hayır | Görüntüleme |
| `FullNameNormalized` | `nvarchar(300)` | Hayır | İleride search için |
| `Email` | `nvarchar(320)` | Evet | |
| `Phone` | `nvarchar(50)` | Evet | |
| `PhoneNormalized` | `nvarchar(50)` | Evet | İleride search için |
| `CreatedAtUtc` | `datetime2` | Hayır | |
| `LastEventId` | `uniqueidentifier` | Hayır | Projection idempotency/dedup (appointment deseni) |
| `LastProjectedAtUtc` | `datetime2` | Hayır | Projection izleme (appointment deseni) |

### Index'ler

- `IX_ClientReadModels_TenantId_FullNameNormalized_ClientId` — (TenantId, FullNameNormalized, ClientId)
- `IX_ClientReadModels_TenantId_PhoneNormalized` — (TenantId, PhoneNormalized)
- `IX_ClientReadModels_TenantId_Email` — (TenantId, Email)

> Not: İsim index'ine sondan `ClientId` eklendi. Bu, `AppointmentReadModel` desenindeki gibi (PK'yi index'e dahil etme)
> stabil sıralama/keyset paging için tiebreaker sağlar. İstenen "TenantId + FullNameNormalized" temeli korunur.

## PetCount neden eklenmedi

PetCount bu fazda **eklenmedi**. Pet event'leri ve Pet projection'ı henüz mevcut değil; bu alan eklenirse
projection beslenmediği için kalıcı olarak stale (0 veya tutarsız) kalır ve yanıltıcı veri riski doğurur.
Pet read-model fazı (Client sonrası) geldiğinde değerlendirilmelidir.

## Soft delete / `IsDeleted` önerisi (uygulanmadı)

Mevcut durumda `Client` domain entity'sinde soft-delete yok: `ISoftDelete` arayüzü, `IsDeleted` alanı veya
EF global query filter bulunmuyor. Client için silme komutu/endpoint'i de implemente edilmemiş (yalnızca Create/Update).

Bu nedenle bu fazda `IsDeleted` **eklenmedi**. Öneri: Client tarafında soft-delete davranışı netleştiğinde
(silme komutu + event eklendiğinde) read-model'e `IsDeleted` alanı ve buna bağlı filtreli index eklenmesi
değerlendirilmelidir. Emin olunmadan uygulanmamıştır.

## Uygulama (apply) komutu

Migration henüz veritabanına manuel uygulanmadı; DbMigrator üzerinden uygulanır:

```bash
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
```

## Garanti

- Production command/read handler davranışı değişmedi (`GetClientsListQueryHandler`, command handler'lar dokunulmadı).
- Feature flag, event emission, projection processor eklenmedi.
- Mevcut testler kırılmadı.
