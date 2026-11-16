# Core Plasma — Core Backend

Core backend (.NET 10) để quản lý tập trung dữ liệu doanh nghiệp: **Customer, Employee, Tank (bình khí), Engine (máy / máy Plasma)**, **phân quyền dạng ma trận** Employee N↔N Permission, và **2 loại tài khoản đăng nhập tách bảng**: nhân viên (`EmployeeUsers`) và khách hàng (`CustomerUsers`).

## Kiến trúc

```
src/
├── CorePlasma.API     Controllers, Middleware, JWT, Authorization (HasPermission), Swagger, Serilog, DI
├── CorePlasma.BAL     Services, DTOs, Validators (FluentValidation), Mappings (AutoMapper), Cache abstraction
├── CorePlasma.DAL     DbContext, Entities, Configurations, Repository, UnitOfWork, Interceptor, Seed, Migrations
└── CorePlasma.Common  Constants (PermissionCodes...), Enums, Exceptions, Extensions, Helpers, Models, Responses
```

Dependency: `API → BAL → DAL → Common` (API và BAL cũng tham chiếu Common). Không có tham chiếu ngược.

Luồng request: `CorrelationId → Request log → Exception handler → Authentication → Authorization (HasPermission → PermissionService + cache) → ValidationFilter (FluentValidation) → Controller → BAL Service → UnitOfWork/Repository → PostgreSQL`.

## Yêu cầu

- .NET SDK 10
- PostgreSQL (local, Docker hoặc dịch vụ PostgreSQL) với extension `unaccent`
- `dotnet-ef` tool: `dotnet tool update -g dotnet-ef`

Ví dụ chạy PostgreSQL bằng Docker (lấy `CORE_POSTGRES_PASSWORD` từ cấu hình bí mật):

```bash
docker run --name core-plasma-postgres \
  -e POSTGRES_DB=CorePlasma -e POSTGRES_USER=core_plasma \
  -e "POSTGRES_PASSWORD=${CORE_POSTGRES_PASSWORD}" \
  -p 5432:5432 -d postgres:18
```

## Cấu hình

Không commit secret. `appsettings.json` để trống các giá trị nhạy cảm; cấu hình qua **user-secrets** (dev) hoặc **biến môi trường** (server).

