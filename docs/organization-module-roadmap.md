# Organization Modülü – Uygulanabilir Yol Haritası

> **Durum (Veteriner):** Bu belge **taslak yol haritasıdır**; Organization domain kodu bu repoda **uygulanmamıştır**. [`docs/README.md`](README.md).

**Tarih:** 2025-03-11  
**Kapsam:** İlk business modül – OrganizationUnit, Employee, Position; DDD/CQRS, mevcut altyapı ile tam uyum.

---

## 1. Genel mimari karar

### Bounded context sınırı
- **Organization** modülü tek bounded context: kurumsal birimler (OrganizationUnit), çalışanlar (Employee), pozisyonlar (Position) ve aralarındaki ilişkiler. User (auth) ve Permission (auth) bu context’in dışında kalır; Employee ile User opsiyonel bağ ile ilişkilenir (Employee.UserId nullable).

### Aggregate root’lar
- **OrganizationUnit** – aggregate root. Kendi hiyerarşisi (ParentOrganizationUnitId), ad, kod, aktif/pasif. Alt birimler ve çalışan atamaları bu aggregate’e referans ile yönetilir.
- **Employee** – aggregate root. Ad, soyad, e-posta, opsiyonel UserId, OrganizationUnitId, PositionId, ManagerEmployeeId, aktif/pasif. Kendi yaşam döngüsü ve kuralları.
- **Position** – aggregate root. Unvan adı, kod, açıklama, aktif/pasif. Bağımsız tanım; Employee tarafında sadece referans (PositionId).

### Neden bu sınırlar?
- Her root kendi consistency boundary’si: OrganizationUnit silinmez (deaktive), Employee ve Position da öyle. Hiyerarşi ilk aşamada ParentId / ManagerEmployeeId ile; event/transaction karmaşıklığı az.
- User ayrı: Auth bounded context değişmeden kalır; Employee.UserId ile “bu çalışan sisteme giriş yapabiliyor” bilgisi taşınır.
- Position ayrı root: Unvanlar merkezi tanım; birçok çalışan aynı pozisyonda. Employee sadece PositionId tutar.

### İlk fazda dışarıda bırakılanlar
- OrganizationUnit hiyerarşi tree query (GetTree); “move unit” (parent değiştirme); unit’e toplu çalışan atama.
- Employee: “assign user” (UserId sonradan bağlama) ayrı use-case olarak sonraki faz.
- Position: hierarchy / career path; pozisyon değişikliği geçmişi (audit ile çözülebilir).
- Soft delete yerine IsActive; gerçek silme yok.

---

## 2. Domain model tasarımı

### 2.1 OrganizationUnit (aggregate root)

**Alanlar**
- `Id` (Guid)
- `Code` (string, unique)
- `Name` (string)
- `ParentOrganizationUnitId` (Guid?, nullable – root birimler null)
- `IsActive` (bool, default true)
- `CreatedAtUtc`, `UpdatedAtUtc` (DateTime/DateTime?)

**Kurucu / factory**
- `OrganizationUnit(string code, string name, Guid? parentOrganizationUnitId)`  
  - Code/Name trim, boş kontrolü. ParentId opsiyonel.

**Domain metotları**
- `Update(string name)` – ad güncelleme
- `Activate()` – IsActive = true
- `Deactivate()` – IsActive = false

**Kurallar / invariants**
- Code ve Name boş olamaz; Code unique (repository/application’da kontrol).
- Deaktive birim alt birimlere “parent” olabilir mi: ilk fazda serbest (isteğe bağlı kural: deaktive unit’in altına yeni unit eklenemesin).

**Domain event (ilk fazda opsiyonel)**
- OrganizationUnitCreatedDomainEvent(OrganizationUnitId, Code, Name) – ileride bildirim/read model için; ilk sprint’te zorunlu değil.

---

### 2.2 Employee (aggregate root)

