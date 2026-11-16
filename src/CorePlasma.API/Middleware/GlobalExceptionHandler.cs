using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Responses;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace CorePlasma.API.Middleware;

/// <summary>
/// Maps exceptions to <see cref="ApiResponse"/> with the proper HTTP status. Controllers never try/catch.
/// Stack traces are only returned in Development.
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, response) = Map(exception);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception");
        }
        else if (exception is DbUpdateException)
        {
            logger.LogWarning(exception, "Database update conflict");
        }
        else
        {
            logger.LogWarning("Request failed with {StatusCode}: {Message}", statusCode, exception.Message);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);
        return true;
    }

    private (int StatusCode, ApiResponse Response) Map(Exception exception) => exception switch
    {
        ValidationException validation => (
            StatusCodes.Status400BadRequest,
            ApiResponse.Fail("Validation failed",
                validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}").ToList())),

        AppException app => (app.StatusCode, ApiResponse.Fail(app.Message, app.Errors)),

        UnauthorizedAccessException unauthorized => (
            StatusCodes.Status401Unauthorized,
            ApiResponse.Fail(string.IsNullOrWhiteSpace(unauthorized.Message) ? "Unauthorized" : unauthorized.Message)),

        DbUpdateConcurrencyException => (
            StatusCodes.Status409Conflict,
            ApiResponse.Fail("The record was modified by another user. Please reload and try again.")),

        DbUpdateException => (
            StatusCodes.Status409Conflict,
            ApiResponse.Fail("The operation conflicts with existing data (duplicate value or record in use).")),

        OperationCanceledException => (
            StatusCodes.Status499ClientClosedRequest,
            ApiResponse.Fail("The request was cancelled.")),

        _ => (
            StatusCodes.Status500InternalServerError,
            ApiResponse.Fail("An unexpected error occurred.",
                environment.IsDevelopment() ? [exception.ToString()] : null))
    };
}