| Key | Mô tả |
|-----|-------|
| `ConnectionStrings:DefaultConnection` | Chuỗi kết nối PostgreSQL/Npgsql (Development có sẵn giá trị mẫu trong `appsettings.Development.json`) |
| `Jwt:Secret` | Khóa ký JWT, **≥ 32 byte**. Bắt buộc, app không khởi động nếu thiếu |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpirationMinutes` | Thông số token |
| `SuperAdmin:EmployeeCode` / `SuperAdmin:Username` / `SuperAdmin:Password` | Nhân viên + tài khoản Super Admin được seed lần đầu (xác định theo mã/username, không hard-code Id) |
| `LoginSecurity:MaxFailedAttempts` / `LockoutMinutes` | Khóa tạm tài khoản sau N lần sai mật khẩu (mặc định 5 lần / 15 phút) |
| `PermissionCache:ExpirationMinutes` | TTL cache quyền (mặc định 10 phút; luôn bị invalidate khi cấp/thu hồi) |
| `Database:ApplyMigrationsOnStartup` | Tự `Migrate()` khi khởi động (mặc định `false`) |
| `Database:SeedOnStartup` | Seed Permission + Super Admin khi khởi động (mặc định `true`, idempotent) |
| `Swagger:Enabled` | Bật Swagger ngoài môi trường Development |
| `Cors:AllowedOrigins` | Danh sách origin của frontend |
| `AUTOMAPPER_LICENSE_KEY` (env) | License AutoMapper 16 — bắt buộc khi chạy production |

Thiết lập cho máy dev:

```bash
cd src/CorePlasma.API
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)"
dotnet user-secrets set "SuperAdmin:Password" "<mật-khẩu-admin>"
# nếu PostgreSQL khác giá trị mẫu:
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=CorePlasma;Username=core_plasma;Password=<mật-khẩu-PostgreSQL>"
```

Trên server dùng biến môi trường: `Jwt__Secret`, `ConnectionStrings__DefaultConnection`, `SuperAdmin__Password`, ...

### Kết nối PLM ↔ Core bằng API key

Hai backend nhận header `X-API-Key` trên các route tích hợp riêng. Đặt cùng bí mật
`ApiKey__Key` ở Core và PLM, gồm 32–512 ký tự ASCII in được, không có dấu cách hoặc dấu phẩy; không đặt khóa trong frontend hay
appsettings được lưu cùng mã nguồn. Mặc định cấu hình công khai để trống, thiếu/sai
khóa trả `401`. Khóa trùng header, rỗng hoặc quá dài cũng bị từ chối.

| Cấu hình | Core | PLM |
|----------|------|-----|
| `ApiKey__Key` | cùng khóa bí mật | cùng khóa bí mật |
| `IntegrationPeer__BaseUrl` | URL gốc backend PLM | URL gốc backend Core |

Production dùng HTTPS. HTTP chỉ được chấp nhận trên loopback trong Development.
URL cấu hình không chứa username/password, query hay fragment. Client không tự theo
redirect; giới hạn tổng thời gian 15 giây và phản hồi 4 MiB. Đổi khóa ở cả hai backend
rồi khởi động lại ứng dụng để thay thế khóa cũ.

Địa chỉ API production: Core là `https://api-core.plasma.vn/`, PLM là
`https://api.plasmamed.com.vn/`; `core.plasma.vn` là giao diện người dùng.
Trên máy chủ triển khai hiện tại, `/etc/hosts` ánh xạ hai hostname API này tới
`127.0.0.1` để backend cùng máy gọi qua HTTPS nội bộ, tránh phản hồi `403` từ edge.
URL vẫn giữ đúng hostname và chứng chỉ TLS vẫn được xác minh bình thường.

Các endpoint sau có ở cả hai bên:

| GET | Ý nghĩa |
|-----|---------|
| `/api/integration/ping` | Xác nhận API key phía nhận; không kiểm tra database |
| `/api/integration/peer/ping` | Backend hiện tại gọi ping của backend còn lại |
| `/api/integration/engines`, `/tanks`, `/customers` | Đọc danh mục tại backend hiện tại |
| `/api/integration/peer/engines`, `/peer/tanks`, `/peer/customers` | Backend hiện tại đọc danh mục của backend còn lại |

Các đường dẫn viết tắt trong bảng đều bắt đầu bằng `/api/integration`. Danh sách
nhận `page` (1–1.000.000), `pageSize` (1–200), `keyword` (tối đa 200 ký tự); giá trị không hợp lệ trả `400`. Phản hồi thống nhất
`{ success, message, data: { items, page, pageSize, totalItems, totalPages } }`.
Core có thêm GET chi tiết `/api/integration/{engines|tanks|customers}/{id}` với UUID.
PLM cũng có GET chi tiết các route tương ứng nhưng dùng ID số của nguồn; không coi các ID hai hệ thống là cùng một khóa.
Máy/bình ở Core có `LegacyId` trong phản hồi chi tiết để đối chiếu theo nguồn.
Danh mục bình qua API key chưa bao gồm bổ sung số đo/cảnh báo khí theo quyền nhân viên.
Các GET danh mục không tự đồng bộ; lịch sử sử dụng máy có luồng đồng bộ nền riêng bên dưới.

API-key có thể đọc toàn bộ các danh mục trên; chỉ chia sẻ cho backend tin cậy.
Các route nghiệp vụ hiện có (`/api/engines`, `/api/tanks`...) tiếp tục yêu cầu JWT
và quyền nhân viên. API-key được ghi lịch sử tại `POST /api/integration/history/sessions`; không cấp quyền CRUD nội bộ hoặc quyền tài khoản người dùng.
Phản hồi peer lỗi được rút gọn: chưa cấu hình `503`, lỗi phía peer/kết quả không hợp
lệ `502`, quá thời gian `504`; không đưa khóa hay nội dung lỗi phía peer vào phản hồi.