**Alanlar**
- `Id` (Guid)
- `FirstName` (string)
- `LastName` (string)
- `Email` (string, nullable – zorunlu değil)
- `UserId` (Guid?, nullable – sisteme giriş yapan kullanıcı)
- `OrganizationUnitId` (Guid – bağlı olduğu birim)
- `PositionId` (Guid – unvan)
- `ManagerEmployeeId` (Guid?, nullable)
- `IsActive` (bool, default true)
- `CreatedAtUtc`, `UpdatedAtUtc` (DateTime/DateTime?)

**Kurucu / factory**
- `Employee(string firstName, string lastName, string? email, Guid organizationUnitId, Guid positionId, Guid? managerEmployeeId)`  
  - FirstName/LastName zorunlu; Email opsiyonel. OrganizationUnitId ve PositionId dış referans (varlık kontrolü application’da).

**Domain metotları**
- `Update(string firstName, string lastName, string? email)`
- `AssignUnit(Guid organizationUnitId)`
- `ChangePosition(Guid positionId)`
- `AssignManager(Guid? managerEmployeeId)` – null = yönetici yok
- `Activate()` / `Deactivate()`

**Kurallar / invariants**
- FirstName, LastName boş olamaz. Email formatı validator’da (FluentValidation). ManagerEmployeeId ≠ Id (kendisi yönetici olamaz; opsiyonel domain kuralı).
- OrganizationUnit ve Position varlığı application’da kontrol edilir; domain sadece id alır.

**Domain event (ilk fazda opsiyonel)**
- EmployeeCreatedDomainEvent(EmployeeId, OrganizationUnitId, PositionId) – ilk sprint’te zorunlu değil.

---

### 2.3 Position (aggregate root)

**Alanlar**
- `Id` (Guid)
- `Code` (string, unique)
- `Name` (string)
- `Description` (string, nullable)
- `IsActive` (bool, default true)
- `CreatedAtUtc`, `UpdatedAtUtc` (DateTime/DateTime?)

**Kurucu / factory**
- `Position(string code, string name, string? description)`  
  - Code/Name trim, boş olamaz.

**Domain metotları**
- `Update(string name, string? description)`
- `Activate()` / `Deactivate()`

**Kurallar / invariants**
- Code unique (repository/application’da); Name boş olamaz.

**Domain event (ilk fazda opsiyonel)**
- PositionCreatedDomainEvent – ilk sprint’te zorunlu değil.

---

## 3. Permission katalog planı

Mevcut pattern: `PermissionCatalog.{Area}.Read` / `PermissionCatalog.{Area}.Write`.

| Code | Açıklama | Neden |
|------|----------|--------|
| **Organization.Units.Read** | Organizasyon birimlerini listeleme ve detay görüntüleme | List/GetById için policy |
| **Organization.Units.Write** | Organizasyon birimi oluşturma, güncelleme, aktif/pasif | Create/Update/Activate/Deactivate |
| **Organization.Employees.Read** | Çalışanları listeleme ve detay görüntüleme | List/GetById |
| **Organization.Employees.Write** | Çalışan oluşturma, güncelleme, birim/pozisyon/yönetici atama, aktif/pasif | Tüm mutasyonlar |
| **Organization.Positions.Read** | Pozisyonları listeleme ve detay görüntüleme | List/GetById |
| **Organization.Positions.Write** | Pozisyon oluşturma, güncelleme, aktif/pasif | Create/Update/Activate/Deactivate |

İlk fazda bu altı permission yeterli; daha sonra Units.Tree, Employees.Export vb. eklenebilir.

---

## 4. CQRS klasör yapısı

Mevcut Application yapısına uyumlu (Auth/Commands, Auth/Queries, Users/Commands, Users/Queries, Contracts/Dtos, Validators alt klasörlerinde).

