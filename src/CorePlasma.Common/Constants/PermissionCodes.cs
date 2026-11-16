namespace CorePlasma.Common.Constants;

/// <summary>
/// Permission codes used across the system. Format: {MODULE}_{ACTION}.
/// Always reference these constants instead of string literals.
/// </summary>
public static class PermissionCodes
{
    public static class SourceBusiness
    {
        public const string View = "SOURCE_BUSINESS_VIEW";
    }
    public static class Customer
    {
        public const string View = "CUSTOMER_VIEW";
        public const string Create = "CUSTOMER_CREATE";
        public const string Update = "CUSTOMER_UPDATE";
        public const string Delete = "CUSTOMER_DELETE";
        public const string Import = "CUSTOMER_IMPORT";
        public const string Export = "CUSTOMER_EXPORT";
    }

    public static class Employee
    {
        public const string View = "EMPLOYEE_VIEW";
        public const string Create = "EMPLOYEE_CREATE";
        public const string Update = "EMPLOYEE_UPDATE";
        public const string Delete = "EMPLOYEE_DELETE";
        public const string Import = "EMPLOYEE_IMPORT";
        public const string Export = "EMPLOYEE_EXPORT";
    }

    public static class Tank
    {
        public const string View = "TANK_VIEW";
        public const string Create = "TANK_CREATE";
        public const string Update = "TANK_UPDATE";
        public const string Delete = "TANK_DELETE";
        public const string Import = "TANK_IMPORT";
        public const string Export = "TANK_EXPORT";
        public const string Assign = "TANK_ASSIGN";
        public const string Transfer = "TANK_TRANSFER";
        public const string TransferApprove = "TANK_TRANSFER_APPROVE";
    }

    public static class Engine
    {
        public const string View = "ENGINE_VIEW";

        /// <summary>See every machine. Without it an employee only sees machines where he is the responsible employee.</summary>
        public const string ViewAll = "ENGINE_VIEW_ALL";
        public const string Create = "ENGINE_CREATE";
        public const string Update = "ENGINE_UPDATE";
        public const string Delete = "ENGINE_DELETE";
        public const string Import = "ENGINE_IMPORT";
        public const string Export = "ENGINE_EXPORT";
        public const string Assign = "ENGINE_ASSIGN";
        public const string Maintenance = "ENGINE_MAINTENANCE";
    }

    public static class Order
    {
        public const string View = "ORDER_VIEW";

        /// <summary>See every order. Without it an employee only sees the orders he created or delivers.</summary>
        public const string ViewAll = "ORDER_VIEW_ALL";
    }

    public static class Permission
    {
        public const string View = "PERMISSION_VIEW";
        public const string Grant = "PERMISSION_GRANT";
        public const string Revoke = "PERMISSION_REVOKE";
    }

    /// <summary>
    /// Login accounts of employees (table EmployeeUsers).
    /// </summary>
    public static class EmployeeUser
    {
        public const string View = "EMPLOYEE_USER_VIEW";
        public const string Create = "EMPLOYEE_USER_CREATE";
        public const string Update = "EMPLOYEE_USER_UPDATE";
        public const string Delete = "EMPLOYEE_USER_DELETE";
        public const string Lock = "EMPLOYEE_USER_LOCK";
        public const string Unlock = "EMPLOYEE_USER_UNLOCK";
    }

    /// <summary>
    /// Login accounts of customers (table CustomerUsers).
    /// </summary>
    public static class CustomerUser
    {
        public const string View = "CUSTOMER_USER_VIEW";
        public const string Create = "CUSTOMER_USER_CREATE";
        public const string Update = "CUSTOMER_USER_UPDATE";
        public const string Delete = "CUSTOMER_USER_DELETE";
        public const string Lock = "CUSTOMER_USER_LOCK";
        public const string Unlock = "CUSTOMER_USER_UNLOCK";
    }

    /// <summary>
    /// Numbering configuration of codes and vouchers (table DocumentSequences).
    /// </summary>
    public static class DocumentSequence
    {
        public const string View = "DOCUMENT_SEQUENCE_VIEW";
        public const string Update = "DOCUMENT_SEQUENCE_UPDATE";
    }

    /// <summary>
    /// Automatic data change history (table DataChangeLogs).
    /// </summary>
    public static class AuditLog
    {
        public const string View = "AUDIT_LOG_VIEW";
    }

    /// <summary>
    /// Menu visibility in the web app sidebar (MENU module). Data access is still governed by the
    /// function permissions above.
    /// </summary>
    public static class Menu
    {
        public const string Customers = "MENU_CUSTOMERS";
        public const string Employees = "MENU_EMPLOYEES";
        public const string Engines = "MENU_ENGINES";
        public const string Tanks = "MENU_TANKS";
        public const string Orders = "MENU_ORDERS";
        public const string SourceBusiness = "MENU_SOURCE_BUSINESS";
        public const string PermissionMenus = "MENU_PERMISSION_MENUS";
        public const string PermissionFunctions = "MENU_PERMISSION_FUNCTIONS";
    }
}