Development: Core chạy `http://localhost:5206`, PLM chạy `http://localhost:5210`;
appsettings Development đã trỏ tới backend đối diện. PLM hiện yêu cầu runtime .NET Core
3.1 và tự migrate/seed khi chạy ứng dụng, nên chỉ dùng database thử nghiệm khi khởi động PLM.

Ví dụ kiểm tra (lấy `PLASMA_API_KEY` từ cấu hình bí mật của backend gọi):

```bash
curl https://api-core.plasma.vn/api/integration/peer/ping \
  -H "X-API-Key: ${PLASMA_API_KEY}"
curl 'https://api-core.plasma.vn/api/integration/peer/engines?page=1&pageSize=20' \
  -H "X-API-Key: ${PLASMA_API_KEY}"
```

Chỉ cấu hình Core trỏ tới URL PLM sau khi bản PLM có các route tích hợp mới đã được
triển khai. Không đưa khóa vào URL. Lời gọi từ máy khác vẫn đi qua gateway và cần
được phép truy cập các route tích hợp; ánh xạ `/etc/hosts` trên chỉ áp dụng cho
hai backend đang chạy cùng máy chủ.

Kiểm tra không dùng database:

```bash
dotnet run --project scripts/ApiKeySmokeTests --disable-build-servers
dotnet run --project scripts/IntegrationConnectionTests --disable-build-servers
```

### Lịch sử máy NEWAPP → Core ở luồng nền

Khi NEWAPP nhận lịch sử sử dụng máy, server lưu `Session` và `TankSession` như trước.
Trong cùng giao dịch nguồn, server lưu thêm một snapshot vào `CoreHistoryOutbox`.
API nguồn chỉ trả thành công sau khi cả lịch sử và snapshot đã commit; không chờ
HTTP tới Core. Worker riêng lấy các snapshot đã commit rồi POST bằng API key tới
`/api/integration/history/sessions` của Core. Retry, timeout hoặc restart không
làm mất snapshot; Core trả xác nhận sau khi dữ liệu đã commit.

Luồng này bao phủ `POST /Engine/createSession`,
`POST /Engine/{serial}/createSessions` và import log qua `EngineService.SaveSession`.
Nguồn chụp giá trị thật đã lưu, sau khi database cấp ID; không gửi lại request máy
chưa được chuẩn hóa và không chuyển múi giờ thêm lần nữa.

Mỗi event có `source=NEWAPP`, `eventId` riêng, `sourceRevision` là ID outbox tăng dần,
`sourceSessionId`, `engineSerial`, thời gian UTC và danh sách `tankSessions` chứa
`sourceTankSessionId`, RFID, serial/legacy ID bình khi biết và số đo nguồn.
Core dùng ID phiên/dòng bình nguồn để upsert lịch sử đã import, lưu receipt và revision
trong cùng giao dịch. Gửi lại không tạo trùng; event cũ không ghi đè event mới; cùng
EventId/revision với nội dung khác bị từ chối. Payload thiếu/bất hợp lệ không được
biến số đo thành 0 hay bỏ qua âm thầm.

Máy phải khớp duy nhất theo serial/mã đã có tại Core; thiếu hoặc mơ hồ trả `409`
và snapshot nguồn tiếp tục chờ để gửi lại sau khi sửa ánh xạ. Bình giữ nguyên RFID;
chỉ liên kết danh mục khi xác định duy nhất. Core chuyển nhãn số đo đúng như import
NEWAPP hiện có: `HighPressureAfter → PressureBefore`,
`HighPressureBefore → PressureAfter`, tương tự load cell, không đổi đơn vị.
Dữ liệu đồng hồ máy bất thường vẫn được giữ và hiển thị cảnh báo.

Core dùng thứ tự ID lịch sử nguồn để chọn số đo mới nhất, cùng các index cho truy vấn
này; một event cũ tới muộn không được coi là áp suất hiện tại chỉ vì vừa được chèn.
NEWAPP gửi `UserCode` đã lưu trong phiên vào snapshot để Core nhận đúng mã nguồn.
Khi nguồn gửi null, Core giữ nguyên `UserCode` đã import hoặc đồng bộ trước đó.
Đây là đồng bộ lịch sử sử dụng máy và dòng bình trong phiên; không tự gộp ID danh mục
PLM/Core hoặc sửa dữ liệu khác.

