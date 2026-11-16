# Core Production — SQL Server

Cập nhật ngày **09/11/2026**, theo các kết quả triển khai/đối soát gần nhất. Bản nhập tài khoản Quanly và sửa xóa vật lý đã hoạt động. Lượt đầu quét đủ **2.198 user**, tạo **1.038 tài khoản**, giữ **1.160 kết quả blocked**. Raw snapshot đã hoàn tất; native vẫn còn backfill và kết quả blocked/protected cần xử lý.

## Bản đang chạy

- Database nghiệp vụ: **SQL Server `core_plasma`, database ID 6**, server `113.160.132.61`; SQL chỉ bind loopback `127.0.0.1:1433`. Không có database `plasma_core` trên instance đã kiểm tra.
- API: **`core-plasma-api-quanly-accounts-deletefix-production`**, release `/home/plasma_admin/core-plasma-api-quanly-accounts-deletefix-20261109`, phục vụ `127.0.0.1:5081`.
- Archive SHA256: **`6970d8eaf84432800280254f0ed7911af2ff2c477c2eb18972096fb76cae892b`**.
- Cấu hình riêng: `/home/plasma_admin/core-plasma-api-quanly-accounts-deletefix-20261109/appsettings.Production.json`; giữ nguyên từng byte từ bản account trước, SHA256 **`ca2168158a530d2e8938f43537bfbe8b551e856f4834cb87b30428009f5854f5`**. Bản account đầu chỉ thêm `QuanlyAccountImport={Enabled:true,PollSeconds:60,MaxUsersPerPoll:250}` vào cấu hình final trước có hash `8b54f2c22c59ce2a4e994e0d3088c6a36198fcc592edf93b1a3c38024cf5d003`; deletefix không thêm hay sửa section nào. JWT/API key, SQL connection, integration và native policy không đổi. `ASPNETCORE_ENVIRONMENT=Production`, provider `SqlServer`, `ApplyMigrationsOnStartup=false`, `SeedOnStartup=false`.
- `https://api-core.plasma.vn/api` và `https://core.plasma.vn/api` cùng tới API SQL 5081. Web là `core-plasma-web-core-hub-production`, cổng `127.0.0.1:5082`.
- API/Web dùng restart policy `unless-stopped`; bản account trước `core-plasma-api-quanly-accounts-production` được giữ dừng, archive SHA256 `fd57534fd9bc455fd7f4dceee6cc5898a834ad28a67d95d0a10807c3a7bba52c`. Bản final và các container cũ cũng được giữ dừng. Hai publisher vẫn active/enabled, số restart 0; generation, tiến độ native, policy và ownership không bị reset bởi đợt triển khai tài khoản/deletefix.
- Tài khoản SQL API vẫn tên `core_plasma_api_test`, có `db_datareader`/`db_datawriter`, không có `db_owner`; tên principal không có nghĩa còn dùng database test.

Database đang hoạt động `core_plasma_api_test` trước đây đã đổi tên thành `core_plasma` để giữ dữ liệu mới, tài khoản, receipts, kiểu số đo và index. Snapshot `core_plasma` cũ được giữ là `core_plasma_snapshot_20261109_0625`. Không dựng lại hoặc xóa các dòng nghiệp vụ khi đổi tên.

## Raw snapshot và cập nhật liên tục

| Nguồn | Bảng đã kiểm chứng | Dòng snapshot đã kiểm chứng | Trạng thái |
|---|---:|---:|---|
| Quanly | 51 | 4.091.537 | `ready` |
| NEWAPP | 46 | 38.365.773 | `ready` |
| Tổng | **97** | **42.457.310** | Hai baseline hoàn tất |

Đây là số tại snapshot, không phải tổng dòng sống cố định. Hai publisher tiếp tục gửi thay đổi qua `https://core.plasma.vn/api/integration/source-sync/{source}`. Collector chỉ đọc khoảng **14:40 UTC** xác nhận `completed=true`, đúng deletefix active, cả hai PG HTTP-pending queue **0**, hai service active với **0 restart**. HTTPS sau deletefix lúc **14:37:16 UTC** đạt **17/17**, hai source unit active/raw ready, container đúng release đang chạy và exit code 0. Đây là trạng thái tại thời điểm quan sát, không cam kết backlog luôn bằng 0.

