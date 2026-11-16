namespace CorePlasma.Common.Exceptions;

public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message)
    {
    }

    public override int StatusCode => 403;
}