Để kích hoạt trên server:

1. Triển khai migration của Core trước khi bật nhận lịch sử; chỉ thêm revision,
   receipt và index. Index trên lịch sử lớn cần được triển khai trong thời gian phù hợp.
2. Triển khai migration outbox tại NEWAPP. Startup PLM hiện tự migrate; cần kiểm tra
   theo quy trình triển khai hiện có trước khi khởi động bản mới.
3. Đặt cùng `ApiKey__Key`; ở NEWAPP đặt `IntegrationPeer__BaseUrl` trỏ Core.
4. Bật `CoreHistorySync__Enabled=true` tại NEWAPP. Giá trị này chỉ bật worker;
   nguồn vẫn lưu outbox khi worker tắt để chờ gửi khi Core sẵn sàng.

Development NEWAPP bật worker trong cấu hình. Cấu hình công khai mặc định tắt worker
và không chứa khóa. Khi Core từ chối dữ liệu hoặc chưa có ánh xạ, outbox giữ payload
và mã lỗi để xử lý; không đánh dấu đã gửi nếu chưa có acknowledgement hợp lệ.
Không in payload hoặc khóa khi kiểm tra vận hành.

Kiểm thử luồng lịch sử bằng dữ liệu giả trên PostgreSQL local:

```bash
dotnet run --project scripts/HistorySyncTests --disable-build-servers
```

Bộ kiểm thử tạo database riêng, áp dụng migration chỉ trên database đó rồi xóa khi
hoàn tất; không chạy startup production hoặc migrate database hiện có.
`TEST_PG_CONNECTION` có thể cung cấp kết nối local cho tài khoản có quyền tạo database.
Chi tiết cấu hình nằm trong `scripts/HistorySyncTests/README.md`.

## Build, migration và chạy

```bash
dotnet restore
dotnet build

# Migration InitialCreate đã được tạo sẵn trong src/CorePlasma.DAL/Migrations
dotnet ef database update --project src/CorePlasma.DAL --startup-project src/CorePlasma.API

# Tạo migration mới khi thay đổi model
dotnet ef migrations add <Name> --project src/CorePlasma.DAL --startup-project src/CorePlasma.API --output-dir Migrations

dotnet run --project src/CorePlasma.API
```

Swagger (Development): `https://localhost:7157/swagger` hoặc `http://localhost:5206/swagger`.

1. `POST /api/auth/employee/login` với `{ "username": "admin", "password": "<SuperAdmin:Password>" }`
2. Copy `data.accessToken` → nút **Authorize** trong Swagger.

## API

