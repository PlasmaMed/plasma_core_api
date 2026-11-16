# PROMPT KHỞI TẠO CORE BACKEND -- CENTRAL DATA MANAGEMENT

## 1. Vai trò

Bạn là **Senior .NET Backend Architect / Senior .NET Developer**.

Hãy thiết kế và khởi tạo một Backend Core dùng để **quản lý tập trung dữ
liệu doanh nghiệp**, có kiến trúc rõ ràng, dễ bảo trì, dễ mở rộng và có
thể phát triển thành hệ thống ERP/FWMS trong tương lai.

Không chỉ mô tả kiến trúc. Hãy **generate code hoàn chỉnh**, kiểm tra
dependency, namespace, package và đảm bảo solution có thể build.

------------------------------------------------------------------------

## 2. Công nghệ bắt buộc

Sử dụng:

-   .NET 10
-   ASP.NET Core Web API
-   C#
-   Entity Framework Core
-   EF Core Code First
-   SQL Server
-   Swagger / OpenAPI
-   Dependency Injection
-   AutoMapper
-   FluentValidation
-   JWT Authentication
-   ASP.NET Core Authorization
-   Serilog
-   Global Exception Handling
-   Repository Pattern
-   Unit Of Work
-   DTO Pattern
-   Async/Await
-   LINQ
-   Nullable Reference Types
-   CancellationToken

Không sử dụng Database First.

Database phải được tạo và cập nhật thông qua Entity Framework Migration.

------------------------------------------------------------------------

## 3. Cấu trúc Solution

Tạo solution:

``` text
CentralDataManagement.sln

src/
├── CentralDataManagement.API
├── CentralDataManagement.BAL
├── CentralDataManagement.DAL
└── CentralDataManagement.Common
```

### 3.1. CentralDataManagement.API

Chịu trách nhiệm:

-   Controllers
-   Middleware
-   Authentication
-   Authorization
-   Swagger
-   Dependency Injection
-   API configuration
-   appsettings.json
-   Program.cs

Không viết business logic trực tiếp trong Controller.

Luồng chuẩn:

``` text
Request
→ Validation
→ Authorization
→ BAL Service
→ DAL
→ Response
```

### 3.2. CentralDataManagement.BAL

Business Logic Layer.

Chứa:

``` text
BAL/
├── Interfaces/
├── Services/
├── DTOs/
├── Validators/
└── Mappings/
```

BAL chịu trách nhiệm:

-   Business Logic
-   Business Rules
-   DTO
-   Validation
-   Mapping
-   Gọi DAL thông qua Repository / Unit Of Work

BAL không truy cập trực tiếp database ngoài abstraction được DAL cung
cấp.

### 3.3. CentralDataManagement.DAL

Data Access Layer.

Chứa:

``` text
DAL/
├── Context/
├── Entities/
├── Configurations/
├── Interfaces/
├── Repositories/
├── UnitOfWork/
├── Seed/
└── Migrations/
```

Chịu trách nhiệm:

-   ApplicationDbContext
-   Entity
-   EF Core Configuration
-   Repository
-   Unit Of Work
-   Migration
-   Seed Data
-   Database transaction

### 3.4. CentralDataManagement.Common

Chứa:

``` text
Common/
├── Constants/
├── Enums/
├── Exceptions/
├── Extensions/
├── Helpers/
├── Models/
└── Responses/
```

Không chứa business logic hoặc code truy cập database.

------------------------------------------------------------------------

## 4. Project Dependency

Dependency phải tránh circular reference.

``` text
API
 ↓
BAL
 ↓
DAL
 ↓
Common
```

Cho phép:

``` text
API → BAL
API → Common

BAL → DAL
BAL → Common

DAL → Common
```

Không cho phép:

``` text
DAL → BAL
DAL → API
Common → DAL
Common → BAL
Common → API
```

------------------------------------------------------------------------

## 5. Quy ước Naming bắt buộc

Sử dụng thống nhất:

  Nghiệp vụ              Entity               Database Table
  ---------------------- -------------------- ---------------------
  Khách hàng             Customer             Customers
  Nhân viên              Employee             Employees
  Bình khí               Tank                 Tanks
  Máy / Máy Plasma       Engine               Engines
  Quyền                  Permission           Permissions
  Phân quyền nhân viên   EmployeePermission   EmployeePermissions
  Nhật ký phân quyền     PermissionAuditLog   PermissionAuditLogs

