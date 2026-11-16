namespace CorePlasma.Common.Constants;

public static class CacheKeys
{
    public static string EmployeePermissions(Guid employeeId) => $"permission:{employeeId}";
}