| Method | Route | Permission |
|--------|-------|-----------|
| POST | `/api/auth/employee/login` | (anonymous) — tài khoản nhân viên |
| POST | `/api/auth/customer/login` | (anonymous) — tài khoản khách hàng |
| GET | `/api/auth/me` | đăng nhập (cả 2 loại) |
| PUT | `/api/auth/change-password` | đăng nhập (cả 2 loại, tự đổi mật khẩu) |
| GET/POST/PUT/DELETE | `/api/customers[/{id}]` | `CUSTOMER_VIEW/CREATE/UPDATE/DELETE` |
| GET/POST/PUT/DELETE | `/api/employees[/{id}]` | `EMPLOYEE_VIEW/CREATE/UPDATE/DELETE` |
| GET/POST/PUT/DELETE | `/api/employee-users[/{id}]` | `EMPLOYEE_USER_VIEW/CREATE/UPDATE/DELETE` |
| PUT | `/api/employee-users/{id}/password` | `EMPLOYEE_USER_UPDATE` |
| POST | `/api/employee-users/{id}/lock`, `/unlock` | `EMPLOYEE_USER_LOCK` / `EMPLOYEE_USER_UNLOCK` |
| GET/POST/PUT/DELETE | `/api/customer-users[/{id}]` (`customerId` filter) | `CUSTOMER_USER_VIEW/CREATE/UPDATE/DELETE` |
| PUT | `/api/customer-users/{id}/password` | `CUSTOMER_USER_UPDATE` |
| POST | `/api/customer-users/{id}/lock`, `/unlock` | `CUSTOMER_USER_LOCK` / `CUSTOMER_USER_UNLOCK` |
| GET | `/api/customer-portal/tanks[/{id}]` | chỉ tài khoản khách hàng, giới hạn trong khách hàng của mình |
| GET/POST/PUT/DELETE | `/api/tank-transfers[/{id}]` (phiếu điều chuyển bình) | xem: `TANK_VIEW`; tạo/sửa/xóa nháp: `TANK_TRANSFER` |
| POST | `/api/tank-transfers/{id}/submit`, `/cancel` | `TANK_TRANSFER` |
| POST | `/api/tank-transfers/{id}/approve`, `/reject` | `TANK_TRANSFER_APPROVE` |
| GET / PUT | `/api/document-sequences[/{id}]` (cấu hình đánh số) | `DOCUMENT_SEQUENCE_VIEW` / `DOCUMENT_SEQUENCE_UPDATE` |
| GET | `/api/data-change-logs` (`entityName`, `entityId`, `action`, `changedBy`, `fromDate`, `toDate`) | `AUDIT_LOG_VIEW` |
| GET/POST/PUT/DELETE | `/api/tanks[/{id}]` | `TANK_VIEW/CREATE/UPDATE/DELETE` |
| GET/POST/PUT/DELETE | `/api/engines[/{id}]` | `ENGINE_VIEW/CREATE/UPDATE/DELETE` |
| GET | `/api/sessions[/{id}]` (`engineId`, `serial`, `tankRfid`, `fromDate`, `toDate`) — lịch sử phiên máy kèm bình đã dùng | `ENGINE_VIEW` |
| POST | `/api/engines/import`, `/api/tanks/import` (multipart `file`, Excel xuất từ PlasmaMED) | `ENGINE_IMPORT` / `TANK_IMPORT` |
| GET | `/api/permissions` (`moduleCode`, `functionCode`, `actionCode`, `keyword`) | `PERMISSION_VIEW` |
| GET | `/api/permissions/{permissionId}/employees` | `PERMISSION_VIEW` |
| GET | `/api/permissions/audit-logs` | `PERMISSION_VIEW` |
| GET | `/api/employees/{employeeId}/permissions` | `PERMISSION_VIEW` |
| PUT | `/api/employees/{employeeId}/permissions` (thay toàn bộ) | `PERMISSION_GRANT` + `PERMISSION_REVOKE` |
| POST | `/api/employees/{employeeId}/permissions` (thêm) | `PERMISSION_GRANT` |
| DELETE | `/api/employees/{employeeId}/permissions/{permissionId}` | `PERMISSION_REVOKE` |
| GET | `/api/permission-matrix` | `PERMISSION_VIEW` |
| PUT | `/api/permission-matrix` (bulk) | `PERMISSION_GRANT` + `PERMISSION_REVOKE` |

Danh sách hỗ trợ `page`, `pageSize` (tối đa 200), `keyword`, `sortBy`, `sortDirection` (`Asc`/`Desc`) và các filter riêng của từng module. Tìm `keyword` không phân biệt hoa/thường và không dấu (PostgreSQL `ILIKE` + `unaccent`, xem `SearchPattern`). Máy lọc thêm `engineType`, `tankType`, `store`; bình lọc thêm `tankType`, `store`.

Định dạng response:

```json
{ "success": true, "message": "Success", "data": { } }
{ "success": false, "message": "Validation failed", "errors": ["CustomerCode: ..."] }
```

HTTP status: 400 (validation/business), 401, 403, 404, 409 (trùng dữ liệu / DbUpdateException), 500 (không trả stack trace ngoài Development).

## Tài khoản người dùng (2 bảng riêng)