### QUAN TRỌNG

Bình khí bắt buộc sử dụng:

``` text
Tank
Tanks
```

Không sử dụng:

``` text
GasCylinder
GasCylinders
Cylinder
```

Máy bắt buộc sử dụng:

``` text
Engine
Engines
```

Không sử dụng:

``` text
Machine
Machines
PlasmaMachine
PlasmaMachines
```

Quy tắc này áp dụng cho:

-   Entity
-   DTO
-   Service
-   Interface
-   Controller
-   Repository
-   API Route
-   PermissionCode
-   Variable naming liên quan đến nghiệp vụ chính

------------------------------------------------------------------------

## 6. Base Entity

Tạo:

``` csharp
public abstract class BaseEntity
{
    public Guid Id { get; set; }

    public DateTime CreatedDate { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
}
```

Sử dụng Soft Delete.

Cấu hình Global Query Filter:

``` text
IsDeleted = false
```

Các truy vấn thông thường không được trả dữ liệu đã Soft Delete.

Audit fields phải được cập nhật tập trung thông qua `SaveChangesAsync`
hoặc EF Core Interceptor.

------------------------------------------------------------------------

# 7. MODULE CUSTOMER

Entity:

``` text
Customer
```

Table:

``` text
Customers
```

Các trường:

-   Id : Guid
-   CustomerCode : string
-   CustomerName : string
-   ShortName : string?
-   TaxCode : string?
-   Address : string?
-   Phone : string?
-   Email : string?
-   ContactPerson : string?
-   ContactPhone : string?
-   ContactEmail : string?
-   Website : string?
-   Description : string?
-   Status
-   CreatedDate
-   CreatedBy
-   UpdatedDate
-   UpdatedBy
-   IsDeleted

Yêu cầu:

-   CustomerCode Unique
-   Index các trường thường xuyên tìm kiếm
-   Validate Email

API:

``` http
GET    /api/customers
GET    /api/customers/{id}
POST   /api/customers
PUT    /api/customers/{id}
DELETE /api/customers/{id}
```

Danh sách hỗ trợ:

-   Pagination
-   Keyword Search
-   Filter
-   Sorting

------------------------------------------------------------------------

# 8. MODULE EMPLOYEE

Entity:

``` text
Employee
```

Table:

``` text
Employees
```

Các trường:

-   Id : Guid
-   EmployeeCode : string
-   FullName : string
-   DateOfBirth : DateTime?
-   Gender
-   Phone : string?
-   Email : string?
-   Address : string?
-   Department : string?
-   Position : string?
-   IdentityNumber : string?
-   JoinDate : DateTime?
-   ResignDate : DateTime?
-   Status
-   Description : string?
-   CreatedDate
-   CreatedBy
-   UpdatedDate
-   UpdatedBy
-   IsDeleted

Yêu cầu:

-   EmployeeCode Unique
-   Email đúng định dạng
-   Hỗ trợ filter Department, Position, Status

Navigation:

``` csharp
ICollection<EmployeePermission> EmployeePermissions
ICollection<Tank> AssignedTanks
ICollection<Engine> ResponsibleEngines
```

API:

``` http
GET    /api/employees
GET    /api/employees/{id}
POST   /api/employees
PUT    /api/employees/{id}
DELETE /api/employees/{id}
```

------------------------------------------------------------------------

# 9. MODULE TANK -- BÌNH KHÍ

Entity:

``` text
Tank
```

Table:

``` text
Tanks
```

Các trường:

-   Id : Guid
-   TankCode : string
-   SerialNumber : string?
-   TankType : string?
-   GasType : string?
-   Manufacturer : string?
-   ManufactureDate : DateTime?
-   InspectionDate : DateTime?
-   NextInspectionDate : DateTime?
-   Capacity : decimal?
-   WorkingPressure : decimal?
-   CurrentPressure : decimal?
-   Unit : string?
-   Status : TankStatus
-   CurrentLocation : string?
-   CustomerId : Guid?
-   AssignedEmployeeId : Guid?
-   Description : string?
-   CreatedDate
-   CreatedBy
-   UpdatedDate
-   UpdatedBy
-   IsDeleted

Yêu cầu:

