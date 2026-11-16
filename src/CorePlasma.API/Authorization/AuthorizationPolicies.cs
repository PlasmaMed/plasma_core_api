namespace CorePlasma.API.Authorization;

public static class AuthorizationPolicies
{
    public const string ApiKey = "ApiKey";

    /// <summary>
    /// Caller signed in with a CustomerUsers account.
    /// </summary>
    public const string CustomerUser = "CustomerUser";
}