SQL raw role bị chặn truy cập `dbo`; API nghiệp vụ bị chặn đọc trực tiếp raw/transport chứa dữ liệu riêng. Toàn bộ 97 bảng nguồn được giữ riêng, gồm 11 bảng hạ tầng không xuất qua giao diện nghiệp vụ. UI hỗ trợ 45 nhóm materialize nhỏ và 41 nhóm operational chỉ đọc; bảng keyless giữ số lần xuất hiện của dòng trùng. Hoàn tất raw không đồng nghĩa mọi bảng thành màn hình chỉnh sửa hoặc native đã hội tụ.

NEWAPP vẫn có luồng máy/lịch sử trực tiếp tới SQL Core. PostgreSQL và `plasma-full-sync.service` tiếp tục mirror nguồn độc lập. API PostgreSQL cũ 8090, API SQL test riêng 5080 và cron relay PG → SQL test đã dừng; ledger được giữ đối soát. Không dùng marker `delivered_at` của mirror cũ làm ACK HTTP.

## Nhập tài khoản Quanly vào Core

Worker nội bộ `QuanlyAccountImport` đã bật, không có endpoint công khai riêng. Nó đọc coherent snapshot `users`/`customer_users` từ raw Quanly `ready`, chỉ tạo tài khoản mới khi đã xác minh được nhân viên/khách hàng Core tương ứng. Credential cũ được dùng qua adapter đăng nhập hiện hữu, không cần mật khẩu rõ; đăng nhập hợp lệ nâng cấp sang PBKDF2. Luồng raw/profile/business công khai vẫn loại trừ credential.

Đây là create-only đối với username, credential, liên kết cha và quyền. Không ghi đè hoặc tiếp quản tài khoản Core có sẵn, kể cả cùng chủ; xung đột được ghi blocked. Không sao chép ACL/role grants và không cấp SuperAdmin. Trạng thái parent nhân viên/khách hàng được giữ nguyên; các điều kiện kiểm tra parent khi đăng nhập giữ nguyên. Với tài khoản do ledger sở hữu, nguồn inactive/soft-delete hoặc xung đột xác minh sẽ làm tài khoản Active thành Inactive; không tự kích hoạt lại tài khoản bị khóa/xóa/tắt thủ công.

Deletefix đang chạy: user bị xóa vật lý chỉ được đối soát từ snapshot đầy đủ, authoritative và `ready`, cùng installation/generation đã kiểm tra trước/sau và sau khi lấy native transaction lock. Chỉ account vẫn được ledger xác minh sở hữu mới có thể chuyển Active → Inactive, ledger ghi `disabled/source_user_absent`. Baseline seeding/thiếu/chuyển generation không được dùng để suy diễn xóa. Giữ nguyên trạng thái khóa/tắt/xóa thủ công và bảo vệ SuperAdmin; không xóa profile/account Core, không đổi parent, không tự bật lại khi user nguồn xuất hiện trở lại.

Lượt quét đầu hoàn tất lúc **14:32:25 UTC: 2.198/2.198 user**. Tạo **1.038 account = 1.004 Customer + 34 Employee**. Trạng thái account mới: Customer **1.002 Active / 2 Inactive**, Employee **32 Active / 2 Inactive**; **0 SuperAdmin**. **344 parent khách hàng ở trạng thái Inactive/Deleted được giữ nguyên**, nên account Active không đồng nghĩa có thể đăng nhập/truy cập. Đối soát lượt đầu xác nhận **1.928 account Core có sẵn: 0 digest credential thay đổi**; account mới vẫn gắn đúng credential nguồn ban đầu.

**1.160 blocked** gồm **949 account Core đã có** (878 customer khác owner/lịch sử, 65 cùng owner, 6 staff), **81 va chạm username nguồn** (75 customer, 6 staff) và **130 reference khách hàng không hợp lệ/thiếu** (129 mapping không khả dụng, 1 quan hệ không hợp lệ). Không tự ghi đè hoặc cấp quyền để ép các kết quả này thành công.

Ledger `source_sync.QuanlyAccounts` đã được cài bằng DDL riêng, không thay đổi account có sẵn; account mới và ledger commit nguyên tử. Deletefix không cần DDL. Worker tiếp tục tối đa **250 định danh mỗi lượt, nghỉ 60 giây**, gồm user hiện có và định danh ledger-owned bị vắng trong snapshot. Quét liên tục vòng lại; một lượt đủ dữ liệu không đồng nghĩa mọi account được tạo hoặc mọi login truy cập được.

