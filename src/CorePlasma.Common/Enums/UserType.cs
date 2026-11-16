namespace CorePlasma.Common.Enums;

/// <summary>
/// Kind of login account. Each type lives in its own table (EmployeeUsers / CustomerUsers).
/// </summary>
public enum UserType
{
    Employee = 1,
    Customer = 2
}