| | Nhân viên — `EmployeeUsers` | Khách hàng — `CustomerUsers` |
|---|---|---|
| Liên kết | 1 `Employee` ↔ 0..1 tài khoản (unique `EmployeeId`) | 1 `Customer` → N tài khoản (nhiều người liên hệ) |
| Đăng nhập | `POST /api/auth/employee/login` | `POST /api/auth/customer/login` |
| Quyền | Ma trận Permission của Employee; có cờ `IsSuperAdmin` | Không dùng ma trận quyền; chỉ vào `/api/customer-portal/*`, dữ liệu luôn lọc theo `CustomerId` của chính họ |
| Quản lý | `/api/employee-users` | `/api/customer-users` |

- `Employee` / `Customer` là dữ liệu nghiệp vụ; mật khẩu, trạng thái tài khoản, khóa tạm, lần đăng nhập cuối nằm ở bảng tài khoản. Hai bảng dùng chung interface `IUserAccount` nên logic đăng nhập/khóa chỉ viết một lần (`UserAccountGuard`).
- Username unique trong từng bảng; vì hai endpoint đăng nhập tách riêng nên cùng một username có thể tồn tại ở cả hai bảng.
- JWT có claim `user_type` (`Employee`/`Customer`), `user_id`, và `employee_id` hoặc `customer_id`. Token khách hàng gọi API nội bộ → 403; token nhân viên gọi customer-portal → 403.
- Khóa/vô hiệu hóa tài khoản có hiệu lực ngay, không chờ JWT hết hạn: tài khoản nhân viên mất quyền (cache bị invalidate), tài khoản khách hàng bị kiểm tra trạng thái ở mỗi request vào portal.
- Xóa Employee/Customer sẽ xóa mềm luôn tài khoản đi kèm. Không thể khóa/xóa tài khoản Super Admin hoặc chính mình.
- Cột audit `CreatedBy/UpdatedBy`: `EmployeeId` khi người thao tác là nhân viên, `CustomerUsers.Id` khi là khách hàng.

## Phân quyền

- **Phân quyền menu** dùng chung cơ chế quyền: module `MENU` (`MENU_CUSTOMERS`, `MENU_EMPLOYEES`, `MENU_ENGINES`, `MENU_TANKS`, `MENU_PERMISSION_MENUS`, `MENU_PERMISSION_FUNCTIONS`). Frontend ẩn/hiện menu và chặn route theo các mã này (`GET /api/auth/me` trả danh sách quyền); quyền thao tác dữ liệu vẫn do các quyền chức năng quyết định.
- **Employee N↔N Permission** qua bảng `EmployeePermissions` (PK `EmployeeId + PermissionId`, có `ExpiredDate`, `IsActive`).
- Permission tổ chức theo `ModuleCode → FunctionCode → ActionCode`; mã quyền là hằng số trong `Common/Constants/PermissionCodes.cs`. Thêm quyền mới vào `PermissionDefinitions.cs` — seeder tự insert mã còn thiếu.
- `[HasPermission(PermissionCodes.Customer.View)]` trên action; `PermissionAuthorizationHandler` gọi `IPermissionService.HasPermissionAsync` — không có `if` kiểm tra quyền trong controller. Mọi endpoint khác mặc định yêu cầu đăng nhập.
- JWT chỉ chứa danh tính (employee id/code/name), **không chứa danh sách quyền**. Quyền được đọc qua cache `permission:{employeeId}` (`ICacheService`, mặc định `IMemoryCache`, thay Redis bằng cách implement `ICacheService`). Cấp/thu hồi quyền, đổi trạng thái hoặc xóa nhân viên sẽ invalidate cache → có hiệu lực ngay, không cần đăng nhập lại.
- Cập nhật quyền (`PUT` employee permissions, `PUT` matrix): kiểm tra Employee/Permission tồn tại → lấy quyền hiện tại → tính delta → chỉ INSERT/DELETE phần thay đổi → transaction → ghi `PermissionAuditLogs` (ai, cấp/thu hồi quyền gì, cho ai, khi nào, IP, CorrelationId, giá trị cũ/mới) → invalidate cache.
- **Super Admin**: cờ `EmployeeUsers.IsSuperAdmin`, gán qua cấu hình `SuperAdmin:EmployeeCode` / `SuperAdmin:Username` (không hard-code Id); có mọi quyền, mọi thay đổi quyền do Super Admin thực hiện vẫn ghi audit log, truy cập bằng quyền Super Admin được log.
- Mở rộng Role sau này: thêm `Roles`, `RolePermissions`, `EmployeeRoles` và hợp quyền Role với quyền trực tiếp trong `PermissionService.LoadEntryAsync` — quyền cấp trực tiếp vẫn giữ nguyên.