```
Backend.Veteriner.Application/
├── Organization/
│   ├── Contracts/
│   │   └── Dtos/
│   │       ├── OrganizationUnitDto.cs
│   │       ├── OrganizationUnitListItemDto.cs
│   │       ├── EmployeeDto.cs
│   │       ├── EmployeeListItemDto.cs
│   │       ├── PositionDto.cs
│   │       └── PositionListItemDto.cs
│   │
│   ├── OrganizationUnits/
│   │   ├── Commands/
│   │   │   ├── Create/
│   │   │   │   ├── CreateOrganizationUnitCommand.cs
│   │   │   │   └── CreateOrganizationUnitCommandHandler.cs
│   │   │   ├── Update/
│   │   │   │   ├── UpdateOrganizationUnitCommand.cs
│   │   │   │   └── UpdateOrganizationUnitCommandHandler.cs
│   │   │   ├── Activate/
│   │   │   │   ├── ActivateOrganizationUnitCommand.cs
│   │   │   │   └── ActivateOrganizationUnitCommandHandler.cs
│   │   │   └── Deactivate/
│   │   │       ├── DeactivateOrganizationUnitCommand.cs
│   │   │       └── DeactivateOrganizationUnitCommandHandler.cs
│   │   ├── Queries/
│   │   │   ├── GetAll/
│   │   │   │   ├── GetOrganizationUnitsQuery.cs
│   │   │   │   └── GetOrganizationUnitsQueryHandler.cs
│   │   │   └── GetById/
│   │   │       ├── GetOrganizationUnitByIdQuery.cs
│   │   │       └── GetOrganizationUnitByIdQueryHandler.cs
│   │   └── Validators/
│   │       ├── CreateOrganizationUnitCommandValidator.cs
│   │       └── UpdateOrganizationUnitCommandValidator.cs
│   │
│   ├── Employees/
│   │   ├── Commands/
│   │   │   ├── Create/
│   │   │   │   ├── CreateEmployeeCommand.cs
│   │   │   │   └── CreateEmployeeCommandHandler.cs
│   │   │   ├── Update/
│   │   │   ├── AssignUnit/
│   │   │   ├── ChangePosition/
│   │   │   ├── AssignManager/
│   │   │   ├── Activate/
│   │   │   └── Deactivate/
│   │   ├── Queries/
│   │   │   ├── GetAll/
│   │   │   └── GetById/
│   │   └── Validators/
│   │
│   └── Positions/
│       ├── Commands/
│       │   ├── Create/
│       │   ├── Update/
│       │   ├── Activate/
│       │   └── Deactivate/
│       ├── Queries/
│       │   ├── GetAll/
│       │   └── GetById/
│       └── Validators/
```

İlk fazda sadece Create + GetAll + GetById + (Update/Activate/Deactivate isteğe bağlı) açılacak; AssignUnit, ChangePosition, AssignManager sonraki fazda.

---

## 5. İlk faz use-case listesi (öncelik sırası)

**İlk sprint – açılacaklar**
1. Position: Create, GetAll (paged), GetById  
2. OrganizationUnit: Create, GetAll (paged), GetById  
3. Employee: Create, GetAll (paged), GetById  

**Sonraki sprint**
4. OrganizationUnit: Update, Activate, Deactivate  
5. Employee: Update, AssignUnit, ChangePosition, AssignManager, Activate, Deactivate  
6. Position: Update, Activate, Deactivate  
7. OrganizationUnit tree query (isteğe bağlı)  
8. Employee list filtre (unit, position, active)

---

## 6. Fiziksel dosya planı (sıralı, bağımlılıklarla)

Aşağıda **ilk faz** için açılacak dosyalar; her satır: Katman | Klasör | Dosya | Amaç | Bağımlılık.

