# CQRS-12B-6 — Client read-model backfill / rebuild

## Kapsam

CQRS-12B-1..5 ile kurulan client read-model'ine, **mevcut Command DB `Clients` kayıtlarını** Query DB
`ClientReadModels` tablosuna **idempotent** biçimde dolduran/yeniden oluşturan backfill mekanizması
ekler. Bu olmadan `ClientsEnabled=true` açılırsa read-model eksik/boş döner (fallback yok), bu yüzden
backfill **flag açmadan önceki önkoşuldur**.

Bu fazın **dışında** (yapılmadı): `GetClientsListQueryHandler` routing değişikliği, `ClientsEnabled`
default'unu değiştirme, client command handler / event contract değişikliği, Pet read-model,
route/auth/permission/tenant scope davranışı, canlı API/k6 acceptance.

## Yaklaşım — non-destructive idempotent upsert

Appointment rebuild (`AppointmentProjectionRebuildService`) Query tablolarını **siler ve sıfırdan**
kurar; bu yüzden çalışmadan önce bekleyen/dead-letter outbox'ın boş olmasını zorunlu kılar.

Client backfill bilinçli olarak **farklı** bir desen kullanır: **upsert** (`ClientId` PK üzerinden).

- Query tablosu **silinmez**; eksik satır insert, mevcut satır update edilir.
- Bu sayede backfill **canlı projection akışıyla aynı anda** çalışabilir; bekleyen outbox'ı boşaltma
  zorunluluğu yoktur.
- Komponentler:
  - `ClientReadModelBackfillPlanner` (Application, **saf**) — timestamp ve insert/update/skip kararı.
  - `ClientReadModelBackfillService` (Infrastructure) — Command DB'den batch okuma + Query upsert.
  - `IClientReadModelBackfillService` / `ClientReadModelBackfillResult`.
  - DbMigrator komutu: `backfill-client-projections`.

Snapshot üretimi mevcut `ClientProjectionSnapshotFactory` ile yapılır (normalize alanlar projection
ile birebir aynı). Upsert alan eşlemesi `ClientProjectionProcessor` ile aynıdır.

## Timestamp / `LastEventOccurredAtUtc` stratejisi

Backfill bir **event değil snapshot**'tır; gerçek event zamanı yoktur. Seçilen strateji:

```
LastEventOccurredAtUtc = client.UpdatedAtUtc ?? client.CreatedAtUtc   (deterministik)
LastProjectedAtUtc     = backfill wall-clock (TimeProvider)
LastEventId            = Guid.Empty (BackfillEventId — satırın backfill kaynaklı olduğunu işaretler)
```

Gerekçe: `UpdatedAtUtc ?? CreatedAtUtc`, Command DB satırının **son mutasyon zamanı**dır;
gerçek `client.updated.v1` event'inin `OccurredAtUtc`'sine en yakın güvenli, wall-clock'tan bağımsız
deterministik değerdir. Bu değer doğrudan stale-guard ordering anahtarı olarak kullanılır.

## Idempotency

- Anahtar `ClientId` (read-model PK). Tekrar çalıştırma **duplicate üretmez**.
- Karar (`ClientReadModelBackfillPlanner.Decide`):
  - satır yok → **Insert**
  - `backfillOccurredAt >= existing.LastEventOccurredAtUtc` → **Update** (eşitlik dâhil; re-run güvenli)
  - `backfillOccurredAt <  existing.LastEventOccurredAtUtc` → **SkipStale** (veri korunur)
- Aynı veriyle re-run, yalnızca `LastProjectedAtUtc`'yi tazeler; satır sayısı değişmez.

## Race condition değerlendirmesi

Backfill canlı sistemde, yeni client event'leri akarken çalışabilir. Güvenlik şu üç mekanizmayla sağlanır:

1. **Stale guard ortak**: Hem `ClientProjectionProcessor` hem backfill aynı ordering kuralını kullanır
   (daha eski `OccurredAtUtc` daha yeni satırı ezmez). Backfill, gerçek bir event ile yazılmış daha yeni
   satırı **ezmez** (`SkipStale`).
