# CQRS-11C — Appointment Projection Monitoring & Alerts

Production-ready observability guide for appointment CQRS projection: metrics, health thresholds, structured logs, and external alert rules.

**Audience:** operators and platform engineers wiring dashboards/alarms.

**Related:** [`cqrs-11b-operations-runbook.md`](cqrs-11b-operations-runbook.md), [`cqrs-11a-staging-rollout.md`](cqrs-11a-staging-rollout.md), [`appointment-projection-operations.md`](appointment-projection-operations.md)

---

## 1. Mevcut observability altyapısı

| Bileşen | Durum |
|---------|--------|
| **Serilog** | Yapılandırılmış log (API startup) |
| **OpenTelemetry** | Trace + Metrics; OTLP exporter (`OTEL_EXPORTER_OTLP_ENDPOINT`) |
| **System.Diagnostics.Metrics** | Appointment projection instrumentation (`Vetinity.Cqrs.Appointments`) |
| **Prometheus `/metrics`** | **Yok** — scrape endpoint eklenmedi |
| **Application Insights** | **Yok** — vendor-neutral OTLP tercih edildi |
| **Health** | `/health/live` (process only), `/health/ready` (DB + projection) |

Metrikler OTLP üzerinden collector'a aktarılır. Prometheus kullanmak için collector'da OTLP→Prometheus dönüşümü veya ileride `OpenTelemetry.Exporter.Prometheus.AspNetCore` eklenebilir.

---

## 2. Meter / provider

| Alan | Değer |
|------|-------|
| .NET `Meter` adı | `Vetinity.Cqrs.Appointments` |
| OTel kaydı | `WebApplicationBuilderExtensions` → `AddMeter("Vetinity.Cqrs.Appointments")` |
| Instrumentation sınıfı | `AppointmentProjectionMetrics` (singleton) |
| Snapshot holder | `AppointmentProjectionMetricsSnapshotHolder` (in-memory, thread-safe) |

**Export adlandırma:** .NET instrument adları noktalı (`appointment_projection.batch.duration`). OTLP/Prometheus dönüşümünde genelde `vetinity_cqrs_appointment_projection_*` snake_case karşılığı üretilir — collector/exporter konfigürasyonuna bağlıdır. Alarm kurarken gerçek export adını collector'da doğrulayın.

---

## 3. Metrik sözlüğü

### Counter

| .NET instrument | Önerilen export adı | Unit | Açıklama |
|-----------------|---------------------|------|----------|
| `appointment_projection.batches.total` | `vetinity_cqrs_appointment_projection_batches_total` | `{batch}` | İşlenen batch sayısı |
| `appointment_projection.events.processed.total` | `vetinity_cqrs_appointment_projection_events_processed_total` | `{event}` | Başarılı event |
| `appointment_projection.events.failed.total` | `vetinity_cqrs_appointment_projection_events_failed_total` | `{event}` | Başarısız event |
| `appointment_projection.events.dead_lettered.total` | `vetinity_cqrs_appointment_projection_events_dead_lettered_total` | `{event}` | Dead-letter |
| `appointment_projection.rebuilds.total` | `vetinity_cqrs_appointment_projection_rebuilds_total` | `{rebuild}` | Rebuild işlemleri |

### Histogram

| .NET instrument | Önerilen export adı | Unit | Açıklama |
|-----------------|---------------------|------|----------|
| `appointment_projection.batch.duration` | `vetinity_cqrs_appointment_projection_batch_duration_ms` | `ms` | Batch süresi |
| `appointment_projection.event.lag` | `vetinity_cqrs_appointment_projection_event_lag_ms` | `ms` | Backend projection lag |
| `appointment_projection.batch.size` | `vetinity_cqrs_appointment_projection_batch_size` | `{event}` | Batch boyutu |
| `appointment_projection.rebuild.duration` | `vetinity_cqrs_appointment_projection_rebuild_duration_ms` | `ms` | Rebuild süresi |