### Domain (önce)

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Domain | Organization | Position.cs | Position aggregate root; Code, Name, Description, IsActive, CreatedAtUtc, UpdatedAtUtc; constructor, Update, Activate, Deactivate | - |
| Backend.Veteriner.Domain | Organization | OrganizationUnit.cs | OrganizationUnit aggregate root; Code, Name, ParentId, IsActive, timestamps; constructor, Update, Activate, Deactivate | - |
| Backend.Veteriner.Domain | Organization | Employee.cs | Employee aggregate root; FirstName, LastName, Email, UserId, OrganizationUnitId, PositionId, ManagerEmployeeId, IsActive, timestamps; constructor, Update, AssignUnit, ChangePosition, AssignManager, Activate, Deactivate | - |

Not: Domain’de Position/OrganizationUnit/Employee için AggregateRoot’tan türetme isteğe bağlı (ilk fazda domain event yoksa türetmeyebilirsin; event ekleyeceksen türet).

### Application – Contracts / DTOs

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Application | Organization/Contracts/Dtos | PositionDto.cs | GetById response: Id, Code, Name, Description, IsActive, CreatedAtUtc | - |
| Backend.Veteriner.Application | Organization/Contracts/Dtos | PositionListItemDto.cs | List item: Id, Code, Name, IsActive | - |
| Backend.Veteriner.Application | Organization/Contracts/Dtos | OrganizationUnitDto.cs | GetById: Id, Code, Name, ParentOrganizationUnitId, IsActive, CreatedAtUtc, UpdatedAtUtc | - |
| Backend.Veteriner.Application | Organization/Contracts/Dtos | OrganizationUnitListItemDto.cs | List: Id, Code, Name, ParentOrganizationUnitId, IsActive | - |
| Backend.Veteriner.Application | Organization/Contracts/Dtos | EmployeeDto.cs | GetById: Id, FirstName, LastName, Email, UserId, OrganizationUnitId, PositionId, ManagerEmployeeId, IsActive, CreatedAtUtc, UpdatedAtUtc | - |
| Backend.Veteriner.Application | Organization/Contracts/Dtos | EmployeeListItemDto.cs | List: Id, FirstName, LastName, Email, OrganizationUnitId, PositionId, IsActive | - |

### Application – Position (Commands / Queries)

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Application | Organization/Positions/Commands/Create | CreatePositionCommand.cs | IRequest<Result<Guid>>, IAuditableRequest; Code, Name, Description | Domain.Shared |
| Backend.Veteriner.Application | Organization/Positions/Commands/Create | CreatePositionCommandHandler.cs | Duplicate code kontrolü, Position oluşturma, SaveChanges | IPositionRepository veya IRepository<Position>, CreatePositionCommand |
| Backend.Veteriner.Application | Organization/Positions/Queries/GetAll | GetPositionsQuery.cs | IRequest<PagedResult<PositionListItemDto>>; PageRequest | Common.Models, Dtos |
| Backend.Veteriner.Application | Organization/Positions/Queries/GetAll | GetPositionsQueryHandler.cs | Paged list | IPositionRepository / IReadRepository<Position>, GetPositionsQuery |
| Backend.Veteriner.Application | Organization/Positions/Queries/GetById | GetPositionByIdQuery.cs | IRequest<Result<PositionDto>>; Id | Domain.Shared, Dtos |
| Backend.Veteriner.Application | Organization/Positions/Queries/GetById | GetPositionByIdQueryHandler.cs | Get by id, Result.Failure(NotFound) veya Success(dto) | IPositionRepository veya IReadRepository, GetPositionByIdQuery |
| Backend.Veteriner.Application | Organization/Positions/Validators | CreatePositionCommandValidator.cs | FluentValidation: Code/Name not empty | CreatePositionCommand |

