namespace CorePlasma.Common.Responses;

public class ApiResponse
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyCollection<string>? Errors { get; init; }

    public static ApiResponse Ok(string message = "Success") => new() { Success = true, Message = message };

    public static ApiResponse Fail(string message, IReadOnlyCollection<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors ?? [] };
}

public sealed class ApiResponse<T> : ApiResponse
{
    public T? Data { get; init; }

    public static ApiResponse<T> Ok(T data, string message = "Success") =>
        new() { Success = true, Message = message, Data = data };
}
