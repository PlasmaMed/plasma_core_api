namespace CorePlasma.Common.Exceptions;

public sealed class BusinessException : AppException
{
    public BusinessException(string message, IReadOnlyCollection<string>? errors = null)
        : base(message, errors)
    {
    }

    public override int StatusCode => 400;
}