### Application – OrganizationUnit (Commands / Queries)

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Application | Organization/OrganizationUnits/Commands/Create | CreateOrganizationUnitCommand.cs | IRequest<Result<Guid>>, IAuditableRequest; Code, Name, ParentOrganizationUnitId? | Domain.Shared |
| Backend.Veteriner.Application | Organization/OrganizationUnits/Commands/Create | CreateOrganizationUnitCommandHandler.cs | Duplicate Code kontrolü, parent varlık (varsa), Add + SaveChanges | IOrganizationUnitRepository, CreateOrganizationUnitCommand |
| Backend.Veteriner.Application | Organization/OrganizationUnits/Queries/GetAll | GetOrganizationUnitsQuery.cs | IRequest<PagedResult<OrganizationUnitListItemDto>> | Common.Models, Dtos |
| Backend.Veteriner.Application | Organization/OrganizationUnits/Queries/GetAll | GetOrganizationUnitsQueryHandler.cs | Paged list | IOrganizationUnitRepository veya IReadRepository, GetOrganizationUnitsQuery |
| Backend.Veteriner.Application | Organization/OrganizationUnits/Queries/GetById | GetOrganizationUnitByIdQuery.cs | IRequest<Result<OrganizationUnitDto>> | Domain.Shared, Dtos |
| Backend.Veteriner.Application | Organization/OrganizationUnits/Queries/GetById | GetOrganizationUnitByIdQueryHandler.cs | Get by id, Result.NotFound veya Success(dto) | IOrganizationUnitRepository, GetOrganizationUnitByIdQuery |
| Backend.Veteriner.Application | Organization/OrganizationUnits/Validators | CreateOrganizationUnitCommandValidator.cs | Code, Name not empty | CreateOrganizationUnitCommand |

### Application – Employee (Commands / Queries)

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Application | Organization/Employees/Commands/Create | CreateEmployeeCommand.cs | IRequest<Result<Guid>>, IAuditableRequest; FirstName, LastName, Email?, OrganizationUnitId, PositionId, ManagerEmployeeId? | Domain.Shared |
| Backend.Veteriner.Application | Organization/Employees/Commands/Create | CreateEmployeeCommandHandler.cs | OrganizationUnit ve Position varlık kontrolü, Manager varlık (varsa), new Employee(…), Add + SaveChanges | IEmployeeRepository, IOrganizationUnitRepository, IPositionRepository, CreateEmployeeCommand |
| Backend.Veteriner.Application | Organization/Employees/Queries/GetAll | GetEmployeesQuery.cs | IRequest<PagedResult<EmployeeListItemDto>>; PageRequest, opsiyonel filter (OrganizationUnitId, PositionId, IsActive) | Common.Models, Dtos |
| Backend.Veteriner.Application | Organization/Employees/Queries/GetAll | GetEmployeesQueryHandler.cs | Paged list (join unit/position name istenirse Dto’da) | IEmployeeRepository, GetEmployeesQuery |
| Backend.Veteriner.Application | Organization/Employees/Queries/GetById | GetEmployeeByIdQuery.cs | IRequest<Result<EmployeeDto>> | Domain.Shared, Dtos |
| Backend.Veteriner.Application | Organization/Employees/Queries/GetById | GetEmployeeByIdQueryHandler.cs | Get by id, Result.NotFound veya Success(dto) | IEmployeeRepository, GetEmployeeByIdQuery |
| Backend.Veteriner.Application | Organization/Employees/Validators | CreateEmployeeCommandValidator.cs | FirstName, LastName not empty; Email format; OrganizationUnitId, PositionId not empty | CreateEmployeeCommand |

### Application – Permission katalog

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Application | Auth | PermissionCatalog.cs | Organization.Units.Read/Write, Organization.Employees.Read/Write, Organization.Positions.Read/Write ekle; All ve AllCodes güncelle | Mevcut PermissionCatalog |

### Infrastructure – Abstractions (Application’da interface)

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Application | Organization/Abstractions veya Common/Abstractions | IPositionRepository.cs | GetByIdAsync, ExistsByCodeAsync, AddAsync, (list için) | Domain.Organization |
| Backend.Veteriner.Application | Organization/Abstractions | IOrganizationUnitRepository.cs | GetByIdAsync, ExistsByCodeAsync, AddAsync, (list) | Domain.Organization |
| Backend.Veteriner.Application | Organization/Abstractions | IEmployeeRepository.cs | GetByIdAsync, AddAsync, (list) | Domain.Organization |

