namespace CorePlasma.Common.Exceptions;

/// <summary>
/// Base class for expected application errors that map to a specific HTTP status.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message, IReadOnlyCollection<string>? errors = null)
        : base(message)
    {
        Errors = errors ?? [];
    }

    public abstract int StatusCode { get; }

    public IReadOnlyCollection<string> Errors { get; }
}