2. **ProcessedProjectionEvents'e dokunulmaz**: Backfill sahte event yazmaz. Bekleyen/gelecek gerçek
   event'ler dedup'a takılmadan, ordering kuralıyla yine de doğru biçimde uygulanır → read-model en güncel
   duruma yakınsar, veri kaybı olmaz.
3. **Batch transaction**: Her batch kendi transaction'ında commit edilir; yarıda hata satırları bozuk
   bırakmaz (upsert olduğu için zaten yeniden çalıştırılabilir).

Sıralama senaryoları:

- Event önce, backfill sonra: backfill daha eski snapshot taşıyorsa `SkipStale`; değilse aynı/yeni veriyle
  update — her iki durumda da en güncel veri korunur.
- Backfill önce, event sonra: event daha yeni `OccurredAtUtc` taşırsa satırı günceller (gerçek
  `LastEventId` yazılır); daha eskiyse stale-guard ile atlanır.

> Not: Backfill **non-destructive** olduğundan appointment rebuild'deki "pending/dead-letter outbox boş
> olmalı" önkoşulu burada **gerekli değildir**.

## Tenant güvenliği

`BackfillAsync(Guid? tenantId, ...)`:

- `tenantId` verilirse Command okuması ve parity sayımı yalnızca o tenant'a filtrelenir; başka tenant
  satırı okunmaz/yazılmaz.
- `null` ise tüm tenant'lar. Upsert anahtarı `ClientId` (global PK) olduğundan tenant sınırları korunur.
- Command/Query aynı veritabanıysa backfill reddedilir (`EnsureDistinctDatabasesAsync`).

## Parity

Backfill sonunda scope'a göre (tenant veya global) Command `Clients` vs Query `ClientReadModels`
sayımı karşılaştırılır; sonuç `ClientReadModelBackfillResult.ParityInSync` ile döner. Client'ta silme
olmadığından tüm satırlar backfill edildiğinde **in-sync** beklenir. Mismatch'te servis exception
atmaz (race'e karşı), **warning** loglar; DbMigrator komutu in-sync değilse exit code `2` döner.

## CLI / tooling

```text
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections --batch-size 500
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-client-projections --tenant <guid>
```

Bağlantı: `ConnectionStrings:DefaultConnection` (command), `ConnectionStrings:QueryConnection` (query).
Çıktı yalnızca sayım/zaman içerir (PII yok). LoadTest ortamı appointment rebuild ile aynıdır.

## PII / loglama

Backfill **client adı/email/telefon loglamaz**. Tüm log/çıktı yalnızca sayım, scope tenant id, süre ve
parity bilgisi içerir.

## Testler (deterministik, CI-safe)

- **Unit** (`Application.Tests/Projections/Clients/ClientReadModelBackfillPlannerTests`):
  timestamp çözümü (UpdatedAt önceliği / CreatedAt fallback) + insert/update/skip-stale kararı + eşitlikte
  idempotent update.
- **Integration** (`IntegrationTests/Projections/Clients/ClientReadModelBackfillIntegrationTests`,
  `client-projection` collection, ayrı LocalDB; hosted servisler kapalı):
  - boş Query DB → backfill `ClientReadModels` doldurur, parity in-sync.
  - re-run → idempotent (insert 0, duplicate yok).
  - Command satırı değişti → backfill update eder.
  - tenant-scoped backfill → diğer tenant'ı yazmaz (izolasyon).
  - `ProcessedProjectionEvents`'e yazmaz.
  - daha yeni event ile yazılmış satırı ezmez (stale guard).
  - tüm tenant backfill → global parity in-sync.
- Mevcut `ClientProjectionProcessor` / parity / smoke testleri değişmedi, geçer.

## Garanti

- `ClientsEnabled` default `false` kaldı.
- `GetClientsListQueryHandler` routing, client command handler ve event contract değişmedi.
- `ClientProjectionProcessor` upsert davranışı değişmedi; backfill aynı alan eşlemesini ve stale-guard'ı
  paylaşır.
- Appointment projection/rebuild davranışı değişmedi.
- Commit atılmadı.