### Observable gauge (snapshot)

| .NET instrument | Önerilen export adı | Unit | Açıklama |
|-----------------|---------------------|------|----------|
| `appointment_projection.pending` | `vetinity_cqrs_appointment_projection_pending` | `{message}` | Pending outbox |
| `appointment_projection.retry_waiting` | `vetinity_cqrs_appointment_projection_retry_waiting` | `{message}` | Retry bekleyen |
| `appointment_projection.dead_letter` | `vetinity_cqrs_appointment_projection_dead_letter` | `{message}` | Dead-letter sayısı |
| `appointment_projection.oldest_pending_age` | `vetinity_cqrs_appointment_projection_oldest_pending_age_seconds` | `s` | En eski pending yaşı |
| `appointment_projection.enabled` | `vetinity_cqrs_appointment_projection_enabled` | `1/0` | Projector enabled |
| `appointment_projection.appointments_query_read_enabled` | `vetinity_cqrs_appointment_query_read_enabled` | `1/0` | Appointments read flag |
| `appointment_projection.dashboard_query_read_enabled` | `vetinity_cqrs_dashboard_query_read_enabled` | `1/0` | Dashboard read flag |
| `appointment_projection.query_database_healthy` | `vetinity_cqrs_appointment_projection_query_database_healthy` | `1/0` | Query DB erişim + migration |

---

## 4. Tag'ler ve cardinality

### İzin verilen tag'ler

| Tag | Değerler |
|-----|----------|
| `operation` | `create`, `reschedule`, `cancel`, `rebuild`, `unknown` |
| `result` | `success`, `failed`, `dead_letter` |
| `event_type` | `created`, `rescheduled`, `cancelled`, `unknown` |

Bilinmeyen outbox type → `unknown`. Raw CLR type adı veya serbest metin tag olarak yazılmaz.

### Kesinlikle kullanılmayan tag'ler

`TenantId`, `ClinicId`, `UserId`, `AppointmentId`, `ClientId`, `PetId`, event ID, exception message, SQL, connection string, URL query, stack trace.

---

## 5. Backend projection lag tanımı

```text
backend_projection_lag_ms = Query DB transaction commit UTC − OutboxMessages.CreatedAtUtc
```

- UTC kullanılır
- Negatif sonuç 0'a clamp edilir
- Bozuk timestamp metriği patlatmaz
- create / reschedule / cancel aynı tanımı kullanır
- Ölçüm noktası: `AppointmentProjectionProcessor` içinde `transaction.CommitAsync` sonrası

### Client visibility lag (farklı metrik)

k6 `appointment-projection-lag.js` workload'u **client-observed visibility lag** ölçer (HTTP poll ile read model görünürlüğü). Bu, backend processing lag ile aynı şey değildir.

| Metrik | Ne ölçer |
|--------|----------|
| `appointment_projection.event.lag` | Sunucu tarafı commit gecikmesi |
| k6 projection lag | İstemci poll ile görünürlük gecikmesi |

Alarm ve SLO raporlarında bu iki metriği karıştırmayın.

---

## 6. Status snapshot yaklaşımı

- `IAppointmentProjectionStatusReader` tek aggregated outbox sorgusu çalıştırır
- Health check ve hosted service batch sonrası snapshot günceller
- `ObservableGauge` callback'leri **async SQL çalıştırmaz** — yalnızca `AppointmentProjectionMetricsSnapshotHolder` okur
- Her health/scrape çağrısında ek tablo taraması yok

Snapshot yenileme:

1. `/health/ready` → `AppointmentProjectionHealthCheck`
2. Projection batch sonrası → `AppointmentProjectionHostedService` → `AppointmentProjectionMetricsStatusRefresher`

---

## 7. Health semantiği

### `/health/live`

- DB sorgusu yok
- Outbox count yok
- Parity yok
- Yalnız process yaşamı