-   TankCode Unique
-   SerialNumber có khả năng kiểm tra trùng
-   Decimal phải cấu hình Precision phù hợp
-   Foreign Key rõ ràng
-   Không Cascade Delete gây mất dữ liệu nghiệp vụ

GasType có thể hỗ trợ:

``` text
Oxygen
CO2
Argon
Nitrogen
LPG
Acetylene
MixedGas
Other
```

TankStatus:

``` text
Available
InUse
Empty
Maintenance
Inspection
Damaged
Lost
```

Quan hệ:

``` text
Customer 1 → N Tank
Employee 1 → N Tank
```

Navigation:

``` text
Tank.Customer
Tank.AssignedEmployee
Customer.Tanks
Employee.AssignedTanks
```

API:

``` http
GET    /api/tanks
GET    /api/tanks/{id}
POST   /api/tanks
PUT    /api/tanks/{id}
DELETE /api/tanks/{id}
```

Hỗ trợ search/filter:

-   TankCode
-   SerialNumber
-   GasType
-   Customer
-   Employee
-   CurrentLocation
-   Status

------------------------------------------------------------------------

# 10. MODULE ENGINE -- MÁY

Entity:

``` text
Engine
```

Table:

``` text
Engines
```

Engine dùng để quản lý Máy / Máy Plasma.

Các trường:

-   Id : Guid
-   EngineCode : string
-   EngineName : string
-   SerialNumber : string?
-   Model : string?
-   Manufacturer : string?
-   ManufactureYear : int?
-   PurchaseDate : DateTime?
-   InstallationDate : DateTime?
-   Power : decimal?
-   Voltage : decimal?
-   Current : decimal?
-   CuttingThickness : decimal?
-   CurrentLocation : string?
-   Department : string?
-   ResponsibleEmployeeId : Guid?
-   Status : EngineStatus
-   LastMaintenanceDate : DateTime?
-   NextMaintenanceDate : DateTime?
-   Description : string?
-   CreatedDate
-   CreatedBy
-   UpdatedDate
-   UpdatedBy
-   IsDeleted

EngineCode phải Unique.

EngineStatus:

``` text
Active
Inactive
Maintenance
Repair
Broken
Disposed
```

Quan hệ:

``` text
Employee 1 → N Engine
```

Navigation:

``` text
Engine.ResponsibleEmployee
Employee.ResponsibleEngines
```

API:

``` http
GET    /api/engines
GET    /api/engines/{id}
POST   /api/engines
PUT    /api/engines/{id}
DELETE /api/engines/{id}
```

Hỗ trợ:

-   Pagination
-   Search
-   Filter
-   Sorting

------------------------------------------------------------------------

# 11. HỆ THỐNG PHÂN QUYỀN DẠNG MA TRẬN

Phân quyền theo mô hình:

``` text
Employee N ↔ N Permission
```

Thông qua:

``` text
EmployeePermission
```

Yêu cầu:

-   1 Employee có nhiều Permission.
-   1 Permission có nhiều Employee.
-   Permission có thể gán trực tiếp cho Employee.
-   Có thể thêm/xóa nhiều quyền cùng lúc.
-   Có thể xem toàn bộ quyền của Employee.
-   Có thể xem Employee nào đang có một Permission.
-   Hỗ trợ giao diện ma trận Checkbox.
-   Thiết kế cho hàng trăm Permission và hàng nghìn Employee.

Không thiết kế Employee chỉ có một Role.

Không lưu Permission dạng chuỗi CSV trong Employee.

------------------------------------------------------------------------

## 12. Permission Entity

Entity:

``` text
Permission
```

Các trường:

-   Id : Guid
-   PermissionCode : string
-   PermissionName : string
-   ModuleCode : string
-   FunctionCode : string
-   ActionCode : string
-   Description : string?
-   SortOrder : int
-   IsActive : bool
-   CreatedDate
-   CreatedBy
-   UpdatedDate
-   UpdatedBy
-   IsDeleted

PermissionCode Unique.

Permission tổ chức:

``` text
Module
→ Function
→ Action
```

Action không giới hạn CRUD.

Hỗ trợ:

``` text
VIEW
CREATE
UPDATE
DELETE
APPROVE
REJECT
IMPORT
EXPORT
PRINT
ASSIGN
TRANSFER
LOCK
UNLOCK
MAINTENANCE
GRANT
REVOKE
```

