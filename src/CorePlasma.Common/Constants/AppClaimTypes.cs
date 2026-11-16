namespace CorePlasma.Common.Constants;

public static class AppClaimTypes
{
    /// <summary>
    /// Id of the login account (EmployeeUsers.Id or CustomerUsers.Id).
    /// </summary>
    public const string UserId = "user_id";

    /// <summary>
    /// <see cref="Enums.UserType"/> name: Employee | Customer.
    /// </summary>
    public const string UserType = "user_type";

    public const string Username = "username";

    public const string FullName = "full_name";

    /// <summary>
    /// Present only for employee accounts.
    /// </summary>
    public const string EmployeeId = "employee_id";

    /// <summary>
    /// Present only for customer accounts.
    /// </summary>
    public const string CustomerId = "customer_id";
}
