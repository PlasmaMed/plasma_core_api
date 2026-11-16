namespace CorePlasma.Common.Exceptions;

public sealed class ConflictException : AppException
{
    public ConflictException(string message, IReadOnlyCollection<string>? errors = null)
        : base(message, errors)
    {
    }

    public override int StatusCode => 409;
}