Not: Mevcut projede IRepository\<T\>, IReadRepository\<T\> (Ardalis) var; Auth tarafında özel repository interface kullanılıyor. Organization için: **Backend.Veteriner.Application/Common/Abstractions** altında IPositionRepository, IOrganizationUnitRepository, IEmployeeRepository tanımla. Listeler için handler içinde IReadRepository\<T\> kullanılabilir; Code unique kontrolü için ExistsByCodeAsync özel interface’te kalır.

### Infrastructure – Persistence

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Infrastructure | Persistence/Configurations | PositionConfiguration.cs | Position entity mapping; table, index Code unique | Domain.Organization |
| Backend.Veteriner.Infrastructure | Persistence/Configurations | OrganizationUnitConfiguration.cs | OrganizationUnit mapping; table, index Code unique, FK ParentId | Domain.Organization |
| Backend.Veteriner.Infrastructure | Persistence/Configurations | EmployeeConfiguration.cs | Employee mapping; table, FK OrganizationUnitId, PositionId, ManagerEmployeeId | Domain.Organization |
| Backend.Veteriner.Infrastructure | Persistence/Repositories/Positions | PositionRepository.cs | IPositionRepository impl; DbContext.Set<Position> | AppDbContext, Domain |
| Backend.Veteriner.Infrastructure | Persistence/Repositories/OrganizationUnits | OrganizationUnitRepository.cs | IOrganizationUnitRepository impl | AppDbContext, Domain |
| Backend.Veteriner.Infrastructure | Persistence/Repositories/Employees | EmployeeRepository.cs | IEmployeeRepository impl | AppDbContext, Domain |
| Backend.Veteriner.Infrastructure | Persistence | AppDbContext.cs | DbSet<Position>, DbSet<OrganizationUnit>, DbSet<Employee> ekle | Domain.Organization |
| Backend.Veteriner.Infrastructure | DependencyInjection.cs | - | IPositionRepository→PositionRepository, IOrganizationUnitRepository→OrganizationUnitRepository, IEmployeeRepository→EmployeeRepository | Repositories |

### API

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Api | Controllers | PositionsController.cs | [Route("api/v{version}/positions")]; GET (list), GET {id}, POST (create); Authorize(Policy = Organization.Positions.Read/Write) | MediatR, ResultExtensions, PermissionCatalog |
| Backend.Veteriner.Api | Controllers | OrganizationUnitsController.cs | [Route("api/v{version}/organization-units")]; GET, GET {id}, POST | Aynı |
| Backend.Veteriner.Api | Controllers | EmployeesController.cs | [Route("api/v{version}/employees")]; GET, GET {id}, POST | Aynı |

### Migration

| Katman | Klasör | Dosya | Amaç | Bağımlılık |
|--------|--------|-------|------|------------|
| Backend.Veteriner.Infrastructure | Migrations | xxx_Add_Organization_Positions_Units_Employees.cs | Positions, OrganizationUnits, Employees tabloları ve FK’lar | Configurations, AppDbContext |

---

## 7. Implementasyon sırası (özet)