Canary tắt cả SourceSync, SourceProjection và QuanlyAccountImport. Bản deletefix đạt **86/86 SQL fixture**, gồm xóa vật lý customer có parent Active, race seeding/generation và `is_active` sai định dạng; **17/17 HTTPS Production lúc 14:37:16 UTC**. Kết quả account vẫn 2.198 đã quét / 1.038 mới, **0 digest thay đổi trong 1.928 account có sẵn**, 0 SuperAdmin mới và 344 parent inactive/deleted được giữ nguyên. Không chủ động thử xóa tài khoản trên Production; nhánh xóa vật lý được xác minh bằng fixture. Source service/raw/native baseline được giữ nguyên. Chi tiết account provenance cùng digest credential trước/sau chỉ lưu riêng trên server, không xuất vào workspace hay tài liệu này.

## Native: kết quả đã xác nhận và phần còn lại

`SourceProjection.Enabled=true`, policy `v2-baseline-create`; lần bật cấu hình lúc `2026-11-09T11:21:56Z` chỉ đổi `Enabled`/`PolicyVersion`. Các lần đổi mã sau đó giữ nguyên cấu hình native và điều kiện bảo vệ; bản account chỉ bổ sung section riêng, deletefix giữ nguyên byte toàn bộ cấu hình đó. Lựa chọn owner cho bản ghi dùng chung vẫn chưa được người dùng trả lời.

- Quanly: **14.938 bình applied**, không còn `missing_column_deleted_at`; **17.228 đơn hàng / 104 phiếu điều chuyển applied**. Còn **2.374 đơn / 2 phiếu** bị chặn reference.
- Bốn kết quả khách hàng chưa nhận diện trạng thái nguồn vẫn blocked. **522 kết quả protected** được giữ: **320 Quanly / 202 NEWAPP**; không tự ghi đè canonical.
- NEWAPP đã hoàn tất **lượt quét profile ban đầu lúc 13:31:46 UTC**. Kết quả khách hàng: **666 applied / 202 protected**; kết quả user/profile: **2.664 applied**, không đồng nghĩa tạo mới tài khoản đăng nhập. Bình: **4.463 = 363 applied / 4.090 xung đột định danh hoặc ownership / 10 assignment mơ hồ**. Đơn hàng: **11 applied / 10 thiếu reference**; điều chuyển: **0 applied / 2 thiếu reference**. Marker applied chỉ xác nhận quét xong, chưa phải mọi bản ghi native thành công.
- Hai baseline nghiệp vụ generic hoàn tất: **23.422 Quanly + 10.782 NEWAPP = 34.204 bản ghi** trên 45 bảng nghiệp vụ nhỏ được cấu hình.
- **5.077.819 dòng TankEngineHistory cũ** tiếp tục backfill có giới hạn. Mẫu lúc **13:41 UTC**: **7.834 applied / 8.196 thiếu reference**; đây là tiến độ một phần, chưa phải tổng kết hay hoàn tất. Nhóm 151 history từng trả 409 do định danh vẫn là vấn đề đối soát riêng.

History xử lý mặc định tối đa 1.000 dòng/poll, ngân sách mềm 15 giây và checkpoint hash khóa nguồn bền vững. `history_baseline_in_progress` chỉ là tiến độ. Receipt mới được xử lý độc lập với từng lát baseline. Compiled `AdapterVersion=v2-source-schema` cho phép quét lại checkpoint mà không replay hồ sơ applied/protected không đổi; không đổi policy chỉ để triển khai sửa schema.

## Actor repair và reference lịch sử

Plan **`20261109T125354317279Z`** đã áp dụng dưới worker lock, chỉ thay metadata projection có mục tiêu. Projector thực hiện cập nhật actor đủ bằng chứng; private audit xác nhận **8.679/8.679 FK khớp, 0 mismatch còn lại trong tập đã duyệt**. Không có thao tác operator trực tiếp cập nhật dbo FK, đổi config/policy hoặc gán owner mới.

Chẩn đoán chỉ đọc bằng 4 SELECT đơn giản + Python join hoàn tất khoảng 2 giây, `MAXDOP 1`, SQLCMD timeout 20 giây, lock timeout 2 giây. Kết quả xác nhận **131 ID khách hàng thiếu reference Core đều đã soft-delete ở nguồn**, không có target sẵn trong `plasma_migration.customer_map`; **519 ID bình vắng trong raw nguồn hiện tại**. Báo cáo có **659 nhóm reference chồng lặp**, không phải 659 đơn khác nhau. Không chạy lại truy vấn CTE mở rộng đã loại bỏ. Không tự hồi sinh dữ liệu đã xóa hoặc ghép identity bằng tên/serial.

## Note điều chuyển: lưu đủ dữ liệu nguồn

DAL final không giới hạn độ dài Note: SQL Server `nvarchar(max)`, PostgreSQL `text`; kiểm thử cả hai provider đạt. Giới hạn nhập tay ở DTO/UI giữ nguyên.