### `/health/ready` — `appointment-projection` check

| Seviye | Koşullar |
|--------|----------|
| **Healthy** | Pending yok veya yaş < degraded eşiği; retry-waiting yok; dead-letter yok; flag/projector uyumu; DB sağlıklı |
| **Degraded** | `oldestPendingAgeSeconds` ≥ `DegradedAfterSeconds` (10s); retry-waiting > 0; query read açık + projector kapalı + pending yok |
| **Unhealthy** | dead-letter > 0; pending yaş ≥ `UnhealthyAfterSeconds` (30s); Query DB unreachable/migration; query read açık + projector kapalı + pending/retry > 0 |

`AppointmentProjectionMonitoringOptions` alarm rehberi eşiklerini tanımlar; health evaluator `AppointmentProjectionHealthOptions` kullanır (`DegradedAfterSeconds=10`, `UnhealthyAfterSeconds=30`).

---

## 8. Konfigürasyon

### `AppointmentProjectionMonitoring` (alarm rehberi / gelecek parity)

```json
{
  "WarningPendingAgeSeconds": 10,
  "CriticalPendingAgeSeconds": 30,
  "ParityCheckEnabled": false,
  "ParityCheckIntervalSeconds": 300
}
```

Validation: `warning > 0`, `critical > warning`, `interval > 0` — geçersiz config startup'ta fail-fast.

### `AppointmentProjectionHealth` (health endpoint)

```json
{
  "DegradedAfterSeconds": 10,
  "UnhealthyAfterSeconds": 30,
  "DeadLetterIsUnhealthy": true
}
```

---

## 9. Alarm eşikleri

### Warning

| Koşul | Önerilen süre |
|-------|---------------|
| `oldest_pending_age_seconds > 10` | 2 ardışık değerlendirme veya 2 dakika |
| `retry_waiting > 0` | 2 dakika |
| `projection p95 > 2000 ms` (histogram) | 5 dakika |
| query read açık + projector kapalı | 2 dakika |
| Command/Query parity mismatch | operasyon script sonucu |

### Critical

| Koşul | Önerilen süre |
|-------|---------------|
| `oldest_pending_age_seconds > 30` | 1 dakika |
| `dead_letter > 0` | hemen (flapping beklemesi gerekmez) |
| `projection p99 > 5000 ms` | 2 dakika |
| `query_database_healthy == 0` AND query read enabled | 1 dakika |
| projector kapalı + query read açık + `pending > 0` | 1 dakika |

### Recovery

2 temiz değerlendirme (warning/critical clear).

---

## 10. Vendor-neutral alarm örnekleri (pseudo-rule)

Prometheus exporter **yok** — aşağıdaki kurallar pseudo PromQL'dir. Gerçek PromQL yalnız OTLP→Prometheus pipeline kurulduktan sonra export adları doğrulanarak yazılmalıdır.

```text
# Warning
oldest_pending_age_seconds > 10 for 2m

# Critical
oldest_pending_age_seconds > 30 for 1m

# Critical (immediate)
dead_letter > 0

# Warning
rate(events_failed_total[5m]) > 0

# Critical
query_database_healthy == 0 AND appointments_query_read_enabled == 1

# Warning (histogram percentile — collector'da p95)
histogram_quantile(0.95, rate(appointment_projection_event_lag_bucket[5m])) > 2000

# Critical
histogram_quantile(0.99, rate(appointment_projection_event_lag_bucket[5m])) > 5000
```

---

## 11. Parity monitoring

Tam parity her health/scrape çağrısında çalıştırılmaz (`ParityCheckEnabled=false` varsayılan).

**Tercih sırası:**

1. CQRS-11A/11B operasyon scriptleri (`Get-CqrsStagedParityReport`, `Assert-DataParity`)
2. Scheduled read-only probe (harici)
3. Interval ile cache'lenmiş sonuç (gelecek faz)
4. Structured log + gauge