1. **Domain** – Position.cs, OrganizationUnit.cs, Employee.cs (alanlar, constructor, domain metotları; AggregateRoot ilk fazda opsiyonel).
2. **Application DTOs** – PositionDto, PositionListItemDto, OrganizationUnitDto, OrganizationUnitListItemDto, EmployeeDto, EmployeeListItemDto.
3. **Application repository interface’leri** – IPositionRepository, IOrganizationUnitRepository, IEmployeeRepository (Application/Common/Abstractions).
4. **EF Configurations** – PositionConfiguration, OrganizationUnitConfiguration, EmployeeConfiguration; AppDbContext’e DbSet’ler.
5. **Infrastructure repositories** – PositionRepository, OrganizationUnitRepository, EmployeeRepository; DI kayıtları.
6. **Position use-case** – CreatePositionCommand + Handler, GetPositionsQuery + Handler, GetPositionByIdQuery + Handler, CreatePositionCommandValidator.
7. **OrganizationUnit use-case** – CreateOrganizationUnitCommand + Handler, GetOrganizationUnitsQuery + Handler, GetOrganizationUnitByIdQuery + Handler, CreateOrganizationUnitCommandValidator.
8. **Employee use-case** – CreateEmployeeCommand + Handler, GetEmployeesQuery + Handler, GetEmployeeByIdQuery + Handler, CreateEmployeeCommandValidator.
9. **PermissionCatalog** – Organization.Units.Read/Write, Organization.Employees.Read/Write, Organization.Positions.Read/Write ekle; All + AllCodes.
10. **API controllers** – PositionsController, OrganizationUnitsController, EmployeesController (GET list, GET by id, POST create); Authorize policy’leri.
11. **Migration** – Add_Organization_Positions_Units_Employees.
12. **Seed** – PermissionSeeder zaten catalog’dan okuyor; sadece catalog güncellenir, seed tekrar çalışınca yeni permission’lar eklenir. Gerekirse AdminClaimSeeder’da yeni permission’ları bir role atama (opsiyonel).

---

## 8. Audit planı

**Önerilen action isimleri (mevcut Permission.Create / User.Create ile uyumlu)**

- OrganizationUnit.Create, OrganizationUnit.Update, OrganizationUnit.Activate, OrganizationUnit.Deactivate  
  (Move, AssignManager birim için anlamlı değil; atlanır.)
- Employee.Create, Employee.Update, Employee.AssignUnit, Employee.ChangePosition, Employee.AssignManager, Employee.Activate, Employee.Deactivate  
- Position.Create, Position.Update, Position.Activate, Position.Deactivate  

**İlk fazda auditable olması gerekenler**
- OrganizationUnit.Create, Employee.Create, Position.Create (mutasyonlar audit’e alınsın). Update/Activate/Deactivate sonraki fazda IAuditableRequest eklenir.

**Target type / target id**
- TargetType: Command/Query sınıf adı (mevcut AuditBehavior davranışı) veya sabit "OrganizationUnit" / "Employee" / "Position".  
- TargetId: Oluşturulan/güncellenen entity Id (Create’te command’ta henüz yok; handler’dan sonra result.Value kullanılamaz pipeline’da – mevcut Permission.Create’te AuditTarget Code=… ile gidiyor). Öneri: Create’te AuditTarget = "Code=…" veya "Name=…"; Update’te "Id=…".

---

## 9. API planı (ilk faz)

### PositionsController
- **Route:** `api/v{version:apiVersion}/positions`
- **Endpoint’ler:**  
  - GET / – GetPositionsQuery (paged); Policy: Organization.Positions.Read  
  - GET /{id:guid} – GetPositionByIdQuery; Policy: Organization.Positions.Read  
  - POST / – CreatePositionCommand; Policy: Organization.Positions.Write; 201 Created + location
- **Response:** Result/ToActionResult; list PagedResult<PositionListItemDto>; getById Result<PositionDto>.

### OrganizationUnitsController
- **Route:** `api/v{version:apiVersion}/organization-units`
- **Endpoint’ler:**  
  - GET / – GetOrganizationUnitsQuery (paged); Policy: Organization.Units.Read  
  - GET /{id:guid} – GetOrganizationUnitByIdQuery; Policy: Organization.Units.Read  
  - POST / – CreateOrganizationUnitCommand; Policy: Organization.Units.Write; 201 Created
- **Response:** Aynı standard (Result, PagedResult, ToActionResult).