SQL thực tế đã chạy: [widen_source_transfer_note.sql](../.dev/integration/core-hub-20261109/widen_source_transfer_note.sql), SHA256 **`D1C6D1325F96D3AD09FDDBC8D922790EAADDF62D3531FC62F3B0FC0AA0C817F8`**. Bản này đã qua fixture trước Production, lấy collation hiện hữu từ catalog, whitelist chặt chữ/số/underscore rồi dùng identifier **không quote sau `COLLATE`**; không thay bằng bản nháp quoting khác.

DDL chỉ mở rộng cột: **103 dòng trước / 103 dòng sau**, độ dài lớn nhất khi đó **146 ký tự**, không đổi giá trị nguồn. Sau đó projector xử lý lại, Note đã kiểm chứng giữ đủ **1.042 ký tự**, khớp chính xác raw và `SourceDetails`; số phiếu Quanly applied là 104. Kết quả schema-only và kết quả projector là hai bước riêng.

## Kiểm chứng, backup và khôi phục

- Bản deletefix hiện hành đạt **86/86 SQL fixture / 17/17 HTTPS Production**, chốt lúc 14:37:16 UTC. Bản account trước đạt 65/65 SQL fixture; bản final trước đạt 78/78 SQL fixture native; đây là các bộ kiểm tra riêng. Ngoài ra có 37 kiểm tra lọc dữ liệu, 18 publisher, giao dịch HTTP 7.102 sự kiện có mất ACK/restart; đối chiếu toàn bộ giá trị 7.106 dòng fixture khớp, bao gồm keyless duplicates.
- Backup SQL cutover ở `/var/opt/mssql/backup/core-production-20261109-0625/` đã `RESTORE VERIFYONLY ... WITH CHECKSUM`. Trước hub có COPY_ONLY **6.209.797.120 byte** đã kiểm chứng CHECKSUM; cấu hình/evidence riêng ở `/home/plasma_admin/.local/share/core-source-hub-20261109/`.
- Backup cấu hình, crontab, ledger và trạng thái cutover ở `/home/plasma_admin/.local/share/core-sql-production-20261109-0625/`, quyền thư mục `700`.
- Đối soát cutover namespace `Source=NEWAPP` đã xác nhận 17.302 receipts PG sau revision 334995 có đủ trong SQL. Đây là bằng chứng lịch sử cutover, không phải tuyên bố native backfill hiện đã xong.
- Bằng chứng cutover: [deployment-result.json](../.dev/deploy/core-sql-production-20261109/deployment-result.json), [public-web-verification.json](../.dev/deploy/core-sql-production-20261109/public-web-verification.json). Vận hành hub: [runbook.md](../.dev/integration/core-hub-20261109/runbook.md).

HTTP fixture thuộc phạm vi kiểm thử đã được dừng sau khi xác minh ownership/scope. Báo cáo và archive đối soát chi tiết được giữ riêng trên server dưới `/home/plasma_admin/.local/share/core-source-hub-20261109/`; chưa xuất nội dung chi tiết về workspace. Việc chuyển các báo cáo chứa reconciliation identifiers/payload cần ủy quyền export rõ ràng. Tài liệu cục bộ chỉ ghi các số liệu vận hành tổng hợp.

Chẩn đoán native chỉ lấy aggregate theo source/installation/generation hiện hành và các mã lỗi an toàn. `_profile_baseline.State=applied` có thể chỉ xác nhận quét xong, vẫn còn unresolved records; marker/cursor không thay thế đối soát per-record. Không xuất payload, `SnapshotJson`, credentials hoặc thông tin cá nhân; tránh quét/đếm toàn bộ raw session/history trong lúc ingest/backfill.

Không trỏ API về PostgreSQL hoặc restore database trước khi đối soát các ghi mới và receipts SQL đã xác nhận sau cutover. Giữ nguyên ledger, generation và ownership; ưu tiên sửa trên SQL đang hoạt động. Mẫu `appsettings.Production.example.json` không chứa bí mật; khi triển khai mới phải cấp cấu hình riêng đúng SQL Server `core_plasma`, không chạy migration PostgreSQL trên SQL Server.

Rollback binary deletefix dùng container account trước được giữ dừng cùng cấu hình giống hệt; lưu ý bản đó chưa có đối soát xóa vật lý. Không xóa account đã tạo hoặc ledger để hoàn tác. Nếu cần dừng nhập mới, tắt riêng worker tài khoản theo quy trình thay đổi có kiểm soát, giữ nguyên raw/native pipeline và bằng chứng sở hữu.
