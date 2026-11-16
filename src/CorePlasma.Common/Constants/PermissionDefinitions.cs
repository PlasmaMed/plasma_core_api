namespace CorePlasma.Common.Constants;

public sealed record PermissionDefinition(
    string PermissionCode,
    string PermissionName,
    string ModuleCode,
    string FunctionCode,
    string ActionCode,
    int SortOrder);

/// <summary>
/// Source of truth for seeded permissions. Add new modules here; the seeder inserts missing codes.
/// </summary>
public static class PermissionDefinitions
{
    public static readonly IReadOnlyList<PermissionDefinition> All = Build();

    private static List<PermissionDefinition> Build()
    {
        var list = new List<PermissionDefinition>();

        void Add(string module, string function, string moduleLabel, params (string Code, string Action, string Label)[] items)
        {
            foreach (var item in items)
            {
                list.Add(new PermissionDefinition(
                    item.Code,
                    $"{item.Label} {moduleLabel}",
                    module,
                    function,
                    item.Action,
                    list.Count + 1));
            }
        }

        Add(ModuleCodes.Customer, "CUSTOMER_MANAGEMENT", "khách hàng",
            (PermissionCodes.Customer.View, PermissionActions.View, "Xem"),
            (PermissionCodes.Customer.Create, PermissionActions.Create, "Thêm"),
            (PermissionCodes.Customer.Update, PermissionActions.Update, "Sửa"),
            (PermissionCodes.Customer.Delete, PermissionActions.Delete, "Xóa"),
            (PermissionCodes.Customer.Import, PermissionActions.Import, "Import"),
            (PermissionCodes.Customer.Export, PermissionActions.Export, "Export"));

        Add(ModuleCodes.Employee, "EMPLOYEE_MANAGEMENT", "nhân viên",
            (PermissionCodes.Employee.View, PermissionActions.View, "Xem"),
            (PermissionCodes.Employee.Create, PermissionActions.Create, "Thêm"),
            (PermissionCodes.Employee.Update, PermissionActions.Update, "Sửa"),
            (PermissionCodes.Employee.Delete, PermissionActions.Delete, "Xóa"),
            (PermissionCodes.Employee.Import, PermissionActions.Import, "Import"),
            (PermissionCodes.Employee.Export, PermissionActions.Export, "Export"));

        Add(ModuleCodes.Tank, "TANK_MANAGEMENT", "bình khí",
            (PermissionCodes.Tank.View, PermissionActions.View, "Xem"),
            (PermissionCodes.Tank.Create, PermissionActions.Create, "Thêm"),
            (PermissionCodes.Tank.Update, PermissionActions.Update, "Sửa"),
            (PermissionCodes.Tank.Delete, PermissionActions.Delete, "Xóa"),
            (PermissionCodes.Tank.Import, PermissionActions.Import, "Import"),
            (PermissionCodes.Tank.Export, PermissionActions.Export, "Export"),
            (PermissionCodes.Tank.Assign, PermissionActions.Assign, "Giao"),
            (PermissionCodes.Tank.Transfer, PermissionActions.Transfer, "Điều chuyển"),
            (PermissionCodes.Tank.TransferApprove, PermissionActions.Approve, "Duyệt phiếu điều chuyển"));

        Add(ModuleCodes.Engine, "ENGINE_MANAGEMENT", "máy",
            (PermissionCodes.Engine.View, PermissionActions.View, "Xem"),
            (PermissionCodes.Engine.Create, PermissionActions.Create, "Thêm"),
            (PermissionCodes.Engine.Update, PermissionActions.Update, "Sửa"),
            (PermissionCodes.Engine.Delete, PermissionActions.Delete, "Xóa"),
            (PermissionCodes.Engine.Import, PermissionActions.Import, "Import"),
            (PermissionCodes.Engine.Export, PermissionActions.Export, "Export"),
            (PermissionCodes.Engine.Assign, PermissionActions.Assign, "Giao"),
            (PermissionCodes.Engine.Maintenance, PermissionActions.Maintenance, "Bảo trì"),
            (PermissionCodes.Engine.ViewAll, PermissionActions.ViewAll, "Xem tất cả"));

        Add(ModuleCodes.Order, "ORDER_MANAGEMENT", "đơn hàng",
            (PermissionCodes.Order.View, PermissionActions.View, "Xem"),
            (PermissionCodes.Order.ViewAll, PermissionActions.ViewAll, "Xem tất cả"));

        Add(ModuleCodes.Permission, "PERMISSION_MANAGEMENT", "phân quyền",
            (PermissionCodes.Permission.View, PermissionActions.View, "Xem"),
            (PermissionCodes.Permission.Grant, PermissionActions.Grant, "Cấp"),
            (PermissionCodes.Permission.Revoke, PermissionActions.Revoke, "Thu hồi"));

        Add(ModuleCodes.EmployeeUser, "EMPLOYEE_USER_MANAGEMENT", "tài khoản nhân viên",
            (PermissionCodes.EmployeeUser.View, PermissionActions.View, "Xem"),
            (PermissionCodes.EmployeeUser.Create, PermissionActions.Create, "Thêm"),
            (PermissionCodes.EmployeeUser.Update, PermissionActions.Update, "Sửa"),
            (PermissionCodes.EmployeeUser.Delete, PermissionActions.Delete, "Xóa"),
            (PermissionCodes.EmployeeUser.Lock, PermissionActions.Lock, "Khóa"),
            (PermissionCodes.EmployeeUser.Unlock, PermissionActions.Unlock, "Mở khóa"));

        Add(ModuleCodes.CustomerUser, "CUSTOMER_USER_MANAGEMENT", "tài khoản khách hàng",
            (PermissionCodes.CustomerUser.View, PermissionActions.View, "Xem"),
            (PermissionCodes.CustomerUser.Create, PermissionActions.Create, "Thêm"),
            (PermissionCodes.CustomerUser.Update, PermissionActions.Update, "Sửa"),
            (PermissionCodes.CustomerUser.Delete, PermissionActions.Delete, "Xóa"),
            (PermissionCodes.CustomerUser.Lock, PermissionActions.Lock, "Khóa"),
            (PermissionCodes.CustomerUser.Unlock, PermissionActions.Unlock, "Mở khóa"));

        Add(ModuleCodes.System, "DOCUMENT_SEQUENCE", "cấu hình đánh số mã/phiếu",
            (PermissionCodes.DocumentSequence.View, PermissionActions.View, "Xem"),
            (PermissionCodes.DocumentSequence.Update, PermissionActions.Update, "Sửa"));

        Add(ModuleCodes.System, "AUDIT_LOG", "nhật ký thay đổi dữ liệu",
            (PermissionCodes.AuditLog.View, PermissionActions.View, "Xem"));

        foreach (var (code, label) in new[]
        {
            (PermissionCodes.Menu.Customers, "Quản lý khách hàng"),
            (PermissionCodes.Menu.Employees, "Quản lý User nhân viên"),
            (PermissionCodes.Menu.Engines, "Máy Plasma"),
            (PermissionCodes.Menu.Tanks, "Bình khí"),
            (PermissionCodes.Menu.Orders, "Đơn hàng"),
            (PermissionCodes.Menu.SourceBusiness, "Nghiệp vụ liên hệ thống"),
            (PermissionCodes.Menu.PermissionMenus, "Phân quyền menu"),
            (PermissionCodes.Menu.PermissionFunctions, "Phân quyền theo chức năng")
        })
        {
            Add(ModuleCodes.Menu, code, $"menu {label}", (code, PermissionActions.View, "Xem"));
        }

        Add(ModuleCodes.System, "SOURCE_BUSINESS", "nghiệp vụ liên hệ thống",
            (PermissionCodes.SourceBusiness.View, PermissionActions.View, "Xem"));
        return list;
    }
}
