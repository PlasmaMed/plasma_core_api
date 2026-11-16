using CorePlasma.API.Authorization;
using CorePlasma.BAL.DTOs.Integration;
using CorePlasma.BAL.Interfaces;
using CorePlasma.Common.Constants;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Interfaces;
using CorePlasma.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CorePlasma.API.Controllers;

[Route("api/integration/legacy")]
public sealed class IntegrationLegacyController(ILegacyEventIngestService service, IOptionsMonitor<LegacySyncSettings> settings) : ApiControllerBase
{
    /// <summary>Accept one committed customer/tank/engine/employee profile snapshot from the named legacy source.</summary>
    [HttpPost("events")]
    [Authorize(Policy = AuthorizationPolicies.ApiKey)]
    [RequestSizeLimit(1_048_576)]
    [ProducesResponseType(typeof(LegacyEventIngestResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ingest([FromBody] LegacyEventIngestDto dto, CancellationToken cancellationToken)
    {
        // A staged rollout may authorize one stream without enabling every
        // entity/source supported by the shared integration infrastructure.
        if (!settings.CurrentValue.Allows(dto.Source, dto.Entity))
            throw new ForbiddenException("This legacy source/entity is not enabled for ingestion.");
        return Ok(await service.IngestAsync(dto, cancellationToken));
    }
}

/// <summary>Explicit administrative mapping; the integration API key cannot bind arbitrary native records.</summary>
[Route("api/legacy-sync/mappings")]
[Authorize]
public sealed class LegacySyncMappingsController(
    ILegacyEventIngestService service,
    IPermissionService permissions,
    ICurrentUserService currentUser,
    IEngineService engines) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Bind([FromBody] LegacyMappingBindDto dto, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(dto.Entity, true, cancellationToken);
        await service.BindMappingAsync(dto, cancellationToken);
        return Success(new { dto.Source, dto.SourceSystem, dto.Entity, dto.EntityId, dto.CoreId });
    }

    [HttpGet("{entity}/{coreId:guid}")]
    public async Task<IActionResult> Identities(string entity, Guid coreId, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(entity, false, cancellationToken);
        // Honor the same machine row scope as the ordinary machine detail endpoint.
        if (entity == "engine") await engines.GetByIdAsync(coreId, cancellationToken);
        return Success(await service.GetIdentitiesAsync(entity, coreId, cancellationToken));
    }

    private async Task RequirePermissionAsync(string entity, bool importing, CancellationToken token)
    {
        var code = (entity, importing) switch
        {
            ("customer", true) => PermissionCodes.Customer.Import, ("customer", false) => PermissionCodes.Customer.View,
            ("tank", true) => PermissionCodes.Tank.Import, ("tank", false) => PermissionCodes.Tank.View,
            ("engine", true) => PermissionCodes.Engine.Import, ("engine", false) => PermissionCodes.Engine.View,
            ("user", true) => PermissionCodes.Employee.Import, ("user", false) => PermissionCodes.Employee.View,
            _ => throw new BusinessException("Invalid legacy entity.")
        };
        if (currentUser.EmployeeId is not { } employee || !await permissions.HasPermissionAsync(employee, code, token))
            throw new ForbiddenException("The corresponding module permission is required.");
    }
}