------------------------------------------------------------------------

# 13. EmployeePermission

Entity trung gian:

``` text
EmployeePermission
```

Các trường:

-   EmployeeId : Guid
-   PermissionId : Guid
-   GrantedDate : DateTime
-   GrantedBy : Guid?
-   ExpiredDate : DateTime?
-   IsActive : bool

Composite Primary Key:

``` text
EmployeeId + PermissionId
```

Không cho phép gán trùng Permission cho Employee.

Quan hệ:

``` text
Employee
   N
   │
   │
EmployeePermission
   │
   │
   N
Permission
```

------------------------------------------------------------------------

# 14. Permission Codes

Tạo PermissionCode theo naming thống nhất.

Customer:

``` text
CUSTOMER_VIEW
CUSTOMER_CREATE
CUSTOMER_UPDATE
CUSTOMER_DELETE
CUSTOMER_IMPORT
CUSTOMER_EXPORT
```

Employee:

``` text
EMPLOYEE_VIEW
EMPLOYEE_CREATE
EMPLOYEE_UPDATE
EMPLOYEE_DELETE
EMPLOYEE_IMPORT
EMPLOYEE_EXPORT
```

Tank:

``` text
TANK_VIEW
TANK_CREATE
TANK_UPDATE
TANK_DELETE
TANK_IMPORT
TANK_EXPORT
TANK_ASSIGN
TANK_TRANSFER
```

Engine:

``` text
ENGINE_VIEW
ENGINE_CREATE
ENGINE_UPDATE
ENGINE_DELETE
ENGINE_IMPORT
ENGINE_EXPORT
ENGINE_ASSIGN
ENGINE_MAINTENANCE
```

Permission Management:

``` text
PERMISSION_VIEW
PERMISSION_GRANT
PERMISSION_REVOKE
```

PermissionCode có thể khai báo Constant trong Common để tránh sai chính
tả.

------------------------------------------------------------------------

# 15. Permission API

``` http
GET /api/permissions
```

Hỗ trợ filter:

``` text
moduleCode
functionCode
actionCode
keyword
```

Lấy quyền của Employee:

``` http
GET /api/employees/{employeeId}/permissions
```

Cập nhật toàn bộ quyền:

``` http
PUT /api/employees/{employeeId}/permissions
```

Request:

``` json
{
  "permissionIds": [
    "guid-1",
    "guid-2",
    "guid-3"
  ]
}
```

Backend phải:

1.  Kiểm tra Employee tồn tại.
2.  Kiểm tra Permission tồn tại.
3.  Lấy quyền hiện tại.
4.  Xác định quyền cần thêm.
5.  Xác định quyền cần thu hồi.
6.  Chỉ INSERT/DELETE dữ liệu thay đổi.
7.  Không xóa toàn bộ rồi insert lại nếu không cần.
8.  Sử dụng Transaction.
9.  Ghi Audit Log.
10. Invalidate Permission Cache.

Thêm quyền:

``` http
POST /api/employees/{employeeId}/permissions
```

Thu hồi quyền:

``` http
DELETE /api/employees/{employeeId}/permissions/{permissionId}
```

Danh sách Employee có quyền:

``` http
GET /api/permissions/{permissionId}/employees
```

------------------------------------------------------------------------

# 16. Permission Matrix API

Tạo:

``` http
GET /api/permission-matrix
```

Phục vụ màn hình:

  ----------------------------------------------------------------------------
  Nhân viên  Xem KH     Thêm KH    Sửa KH     Xem Tank   Sửa Tank   Xem Engine
  ---------- ---------- ---------- ---------- ---------- ---------- ----------
  NV001      ✓          ✓          ✓          ✓                     ✓

  NV002      ✓                                ✓          ✓          

  NV003                                       ✓                     ✓
  ----------------------------------------------------------------------------

Checked = Employee có Permission.

Unchecked = Employee không có Permission.

Hỗ trợ filter:

-   Employee
-   Department
-   Module
-   Function
-   Permission

Phải có Pagination.

Không tải toàn bộ Employee × Permission nếu dữ liệu lớn.

Bulk Update:

``` http
PUT /api/permission-matrix
```

Request:

``` json
{
  "changes": [
    {
      "employeeId": "guid",
      "permissionId": "guid",
      "granted": true
    },
    {
      "employeeId": "guid",
      "permissionId": "guid",
      "granted": false
    }
  ]
}
```