Parity kapsamı (yalnız count eşitliği yeterli değil):

- Command vs Query appointment count
- count difference
- duplicate `AppointmentId`
- empty Guid
- `ScheduledEndUtc < ScheduledAtUtc`
- daily stats invariant

---

## 12. Structured log eventleri

| Event | Seviye | Alanlar (PII-safe) |
|-------|--------|-------------------|
| `AppointmentProjectionBatchCompleted` | Information | ProcessedCount, FailedCount, DeadLetteredCount, DurationMs, BatchSize, OldestPendingAgeMs, ConsumerName |
| `AppointmentProjectionBatchFailed` | Warning | Aynı batch alanları |
| `AppointmentProjectionHealthDegraded` | Warning | PendingCount, RetryWaitingCount, DeadLetterCount, OldestPendingAgeSeconds |
| `AppointmentProjectionHealthUnhealthy` | Error | Aynı |
| `AppointmentProjectionDeadLetterDetected` | Error | Type, Retry (tag değil) |
| `AppointmentProjectionParityMismatch` | Warning | Count diff (script/ops) |
| `AppointmentProjectionRecovered` | Debug | PendingCount, RetryWaitingCount |

Loglanmaz: connection string, JWT, payload, TenantId, ClinicId, UserId, AppointmentId, stack trace property spam.

---

## 13. Operasyonel aksiyonlar (alarm → runbook)

| Alarm | İlk aksiyon |
|-------|-------------|
| Pending age warning | Outbox durumu, projector log, batch throughput |
| Pending age critical | CQRS-11B backlog prosedürü; gerekirse Mode A rollback |
| Dead-letter | Root cause; manuel müdahale (desteklenmiş replay yok) |
| Query DB unhealthy + read on | Mode C→B→A rollback; projector **açık kalmalı** |
| Parity mismatch | `rebuild-appointment-projections` + parity script |
| Projection p95/p99 | CQRS-10.1 optimizasyon baseline ile karşılaştır |

**Tek instance uyarısı:** Birden fazla projector instance claim/lease olmadan çalıştırılmamalı (CQRS-11D).

---

## 14. Staging doğrulama adımları

1. `dotnet test --no-restore` — tüm testler geçmeli
2. `.\tests\load\tools\Test-CqrsStagedRollout.ps1` — readiness (monitoring doc + config dahil)
3. Projection lag workload (Mode C, API + load DB):

```powershell
.\tests\load\tools\Invoke-CqrsLoadMeasurement.ps1
# veya yalnız projection-lag:
# Invoke-LoadCaseWithPerf -Mode full-query -Workload projection-lag -Vus 2 -Duration 5m
```

Hedef (client-observed):

- create/reschedule/cancel p95 < 2000 ms
- p99 < 5000 ms
- timeout = 0, wrong-state = 0, skipped = 0, http_req_failed = 0

4. OTLP collector'da `Vetinity.Cqrs.Appointments` meter görünürlüğünü doğrula
5. `/health/ready` appointment-projection data alanlarını kontrol et

---

## 15. Exporter entegrasyon notu

Bu fazda yeni Prometheus paketi eklenmedi. Minimum bağımlılık:

- Mevcut OTLP exporter + harici OpenTelemetry Collector
- Veya gelecekte: `OpenTelemetry.Exporter.Prometheus.AspNetCore` + `/metrics` endpoint

Testlerde metrik doğrulama `MeterListener` ile yapılır; production'da collector zorunludur.

---

## 16. Kalan riskler

- Parity otomatik background worker yok — operasyon script'ine bağımlılık
- Observable gauge snapshot batch/health arasında kısa stale penceresi olabilir
- Multi-instance projector claim/lease yok (CQRS-11D)
- Histogram percentile alarmları collector/exporter konfigürasyonuna bağlı
- Gerçek staging rollout bu fazda yapılmadı — eşikler local/load baseline'a göre ayarlandı