### EmployeesController
- **Route:** `api/v{version:apiVersion}/employees`
- **Endpoint’ler:**  
  - GET / – GetEmployeesQuery (paged); Policy: Organization.Employees.Read  
  - GET /{id:guid} – GetEmployeeByIdQuery; Policy: Organization.Employees.Read  
  - POST / – CreateEmployeeCommand; Policy: Organization.Employees.Write; 201 Created
- **Response:** Aynı standard.

---

## 10. Nihai aksiyon listesi (bugün ne yapılacak, sıra)

1. **Domain dosyalarını aç**  
   - Backend.Veteriner.Domain/Organization/Position.cs  
   - Backend.Veteriner.Domain/Organization/OrganizationUnit.cs  
   - Backend.Veteriner.Domain/Organization/Employee.cs  
   (İçerik: yukarıdaki alanlar, private parameterless ctor EF için, public constructor, Update/Activate/Deactivate ve Employee için AssignUnit, ChangePosition, AssignManager.)

2. **Application DTOs**  
   - Organization/Contracts/Dtos içinde PositionDto, PositionListItemDto, OrganizationUnitDto, OrganizationUnitListItemDto, EmployeeDto, EmployeeListItemDto.

3. **Application repository interface’leri**  
   - Common/Abstractions (veya Organization altında) IPositionRepository, IOrganizationUnitRepository, IEmployeeRepository; gerekli metotlar: GetByIdAsync, ExistsByCodeAsync (Position, OrganizationUnit), AddAsync, list için ya özel metot ya IReadRepository<Position> kullanımı (mevcut projede IReadRepository var mı kontrol et; varsa spec ile list alınabilir).

4. **EF Configurations + DbContext**  
   - PositionConfiguration, OrganizationUnitConfiguration, EmployeeConfiguration; AppDbContext’e DbSet’ler ekle.

5. **Infrastructure repositories**  
   - PositionRepository, OrganizationUnitRepository, EmployeeRepository implement et; DependencyInjection’da kaydet.

6. **Position CQRS**  
   - CreatePositionCommand + Handler, GetPositionsQuery + Handler, GetPositionByIdQuery + Handler, CreatePositionCommandValidator.

7. **OrganizationUnit CQRS**  
   - CreateOrganizationUnitCommand + Handler, GetOrganizationUnitsQuery + Handler, GetOrganizationUnitByIdQuery + Handler, CreateOrganizationUnitCommandValidator.

8. **Employee CQRS**  
   - CreateEmployeeCommand + Handler, GetEmployeesQuery + Handler, GetEmployeeByIdQuery + Handler, CreateEmployeeCommandValidator.

9. **PermissionCatalog güncelle**  
   - Organization.Units.Read, Organization.Units.Write, Organization.Employees.Read, Organization.Employees.Write, Organization.Positions.Read, Organization.Positions.Write ekle; All ve AllCodes’u güncelle.

10. **Controllers**  
    - PositionsController, OrganizationUnitsController, EmployeesController; route ve endpoint’ler yukarıdaki gibi; Authorize(Policy = …).

11. **Migration**  
    - `dotnet ef migrations add Add_Organization_Positions_Units_Employees -p Backend.Veteriner.Infrastructure -s Backend.Veteriner.Api`  
    - Gerekirse DbContext’e using Backend.Veteriner.Domain.Organization ekle.

12. **Smoke test**  
    - Uygulama ayağa kalkıyor mu; Permission seed (catalog güncellendiği için); GET positions/organization-units/employees list; POST create (Position, OrganizationUnit, Employee); GET by id.  
    - Audit: Create command’lar IAuditableRequest ile işaretlendi mi kontrol et; gerekirse ekle.

Bu sırayla ilerlediğinde Organization modülü ilk fazda mevcut mimari ve Result/audit/permission standardına tam uyumlu şekilde açılmış olur.