Toàn bộ batch update chạy trong Transaction.

------------------------------------------------------------------------

# 17. Authorization

API phải thực sự kiểm tra Permission.

Ví dụ:

``` text
GET    /api/customers       → CUSTOMER_VIEW
POST   /api/customers       → CUSTOMER_CREATE
PUT    /api/customers/{id}  → CUSTOMER_UPDATE
DELETE /api/customers/{id}  → CUSTOMER_DELETE

GET    /api/tanks           → TANK_VIEW
POST   /api/tanks           → TANK_CREATE
PUT    /api/tanks/{id}      → TANK_UPDATE

GET    /api/engines         → ENGINE_VIEW
POST   /api/engines         → ENGINE_CREATE
PUT    /api/engines/{id}    → ENGINE_UPDATE
```

Tạo Custom Authorization Attribute hoặc Policy:

``` csharp
[HasPermission("CUSTOMER_VIEW")]
[HttpGet]
public async Task<IActionResult> GetCustomers(...)
{
}
```

Không viết:

``` csharp
if (user.Permission == ...)
```

trong từng Controller.

Authorization phải xử lý tập trung.

------------------------------------------------------------------------

# 18. JWT & Permission Service

JWT dùng để xác định User/Employee đăng nhập.

Không đưa toàn bộ Permission vào JWT nếu số lượng quyền lớn.

Tạo:

``` text
IPermissionService
PermissionService
```

Các method:

``` csharp
Task<bool> HasPermissionAsync(
    Guid employeeId,
    string permissionCode,
    CancellationToken cancellationToken);

Task<HashSet<string>> GetPermissionsAsync(
    Guid employeeId,
    CancellationToken cancellationToken);
```

Authorization Handler gọi PermissionService.

Khi Admin thay đổi quyền, quyền mới phải có hiệu lực nhanh mà không bắt
buộc Employee đăng nhập lại.

------------------------------------------------------------------------

# 19. Permission Cache

Thiết kế Cache abstraction để có thể:

-   Ban đầu dùng IMemoryCache.
-   Sau này chuyển Redis.

Cache key:

``` text
permission:{employeeId}
```

Khi:

-   Grant Permission
-   Revoke Permission
-   Update Permission

phải invalidate cache Employee tương ứng.

------------------------------------------------------------------------

# 20. Super Admin

Hỗ trợ Super Admin có toàn bộ Permission.

Không hard-code EmployeeId.

Super Admin vẫn phải Audit Log.

Thiết kế sao cho có thể mở rộng Role trong tương lai, nhưng Role không
được làm mất khả năng cấp Permission trực tiếp cho Employee.

------------------------------------------------------------------------

# 21. Permission Audit Log

Entity:

``` text
PermissionAuditLog
```

Các trường:

-   Id
-   EmployeeId
-   PermissionId
-   Action
-   ChangedBy
-   ChangedDate
-   OldValue
-   NewValue
-   IpAddress
-   CorrelationId

Action:

``` text
GRANT
REVOKE
```

Phải trả lời được:

> Ai đã cấp/thu hồi quyền gì cho nhân viên nào, vào thời điểm nào?

------------------------------------------------------------------------

# 22. Repository Pattern

Tạo:

``` csharp
IRepository<T>
```

Các method tối thiểu:

``` csharp
Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);

Task AddAsync(T entity, CancellationToken cancellationToken = default);

void Update(T entity);

void Delete(T entity);

IQueryable<T> Query();
```

Không gọi SaveChanges trong từng Repository.

------------------------------------------------------------------------

# 23. Unit Of Work

Tạo:

``` text
IUnitOfWork
UnitOfWork
```

Cung cấp Repository cho:

``` text
Customers
Employees
Tanks
Engines
Permissions
EmployeePermissions
PermissionAuditLogs
```

Hỗ trợ:

``` text
SaveChangesAsync
BeginTransactionAsync
CommitTransactionAsync
RollbackTransactionAsync
```

------------------------------------------------------------------------

# 24. DTO

Không trả Entity trực tiếp từ Controller.

Customer:

``` text
CustomerDto
CustomerDetailDto
CreateCustomerDto
UpdateCustomerDto
CustomerFilterDto
```

Employee:

``` text
EmployeeDto
EmployeeDetailDto
CreateEmployeeDto
UpdateEmployeeDto
EmployeeFilterDto
```

Tank:

``` text
TankDto
TankDetailDto
CreateTankDto
UpdateTankDto
TankFilterDto
```

Engine:

``` text
EngineDto
EngineDetailDto
CreateEngineDto
UpdateEngineDto
EngineFilterDto
```

Permission:

``` text
PermissionDto
EmployeePermissionDto
UpdateEmployeePermissionsDto
PermissionMatrixDto
PermissionMatrixFilterDto
UpdatePermissionMatrixDto
```

Sử dụng AutoMapper.

------------------------------------------------------------------------

# 25. FluentValidation

Validate DTO bằng FluentValidation.

Ví dụ:

-   CustomerCode Required + MaxLength
-   CustomerName Required
-   EmployeeCode Required
-   EmployeeCode Unique
-   TankCode Required + Unique
-   EngineCode Required + Unique
-   PermissionCode Required + Unique
-   Email đúng định dạng
-   Guid phải hợp lệ
-   Các FK phải tồn tại nếu được cung cấp

Business validation phức tạp xử lý trong BAL.

------------------------------------------------------------------------

# 26. API Response

Tạo:

``` text
ApiResponse<T>
PagedResult<T>
```

Success:

``` json
{
  "success": true,
  "message": "Success",
  "data": {}
}
```

Error:

``` json
{
  "success": false,
  "message": "Resource not found",
  "errors": []
}
```

Pagination:

``` json
{
  "success": true,
  "data": {
    "items": [],
    "page": 1,
    "pageSize": 20,
    "totalItems": 100,
    "totalPages": 5
  }
}
```

------------------------------------------------------------------------

# 27. Global Exception Handling

Không lặp try/catch ở Controller.

Xử lý tập trung:

-   ValidationException
-   NotFoundException
-   BusinessException
-   UnauthorizedAccessException
-   DbUpdateException
-   Exception

HTTP Status:

``` text
400 Bad Request
401 Unauthorized
403 Forbidden
404 Not Found
409 Conflict
500 Internal Server Error
```

Không trả StackTrace ở Production.

------------------------------------------------------------------------

# 28. ApplicationDbContext

Tối thiểu:

``` csharp
DbSet<Customer> Customers
DbSet<Employee> Employees
DbSet<Tank> Tanks
DbSet<Engine> Engines
DbSet<Permission> Permissions
DbSet<EmployeePermission> EmployeePermissions
DbSet<PermissionAuditLog> PermissionAuditLogs
```

Sử dụng `IEntityTypeConfiguration<T>` để cấu hình Entity.

Không nhồi toàn bộ configuration vào `OnModelCreating`.

------------------------------------------------------------------------

# 29. Database

SQL Server + EF Core Code First.

Database tối thiểu:

``` text
Customers
Employees
Tanks
Engines
Permissions
EmployeePermissions
PermissionAuditLogs
```

Thiết lập đầy đủ:

-   Primary Key
-   Foreign Key
-   Composite Key
-   Unique Index
-   Search Index
-   MaxLength
-   Required
-   Decimal Precision
-   Delete Behavior
-   Global Query Filter

Không sử dụng Cascade Delete nếu có nguy cơ mất dữ liệu nghiệp vụ.

Tạo Migration:

``` text
InitialCreate
```

------------------------------------------------------------------------

# 30. Logging

Sử dụng Serilog.

Log:

-   Request
-   Error
-   Exception
-   Business Error
-   Database Error

Mỗi Request có CorrelationId.

Không log:

-   Password
-   JWT Token
-   Connection String
-   Sensitive Data

------------------------------------------------------------------------

# 31. Swagger / OpenAPI

Swagger phải:

-   Hiển thị toàn bộ API
-   JWT Bearer Authentication
-   Request/Response schema
-   HTTP Status Code
-   Cho phép test API

------------------------------------------------------------------------

# 32. Dependency Injection

Không nhồi đăng ký Service vào Program.cs.

Tạo extension:

``` csharp
services.AddDalServices(configuration);
services.AddBusinessServices();
services.AddApplicationServices();
```

Program.cs phải ngắn gọn.

------------------------------------------------------------------------

# 33. Code Quality

Áp dụng:

-   SOLID
-   Clean Code
-   Separation of Concerns
-   Dependency Injection
-   Async/Await
-   CancellationToken
-   Nullable Reference Types
-   RESTful API
-   EF Core Best Practices

Không:

-   SQL trực tiếp trong Controller
-   DbContext trong Controller
-   Business Logic trong Controller
-   Trả Entity trực tiếp
-   Duplicate Code
-   Hard-code Connection String
-   Hard-code JWT Secret
-   Hard-code EmployeeId Super Admin
-   Hard-code kiểm tra quyền trong Controller

------------------------------------------------------------------------

# 34. Khả năng mở rộng

Kiến trúc phải cho phép bổ sung:

-   Supplier
-   Material
-   Warehouse
-   Equipment
-   Project
-   Construction Site
-   Department
-   Position
-   Tank History
-   Tank Inspection
-   Tank Transfer
-   Engine Maintenance
-   Engine History
-   Location History
-   Attachment
-   Notification
-   Audit Log
-   User
-   Role
-   Redis Cache

Role có thể được bổ sung sau dưới dạng **nhóm/bộ Permission**, nhưng hệ
thống vẫn phải hỗ trợ Permission trực tiếp cho Employee.

------------------------------------------------------------------------

# 35. Yêu cầu Generate Code

Thực hiện theo thứ tự:

1.  Tạo Solution .NET 10.
2.  Tạo 4 Project API, BAL, DAL, Common.
3.  Thiết lập Project Reference.
4.  Cài NuGet packages.
5.  Tạo folder structure.
6.  Tạo BaseEntity.
7.  Tạo Enums.
8.  Tạo Entities.
9.  Tạo Entity Configurations.
10. Tạo ApplicationDbContext.
11. Tạo Repository.
12. Tạo Unit Of Work.
13. Tạo DTO.
14. Tạo AutoMapper Profiles.
15. Tạo FluentValidation Validators.
16. Tạo Customer Service.
17. Tạo Employee Service.
18. Tạo Tank Service.
19. Tạo Engine Service.
20. Tạo Permission Service.
21. Tạo Permission Matrix Service.
22. Tạo Authorization Handler / HasPermission.
23. Tạo Permission Cache.
24. Tạo Controllers.
25. Tạo Global Exception Handling.
26. Tạo ApiResponse/PagedResult.
27. Cấu hình JWT.
28. Cấu hình Swagger.
29. Cấu hình Serilog.
30. Cấu hình Dependency Injection.
31. Cấu hình SQL Server.
32. Tạo Seed Permission.
33. Tạo Initial Migration.
34. Viết README hướng dẫn chạy.
35. Restore và Build Solution.

------------------------------------------------------------------------

# 36. Kiểm tra bắt buộc

Sau mỗi phần phải kiểm tra:

-   Namespace
-   Project Reference
-   Circular Dependency
-   NuGet Package
-   Nullable Warning quan trọng
-   Compile Error
-   Naming Convention
-   Async/CancellationToken
-   Dependency Injection

Cuối cùng chạy:

``` bash
dotnet restore
dotnet build
```

Nếu có compile error:

1.  Phân tích lỗi.
2.  Sửa code.
3.  Build lại.
4.  Lặp lại đến khi build thành công.

Sau đó cung cấp lệnh Migration:

``` bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

------------------------------------------------------------------------

# 37. Kết quả cuối cùng

Mục tiêu cuối cùng phải có Backend Core .NET 10 chạy được, bao gồm:

``` text
Customer
Employee
Tank
Engine
Permission
EmployeePermission
PermissionAuditLog
```

Và đáp ứng:

-   EF Core Code First
-   SQL Server
-   CRUD
-   Pagination
-   Search
-   Filter
-   Sorting
-   Validation
-   JWT
-   Permission Matrix
-   Employee N:N Permission
-   Bulk Permission Update
-   Permission Cache
-   Audit Permission
-   Global Exception Handling
-   Swagger
-   Serilog
-   Repository
-   Unit Of Work
-   Clean Architecture theo 4 Project API/BAL/DAL/Common

### QUY TẮC CUỐI CÙNG

Không tự ý đổi:

``` text
Tank → GasCylinder/Cylinder
Engine → Machine/PlasmaMachine
```

Luôn sử dụng:

``` text
Customer
Employee
Tank
Engine
Permission
EmployeePermission
```

làm naming chuẩn của hệ thống.
