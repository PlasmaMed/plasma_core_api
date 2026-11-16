using CorePlasma.Common.Responses;
using Microsoft.AspNetCore.Mvc;

namespace CorePlasma.API.Controllers;

/// <summary>
/// Common conventions: JSON envelope, documented error status codes. No business logic in controllers.
/// </summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase : ControllerBase
{
    protected OkObjectResult Success<T>(T data, string message = "Success") => Ok(ApiResponse<T>.Ok(data, message));

    protected OkObjectResult Success(string message = "Success") => Ok(ApiResponse.Ok(message));

    protected CreatedAtActionResult Created<T>(string actionName, object routeValues, T data) =>
        CreatedAtAction(actionName, routeValues, ApiResponse<T>.Ok(data, "Created"));
}