## Hàm dùng chung

Module mới **không viết lại** phân trang, lọc, kiểm tra trùng, sinh mã, quy trình phiếu — chỉ kế thừa.

| Thành phần | Vị trí | Dùng khi |
|---|---|---|
| `CrudServiceBase<...>` | `BAL/Services/Common` | Danh mục (Supplier, Warehouse...): khai báo `Repository`, `SortableFields`, `ApplyFilter`; override hook `Before/AfterCreate`, `Before/AfterUpdate`, `Before/AfterDelete`; `EnsureUniqueAsync(...)` kiểm tra trùng |
| `AutoCodeOptions` | `BAL/Services/Common` | Mã tự sinh khi để trống: `protected override AutoCodeOptions<Supplier> AutoCode => new(SequenceCodes.Supplier, x => x.SupplierCode);` |
| `VoucherBase` + `VoucherBaseConfiguration<T>` | `DAL/Entities`, `DAL/Configurations` | Phiếu mới: kế thừa để có sẵn VoucherNo, VoucherDate, Status, người/ngày gửi-duyệt-từ chối-hủy |
| `VoucherServiceBase<...>` | `BAL/Services/Common` | Phiếu mới: khai báo `SequenceCode`, `ApplyVoucherFilter`, `OnApprovingAsync` (tác động khi duyệt). Có sẵn số phiếu tự sinh, Nháp → Gửi duyệt → Duyệt/Từ chối, Hủy, chặn sửa/xóa sai trạng thái, transaction |
| `VoucherDto` / `VoucherDetailDto` / `VoucherWriteDto` / `VoucherFilterDto` / `VoucherWriteValidator<T>` | `BAL/DTOs/Common`, `BAL/Validators` | DTO/validator phần đầu phiếu dùng chung |
| `IDocumentNumberService` | `BAL/Interfaces` | Sinh số bất kỳ: `GenerateAsync("TANK_TRANSFER")`, `PreviewAsync`, `GenerateUniqueAsync` |
| `NumberToWordsHelper.ToVietnameseWords` | `Common/Helpers` | In phiếu: 1250000 → "Một triệu hai trăm năm mươi nghìn đồng" |
| `DateTimeHelper` | `Common/Helpers` | Giờ Việt Nam: `VietnamNow`, `VietnamToday`, `ToVietnamTime`, `VietnamDayRangeUtc` |
| `VietnameseTextHelper` | `Common/Helpers` | `RemoveDiacritics`, `NormalizeForSearch`, `ToSlug` |
| `QueryableExtensions` / `ExpressionExtensions` | `Common/Extensions` | `WhereIf`, `ApplySorting`, `AndAlso`, `OrElse` |

**Đánh số (`DocumentSequences`)**: mỗi loại mã/phiếu một dòng — tiền tố, dấu phân cách, phần ngày, số chữ số, chu kỳ reset (không / năm / tháng / ngày). Mặc định: `KH00001`, `NV00001`, `TK000001`, `EN00001`, `PDC-202610-0001`. Tăng số bằng một câu PostgreSQL `UPDATE ... RETURNING` có khóa dòng nên không trùng khi nhiều người tạo cùng lúc; số phiếu sinh trong transaction của phiếu nên không bị nhảy số khi lưu lỗi. Thêm loại mới: thêm vào `SequenceCodes` + `SequenceDefinitions` (seeder tự tạo dòng).

**Thêm một phiếu mới** (ví dụ phiếu bảo trì máy): entity kế thừa `VoucherBase` → configuration kế thừa `VoucherBaseConfiguration<T>` → DTO kế thừa `Voucher*Dto` → service kế thừa `VoucherServiceBase` và viết `OnApprovingAsync` → controller → migration. Tham khảo `TankTransfer*`.

## Ghi chú thiết kế

- **Nhật ký thay đổi dữ liệu** (`DataChangeLogs`): `DataChangeLogInterceptor` tự ghi mọi thêm/sửa/xóa của mọi bảng (trừ chính bảng log) trong cùng transaction với dữ liệu — tên bảng, khóa, hành động `Created`/`Updated`/`Deleted`, các trường thay đổi dạng JSON `{ "Field": { "old", "new" } }`, người thao tác, IP, CorrelationId. `PasswordHash` được che `***`. Sửa mà không đổi giá trị nào thì không ghi. Bộ đếm `DocumentSequences` tăng bằng SQL trực tiếp nên không có log riêng (mã sinh ra đã nằm trong log của bản ghi được tạo).
- **Import máy / bình khí** (`EquipmentImportService`): đọc file Excel xuất từ hệ thống PlasmaMED (tìm sheet có cột `serial` + `equipmentIdx`/`tankIdx` + `_id`, bỏ qua các dòng tiêu đề). `serial` → mã + serial, `_id` → `LegacyId` (khóa để import lại: khớp theo `LegacyId`, rồi theo mã), ngày giờ UTC+7 → UTC, `[null]`/`[không có trường]` → null, giữ `createdAt` gốc. Trạng thái nguồn: máy `READY_TO_USE/LENT/RENTED/SOLD/CHECKING/DISABLED` → `ReadyToUse/Lent/Rented/Sold/Checking/Inactive`; bình `READY_TO_USE/AT_CLIENT/LOADING/EMPTY/LOCKED/DISABLED` → `Available/AtClient/Loading/Empty/Locked/Inactive`. Có dòng lỗi thì không lưu gì và trả danh sách lỗi theo số dòng. Cột `clientUserId` (ID khách hàng ở hệ thống cũ) được bỏ qua, không lưu; `invoiceId` lưu dạng ID gốc (`LegacyInvoiceId`) vì hóa đơn chưa được import.
- **Phiên sử dụng máy**: `Sessions` (máy `EngineId`, `StartTime`, `EndTime` – null khi phiên đang chạy) lưu giá trị UTC. Không ràng buộc `EndTime >= StartTime`: lịch sử giữ nguyên đồng hồ nguồn, kể cả ngày bất thường hoặc kết thúc trước bắt đầu; API trả `TimeIssue` để hiển thị cảnh báo. `TankSessions` giữ RFID và áp suất/cân nặng trước–sau; `SessionId` là FK tới phiên, `TankId` là FK nullable tới `Tanks`, chỉ gán khi ánh xạ xác định duy nhất. `TankRfid` giữ mã nguồn và không phải FK. Tra cứu: `TankSession.SessionId → Session.Id → Session.EngineId → Engine.Id`; mã máy là `Engine.SerialNumber`.
- Không xóa cứng: xóa Customer, Employee, Tank, Engine, EmployeeUser, CustomerUser chỉ đặt `IsDeleted = true` và `Status = Deleted` (không gán được trạng thái này qua API thêm/sửa).
- Soft delete: `BaseEntity.IsDeleted` + global query filter; `AuditableEntityInterceptor` tự điền `CreatedDate/CreatedBy/UpdatedDate/UpdatedBy` và đổi `Delete` thành soft delete. Unique index mã danh mục có filter PostgreSQL `"IsDeleted" = false` nên mã đã xóa có thể tái sử dụng; unique index ID nguồn của lịch sử vẫn bao gồm bản ghi đã xóa.
- FK dùng `DeleteBehavior.Restrict`; xóa Customer/Employee bị chặn nếu còn Tank/Engine liên kết.
- Enum lưu dạng chuỗi trong DB và JSON.
- Mật khẩu băm PBKDF2-SHA256; mật khẩu mới phải ≥ 8 ký tự, có chữ hoa, chữ thường và số.
- Log: Serilog (Console + file `logs/`), mỗi request có `X-Correlation-ID`; không log header Authorization, query string, body hay connection string.
