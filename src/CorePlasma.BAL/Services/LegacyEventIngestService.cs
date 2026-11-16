using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CorePlasma.BAL.DTOs.Integration;
using CorePlasma.BAL.Interfaces;
using CorePlasma.BAL.Services.Common;
using CorePlasma.Common.Enums;
using CorePlasma.Common.Exceptions;
using CorePlasma.Common.Models;
using CorePlasma.DAL.Context;
using CorePlasma.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CorePlasma.BAL.Services;

/// <summary>Apply only legacy-owned fields, with receipts and identity mappings in the same transaction.</summary>
public sealed class LegacyEventIngestService(ApplicationDbContext context, IPermissionService permissions,
    IOptionsMonitor<LegacySyncSettings> settings) : ILegacyEventIngestService
{
    // The short transaction lock also protects first-time legacy/serial matching and administrative binds.
    // Source and Core application writes remain separate; native uniqueness constraints still apply.
    private static readonly long IngestLock = BinaryPrimitives.ReadInt64BigEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes("CorePlasma:legacy-ingest:v1")));

    public async Task<LegacyEventIngestResultDto> IngestAsync(LegacyEventIngestDto dto, CancellationToken cancellationToken)
    {
        return await IngestWithInvalidationAsync(dto, false, cancellationToken);
    }

    public Task<LegacyEventIngestResultDto> IngestProjectionAsync(LegacyEventIngestDto dto, CancellationToken cancellationToken)
    {
        if (dto.Source is not ("plasma-bv" or "newapp") || dto.Source == "newapp" && dto.Entity == "engine" ||
            !settings.CurrentValue.Allows(dto.Source, dto.Entity))
            throw new ConflictException("This source/entity is not owned by the enabled raw projection bridge.");
        return IngestWithInvalidationAsync(dto, true, cancellationToken);
    }

    private async Task<LegacyEventIngestResultDto> IngestWithInvalidationAsync(LegacyEventIngestDto dto, bool projection, CancellationToken cancellationToken)
    {
        var result = await IngestTransactionAsync(dto, projection, cancellationToken);
        if (dto.Entity == "user")
        {
            // Invalidate only after commit; retrying a lost ack also retries cache invalidation.
            var employeeId = await context.LegacySyncReceipts.AsNoTracking().Where(x => x.EventId == dto.EventId)
                .Select(x => x.CoreId).SingleAsync(cancellationToken);
            if (employeeId is not null) await permissions.InvalidateCacheAsync([employeeId.Value], cancellationToken);
        }
        return result;
    }

    private async Task<LegacyEventIngestResultDto> IngestTransactionAsync(LegacyEventIngestDto dto, bool projection, CancellationToken cancellationToken)
    {
        ValidateIdentity(dto.Source, dto.Entity, dto.EntityId);
        if (!projection && dto.Source is "raw-newapp" or "raw-plasma-bv")
            throw new BusinessException("The raw projection receipt namespace is reserved for the internal bridge.");
        var sourceSystem = ResolveSourceSystem(dto.Source, dto.SourceSystem);
        if (dto.EventId == Guid.Empty || !projection && dto.Sequence <= 0 || dto.OccurredAt == default || dto.OccurredAt.Offset != TimeSpan.Zero ||
            dto.Operation is not ("create" or "update" or "delete") || dto.Data.ValueKind != JsonValueKind.Object ||
            !dto.Data.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var dataId) || dataId != dto.EntityId)
            throw new BusinessException("Invalid legacy event envelope or snapshot identity.");
        if (dto.CoreId == Guid.Empty) throw new BusinessException("Invalid optional native Core ID.");
        if (dto.Entity == "user") ValidateUserSnapshot(dto.Data, dto.Operation == "delete");
        var receiptSource = projection ? "raw-" + dto.Source : dto.Source;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await TransactionAdvisoryLock.AcquireAsync(context.Database, IngestLock, cancellationToken);
            await ValidateSourceSystemAsync(dto.Source, sourceSystem, cancellationToken);
            var receipt = await context.LegacySyncReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.EventId == dto.EventId, cancellationToken);
            if (receipt is not null)
            {
                if (projection) dto.Sequence = receipt.Sequence;
                var retryHash = PayloadHash(dto);
                var upgradedLegacyRetry = dto.Source == "plasma-bv" && sourceSystem == "quanly.plasma.vn" &&
                    receipt.SourceSystem == sourceSystem && receipt.PayloadHash == PayloadHash(dto, omitSourceSystem: true);
                if (receipt.Source != receiptSource || receipt.PayloadHash != retryHash && !upgradedLegacyRetry)
                    throw new ConflictException("The legacy event ID has already been used for different content.");
                await transaction.CommitAsync(cancellationToken);
                return Result(dto, projection && receipt.Status == "protected" ? "protected" : "duplicate");
            }
            if (projection)
            {
                // Allocate under the SAME lock/transaction as the mapping and receipt. Raw event IDs
                // are never native revisions; NEWAPP's direct engine outbox retains its own counter.
                var rawMax = await context.LegacySyncReceipts.Where(x => x.Source == receiptSource)
                    .MaxAsync(x => (long?)x.Sequence, cancellationToken) ?? 0;
                var nativeMax = await context.LegacySyncReceipts.Where(x => x.Source == dto.Source)
                    .MaxAsync(x => (long?)x.Sequence, cancellationToken) ?? 0;
                var mappingMax = await context.LegacySyncMappings.Where(x => x.Source == dto.Source)
                    .MaxAsync(x => (long?)x.LastSequence, cancellationToken) ?? 0;
                dto.Sequence = checked(Math.Max(Math.Max(rawMax, nativeMax), mappingMax) + 1);
            }
            var hash = PayloadHash(dto);
            if (await context.LegacySyncReceipts.AnyAsync(x => x.Source == receiptSource && x.Sequence == dto.Sequence, cancellationToken))
                throw new ConflictException("The legacy source sequence has already been used for a different event.");

            var mapping = await context.LegacySyncMappings.SingleOrDefaultAsync(
                x => x.Source == dto.Source && x.Entity == dto.Entity && x.EntityId == dto.EntityId, cancellationToken);
            if (mapping is not null && mapping.SourceSystem != sourceSystem)
                throw new ConflictException("The source namespace is already bound to another source system.");
            if (dto.CoreId is not null && mapping?.CoreId != dto.CoreId)
                throw new ConflictException("The supplied Core ID does not match the verified source identity.");
            var mappedCoreId = mapping?.CoreId;
            if (await context.LegacySyncMappings.AnyAsync(x => x.SourceSystem == sourceSystem && x.Entity == dto.Entity &&
                x.EntityId == dto.EntityId && x.Source != dto.Source && x.CoreId != mappedCoreId, cancellationToken))
                throw new ConflictException("This system ID belongs to a different source namespace; verify its mapping first.");
            var status = mapping is not null && dto.Sequence <= mapping.LastSequence ? "stale" : "applied";
            if (status == "applied")
            {
                var deleting = dto.Operation == "delete" ||
                    (dto.Data.TryGetProperty("deleted_at", out var deletedAt) && deletedAt.ValueKind != JsonValueKind.Null);
                var protectedCustomer = projection && dto.Entity == "customer" && mapping?.CoreId is not null &&
                    (mapping.PreserveCanonical || await context.LegacySyncMappings.CountAsync(x =>
                        x.Entity == "customer" && x.CoreId == mapping.CoreId, cancellationToken) > 1);
                var preserveNative = protectedCustomer || await PreserveAlternateProjectionAsync(dto, mapping, cancellationToken);
                // Raw transport cannot choose a winner for merged customer aliases. Retain its
                // revision/snapshot, but report the protected native fields explicitly.
                if (projection && preserveNative) status = "protected";
                var coreId = preserveNative ? mapping!.CoreId : dto.Entity switch
                {
                    "customer" => await ApplyCustomerAsync(dto, mapping, deleting, cancellationToken),
                    "tank" => await ApplyTankAsync(dto, mapping, deleting, cancellationToken),
                    "engine" => await ApplyEngineAsync(dto, mapping, deleting, cancellationToken),
                    "user" => await ApplyEmployeeAsync(dto, mapping, deleting, cancellationToken),
                    _ => throw new BusinessException("Invalid legacy entity.")
                };
                if (coreId is not null) await SetSourceIdAsync(dto.Entity, coreId.Value, sourceSystem, dto.EntityId, cancellationToken);
                if (mapping is null)
                {
                    mapping = new LegacySyncMapping { Source = dto.Source, SourceSystem = sourceSystem, Entity = dto.Entity, EntityId = dto.EntityId, CoreId = coreId };
                    context.LegacySyncMappings.Add(mapping);
                }
                mapping.CoreId = coreId;
                mapping.LastSequence = dto.Sequence;
                mapping.SnapshotJson = dto.Data.GetRawText();
                mapping.IsDeleted = deleting;
                mapping.UpdatedAt = DateTime.UtcNow;
            }
            context.LegacySyncReceipts.Add(new LegacySyncReceipt
            {
                EventId = dto.EventId, Source = receiptSource, SourceSystem = sourceSystem, Sequence = dto.Sequence,
                Entity = dto.Entity, EntityId = dto.EntityId, CoreId = mapping!.CoreId,
                Operation = dto.Operation, PayloadHash = hash, Status = status,
                SnapshotJson = dto.Data.GetRawText(), OccurredAt = dto.OccurredAt.UtcDateTime, ReceivedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(dto, status);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task BindMappingAsync(LegacyMappingBindDto dto, CancellationToken cancellationToken)
    {
        ValidateIdentity(dto.Source, dto.Entity, dto.EntityId);
        var sourceSystem = ResolveSourceSystem(dto.Source, dto.SourceSystem);
        if (dto.CoreId == Guid.Empty) throw new BusinessException("A native Core ID is required.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await TransactionAdvisoryLock.AcquireAsync(context.Database, IngestLock, cancellationToken);
            await ValidateSourceSystemAsync(dto.Source, sourceSystem, cancellationToken);
            var record = await FindNativeAsync(dto.Entity, dto.CoreId, cancellationToken);
            if (record is null || record.IsDeleted) throw new ConflictException("The native Core record does not exist or is deleted.");
            var mapping = await context.LegacySyncMappings.SingleOrDefaultAsync(
                x => x.Source == dto.Source && x.Entity == dto.Entity && x.EntityId == dto.EntityId, cancellationToken);
            if (mapping is not null && ((mapping.CoreId is not null && mapping.CoreId != dto.CoreId) || mapping.SourceSystem != sourceSystem))
                throw new ConflictException("This source identity is already bound; existing mappings cannot be reassigned.");
            if (await context.LegacySyncMappings.AnyAsync(x => x.SourceSystem == sourceSystem && x.Entity == dto.Entity &&
                x.EntityId == dto.EntityId && x.Source != dto.Source && x.CoreId != dto.CoreId, cancellationToken))
                throw new ConflictException("This system ID already identifies another Core record.");
            if (mapping?.CoreId is null)
            {
                if (dto.Entity != "customer" && await context.LegacySyncMappings.AnyAsync(x =>
                    x.Source == dto.Source && x.Entity == dto.Entity && x.CoreId == dto.CoreId, cancellationToken))
                    throw new ConflictException("This native record already has an identity in the same source namespace.");
                var aliases = dto.Entity == "customer" ? await context.LegacySyncMappings.Where(x =>
                    x.Entity == "customer" && x.CoreId == dto.CoreId).ToListAsync(cancellationToken) : [];
                foreach (var alias in aliases) alias.PreserveCanonical = true;
                if (mapping is null)
                {
                    mapping = new LegacySyncMapping
                    { Source = dto.Source, SourceSystem = sourceSystem, Entity = dto.Entity, EntityId = dto.EntityId };
                    context.LegacySyncMappings.Add(mapping);
                }
                // Explicit administration may resolve a null target; committed source revisions/snapshots are retained.
                mapping.CoreId = dto.CoreId;
                mapping.PreserveCanonical |= aliases.Count > 0;
                mapping.UpdatedAt = DateTime.UtcNow;
                await SetSourceIdAsync(dto.Entity, dto.CoreId, sourceSystem, dto.EntityId, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<IReadOnlyList<LegacySourceIdentityDto>> GetIdentitiesAsync(string entity, Guid coreId, CancellationToken token)
    {
        ValidateIdentity("read", entity, 1);
        var record = await FindNativeAsync(entity, coreId, token);
        if (record is null || record.IsDeleted) throw new NotFoundException("Core record", coreId);
        var aliases = await context.LegacySyncMappings.AsNoTracking().Where(x => x.Entity == entity && x.CoreId == coreId)
            .OrderBy(x => x.SourceSystem).ThenBy(x => x.Source).ThenBy(x => x.EntityId)
            .Select(x => new { x.Source, x.SourceSystem, x.EntityId, x.IsDeleted, x.SnapshotJson }).ToListAsync(token);
        return aliases.Select(alias =>
        {
            var status = entity == "tank" ? SourceTankStatusPresentation.FromSnapshot(alias.SourceSystem, alias.SnapshotJson) : default;
            return new LegacySourceIdentityDto(alias.Source, alias.SourceSystem, alias.EntityId, alias.IsDeleted, status.Code, status.Label);
        }).ToArray();
    }

    private async Task<Guid?> ApplyCustomerAsync(LegacyEventIngestDto dto, LegacySyncMapping? mapping, bool deleting, CancellationToken token)
    {
        if (mapping?.CoreId is null && deleting) return null;
        Customer? customer = mapping?.CoreId is not null ? await context.Customers.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == mapping.CoreId, token) : null;
        if (mapping?.CoreId is not null && customer is null) throw new ConflictException("The mapped Core customer is missing.");
        if (customer is null)
        {
            if ((dto.Operation != "create" && !IsHistoricalCustomerTombstone(dto, mapping)) || deleting)
                throw new ConflictException("Bind this existing source customer to its verified native Core customer before retrying.");
            customer = new Customer { CustomerCode = SourceCode(dto, "C"), CreatedDate = DateTime.UtcNow };
            context.Customers.Add(customer);
        }
        var aliases = await context.LegacySyncMappings.AsNoTracking().Where(x =>
            x.Entity == "customer" && x.CoreId == customer.Id).ToListAsync(token);
        var canonical = mapping?.PreserveCanonical == true || aliases.Count > 1;
        if (!deleting && customer.IsDeleted && (canonical || mapping is { IsDeleted: false }))
            throw new ConflictException("The mapped customer was deleted in Core; resolve its canonical mapping before retrying.");
        if (deleting)
        {
            // A source alias is not the canonical survivor. Its tombstone never deletes the native customer.
            if (canonical || customer.IsDeleted) return customer.Id;
            if (await context.Tanks.AnyAsync(x => x.CustomerId == customer.Id, token))
                throw new ConflictException("Cannot delete the Core customer while native tanks remain assigned.");
            customer.IsDeleted = true;
            customer.Status = CustomerStatus.Deleted;
            foreach (var account in await context.CustomerUsers.Where(x => x.CustomerId == customer.Id).ToListAsync(token))
            { account.IsDeleted = true; account.Status = UserStatus.Deleted; }
            return customer.Id;
        }

        var name = Text(dto.Data, "name", 250, required: true)!;
        var address = Text(dto.Data, "address", 500);
        var phone = Text(dto.Data, "phone_number", 100);
        var incomingStatus = Text(dto.Data, "status", 30, required: true) switch
        {
            "active" => CustomerStatus.Active, "inactive" => CustomerStatus.Inactive,
            _ => throw new BusinessException("Unknown legacy customer status.")
        };
        if (!canonical)
        {
            customer.CustomerName = name;
            customer.Address = address;
            customer.Phone = phone;
            if (customer.Status != CustomerStatus.Suspended) customer.Status = incomingStatus;
        }
        else
        {
            var snapshots = aliases.Where(x => !(x.Source == dto.Source && x.EntityId == dto.EntityId) && !x.IsDeleted)
                .Select(x => JsonSerializer.Deserialize<JsonElement>(x.SnapshotJson)).Append(dto.Data).ToArray();
            var previous = mapping is null ? default : JsonSerializer.Deserialize<JsonElement>(mapping.SnapshotJson);
            if (CanUpdateCanonical("name", name, customer.CustomerName, previous, snapshots)) customer.CustomerName = name;
            if (CanUpdateCanonical("address", address, customer.Address, previous, snapshots)) customer.Address = address;
            if (CanUpdateCanonical("phone_number", phone, customer.Phone, previous, snapshots)) customer.Phone = phone;
            var raw = dto.Data.GetProperty("status").GetString();
            if (customer.Status != CustomerStatus.Suspended && !(customer.Status == CustomerStatus.Active && incomingStatus == CustomerStatus.Inactive) &&
                SourceChanged(previous, "status", raw) && snapshots.All(x => SnapshotText(x, "status") == raw))
                customer.Status = incomingStatus;
        }
        customer.IsDeleted = false;
        return customer.Id;
    }

    private static bool IsHistoricalCustomerTombstone(LegacyEventIngestDto dto, LegacySyncMapping? mapping) =>
        dto.Source == "plasma-bv" && dto.Entity == "customer" && mapping is { CoreId: null, IsDeleted: true, LastSequence: 0 };

    private static bool CanUpdateCanonical(string field, string? incoming, string? native, JsonElement previous, JsonElement[] snapshots)
    {
        if (string.IsNullOrWhiteSpace(incoming) || !SourceChanged(previous, field, incoming)) return false;
        string Normalize(string? text) => field == "phone_number" ? NormalizePhone(text) : NormalizeText(text);
        var expected = Normalize(incoming);
        return Normalize(native) != expected && snapshots.All(x => Normalize(SnapshotText(x, field)) == expected);
    }

    private static bool SourceChanged(JsonElement previous, string field, string? value) =>
        previous.ValueKind == JsonValueKind.Object && previous.TryGetProperty(field, out var old) &&
        old.ValueKind is JsonValueKind.String or JsonValueKind.Null && old.GetString() != value;
    private static string? SnapshotText(JsonElement snapshot, string field) =>
        snapshot.ValueKind == JsonValueKind.Object && snapshot.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
    private static string NormalizeText(string? text) => Regex.Replace((text ?? "").Normalize(NormalizationForm.FormC).Trim().ToUpperInvariant(), @"\s+", " ");
    private static string NormalizePhone(string? text)
    {
        var raw = (text ?? "").Trim();
        var digits = Regex.Replace(raw, "[^0-9]", "");
        return raw.StartsWith("+84", StringComparison.Ordinal) ? "0" + digits[2..]
            : digits.StartsWith("0084", StringComparison.Ordinal) ? "0" + digits[4..] : digits;
    }

    private async Task<Guid?> ApplyTankAsync(LegacyEventIngestDto dto, LegacySyncMapping? mapping, bool deleting, CancellationToken token)
    {
        if (mapping?.CoreId is null && deleting) return null;
        var serial = deleting ? OptionalText(dto.Data, "serial", 100) : Text(dto.Data, "serial", 100, required: true);
        var legacy = deleting ? OptionalText(dto.Data, "old_id", 50) : Text(dto.Data, "old_id", 50);
        Tank? tank = mapping?.CoreId is not null
            ? await context.Tanks.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == mapping.CoreId, token)
            : dto.Operation == "create" ? await MatchTankAsync(serial, legacy, token) : null;
        if (mapping?.CoreId is not null && tank is null) throw new ConflictException("The mapped Core tank is missing.");
        if (mapping?.CoreId is null && tank is not null)
            throw new ConflictException("A tank with this physical identity already exists; verify and bind its source ID before updating it.");
        var isNew = tank is null;
        if (tank is null)
        {
            if (dto.Operation != "create" || deleting)
                throw new ConflictException("Bind this existing source tank to its verified native Core tank before retrying.");
            tank = new Tank { TankCode = serial is { Length: <= 50 } ? serial : SourceCode(dto, "T"), CreatedDate = DateTime.UtcNow,
                LegacyId = dto.Source == "plasma-bv" ? "plasma:" + dto.EntityId : null };
            context.Tanks.Add(tank);
        }
        if (mapping is null) await EnsureUnownedAsync("tank", tank.Id, token);
        if (!deleting && tank.IsDeleted && mapping is not { IsDeleted: true })
            throw new ConflictException("The matched tank was deleted in Core; resolve the mapping before retrying.");
        if (deleting)
        {
            if (await HasOtherActiveIdentityAsync(dto, tank.Id, token)) return tank.Id;
            if (!tank.IsDeleted && tank.Status == TankStatus.InUse)
                throw new ConflictException("Cannot delete a native tank while it is in use.");
            tank.IsDeleted = true;
            tank.Status = TankStatus.Deleted;
            return tank.Id;
        }
        if (!dto.Data.TryGetProperty("customer_id", out var sourceCustomer))
            throw new BusinessException("A full tank snapshot including customer_id is required.");
        Guid? coreCustomer = null;
        string? customerName = null;
        if (sourceCustomer.ValueKind != JsonValueKind.Null)
        {
            if (sourceCustomer.ValueKind != JsonValueKind.Number || !sourceCustomer.TryGetInt64(out var sourceId) || sourceId < 0)
                throw new BusinessException("Invalid legacy customer_id.");
            if (sourceId > 0)
            {
                var customerMap = await context.LegacySyncMappings.AsNoTracking().SingleOrDefaultAsync(
                    x => x.Source == dto.Source && x.Entity == "customer" && x.EntityId == sourceId && !x.IsDeleted, token);
                var customer = customerMap?.CoreId is null ? null : await context.Customers.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == customerMap.CoreId, token);
                if (customer is null)
                    throw new ConflictException("The source customer_id must have an active verified Core customer mapping first.");
                coreCustomer = customer.Id;
                customerName = customer.CustomerName;
            }
        }
        if (!isNew && TankKey(tank.SerialNumber) != TankKey(serial))
        {
            var collision = await MatchTankAsync(serial, legacy, token);
            if (collision is not null && collision.Id != tank.Id)
                throw new ConflictException("The updated tank physical identity belongs to another Core record.");
        }
        if (isNew || TankKey(tank.SerialNumber) != TankKey(serial)) tank.SerialNumber = serial;
        if (tank.LegacyId is null && legacy is not null) tank.LegacyId = legacy;
        tank.CustomerId = coreCustomer;
        var rawStatus = Text(dto.Data, "status", 40, required: true)!;
        tank.Status = ParseTankStatus(rawStatus, tank);
        if (dto.Data.TryGetProperty("_capacity", out var capacity) && capacity.ValueKind == JsonValueKind.Object)
        {
            if (!capacity.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var litres) || litres < 0)
                throw new BusinessException("Invalid source capacity metadata.");
            tank.Capacity = litres;
            if (isNew) tank.TankType = Text(capacity, "name", 100);
            tank.Unit ??= "L";
        }
        else if (isNew) throw new ConflictException("New tanks require resolved _capacity metadata.");
        if (rawStatus == "occupied" && coreCustomer is not null) tank.CurrentLocation = customerName;
        else if (dto.Data.TryGetProperty("_warehouse", out var warehouse) && warehouse.ValueKind == JsonValueKind.Object)
            tank.CurrentLocation = Text(warehouse, "name", 250);
        else if (coreCustomer is not null) tank.CurrentLocation = customerName;
        else if (dto.Data.TryGetProperty("_factory", out var factory) && factory.ValueKind == JsonValueKind.Object)
            tank.CurrentLocation = Text(factory, "name", 250);
        else if (dto.Data.TryGetProperty("_warehouse", out _) && dto.Data.TryGetProperty("_factory", out _))
            tank.CurrentLocation = null;
        // Older unenriched snapshots preserve native location rather than turning numeric IDs into display names.
        tank.IsDeleted = false;
        return tank.Id;
    }

    private async Task<Guid?> ApplyEngineAsync(LegacyEventIngestDto dto, LegacySyncMapping? mapping, bool deleting, CancellationToken token)
    {
        if (mapping?.CoreId is null && deleting) return null;
        var engine = mapping?.CoreId is not null
            ? await context.Engines.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == mapping.CoreId, token) : null;
        if (mapping?.CoreId is not null && engine is null) throw new ConflictException("The mapped Core engine is missing.");
        if (engine is null)
        {
            if (dto.Operation != "create")
                throw new ConflictException("Bind this existing source engine to its verified Core engine before retrying.");
            var serial = Text(dto.Data, "serial", 100, required: true)!;
            var legacy = OptionalText(dto.Data, "old_id", 50);
            // Physical identifiers are collision checks, never authority to update an existing record.
            if (await EngineCollisionAsync(serial, legacy, null, token))
                throw new ConflictException("An engine with this physical identity already exists; verify and bind its source ID.");
            engine = new Engine { EngineCode = SourceCode(dto, "E"), CreatedDate = DateTime.UtcNow, LegacyId = legacy };
            context.Engines.Add(engine);
        }
        if (!deleting && engine.IsDeleted && mapping is not { IsDeleted: true })
            throw new ConflictException("The Core engine was deleted; resolve its mapping before retrying.");
        if (deleting)
        {
            if (await HasOtherActiveIdentityAsync(dto, engine.Id, token)) return engine.Id;
            if (await context.Sessions.AnyAsync(x => x.EngineId == engine.Id && x.EndTime == null, token))
                throw new ConflictException("Cannot delete a Core engine with a running session.");
            engine.IsDeleted = true; engine.Status = EngineStatus.Deleted;
            return engine.Id;
        }
        var serialNumber = Text(dto.Data, "serial", 100, required: true)!;
        var name = Text(dto.Data, "name", 250);
        engine.EngineName = name ?? serialNumber;
        if (TankKey(engine.SerialNumber) != TankKey(serialNumber))
        {
            if (await EngineCollisionAsync(serialNumber, null, engine.Id, token))
                throw new ConflictException("The updated engine physical identity belongs to another Core record.");
            engine.SerialNumber = serialNumber;
        }
        // Core model, warranty, owner and employee assignments remain managed in Core.
        engine.Status = Text(dto.Data, "status", 30, required: true) switch
        {
            "active" => EngineStatus.Active, "inactive" or "locked" => EngineStatus.Inactive,
            "broken" => EngineStatus.Broken, "checking" => EngineStatus.Checking, "repair" => EngineStatus.Repair,
            _ => throw new BusinessException("Unknown legacy engine status.")
        };
        engine.IsDeleted = false;
        return engine.Id;
    }

    private Task<bool> EngineCollisionAsync(string serial, string? legacy, Guid? exceptId, CancellationToken token)
    {
        var normalized = TankKey(serial);
        if (context.Database.IsSqlServer())
        {
            var aliases = CodeAliases.For(normalized, 100);
            return context.Engines.IgnoreQueryFilters().AnyAsync(x =>
                (exceptId == null || x.Id != exceptId) &&
                ((x.SerialNumber != null && aliases.Contains(x.SerialNumber.Trim().ToUpper())) ||
                 aliases.Contains(x.EngineCode.Trim().ToUpper()) ||
                 (legacy != null && x.LegacyId == legacy)), token);
        }
        return context.Engines.FromSqlInterpolated($"""
            SELECT * FROM "Engines"
            WHERE (regexp_replace(upper(btrim("SerialNumber")), '^([A-Z]+)0+([0-9]+)$', '\1\2') = {normalized}
                OR regexp_replace(upper(btrim("EngineCode")), '^([A-Z]+)0+([0-9]+)$', '\1\2') = {normalized}
                OR ({legacy}::text IS NOT NULL AND "LegacyId" = {legacy}))
            """).IgnoreQueryFilters().AnyAsync(x => exceptId == null || x.Id != exceptId, token);
    }

    private async Task<Guid?> ApplyEmployeeAsync(LegacyEventIngestDto dto, LegacySyncMapping? mapping, bool deleting, CancellationToken token)
    {
        if (mapping?.CoreId is null && deleting) return null;
        var employee = mapping?.CoreId is not null
            ? await context.Employees.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == mapping.CoreId, token) : null;
        if (mapping?.CoreId is not null && employee is null) throw new ConflictException("The mapped Core employee is missing.");
        if (employee is null)
        {
            if (dto.Operation != "create")
                throw new ConflictException("Bind this existing source user to its verified Core employee before retrying.");
            employee = new Employee { EmployeeCode = SourceCode(dto, "U"), CreatedDate = DateTime.UtcNow };
            context.Employees.Add(employee);
        }
        if (!deleting && employee.IsDeleted && mapping is not { IsDeleted: true })
            throw new ConflictException("The Core employee was deleted; resolve its mapping before retrying.");
        var protectedAccount = await context.EmployeeUsers.AnyAsync(x => x.EmployeeId == employee.Id && x.IsSuperAdmin, token);
        if (deleting)
        {
            if (await HasOtherActiveIdentityAsync(dto, employee.Id, token)) return employee.Id;
            if (protectedAccount || await context.Tanks.AnyAsync(x => x.AssignedEmployeeId == employee.Id, token) ||
                await context.Engines.AnyAsync(x => x.ResponsibleEmployeeId == employee.Id, token))
                throw new ConflictException("Cannot delete a protected Core employee or an employee with assigned assets.");
            employee.IsDeleted = true; employee.Status = EmployeeStatus.Deleted;
            // Never create, mutate or delete EmployeeUsers authentication records from source profile events.
            return employee.Id;
        }
        if (!dto.Data.TryGetProperty("is_active", out var active) || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new BusinessException("A full user profile with boolean is_active is required.");
        if (protectedAccount && !active.GetBoolean())
            throw new ConflictException("A source event cannot deactivate a Core Super Admin.");
        employee.FullName = Text(dto.Data, "name", 150, required: true)!;
        employee.Phone = Text(dto.Data, "phone_number", 100);
        employee.Email = Text(dto.Data, "email", 150);
        if (employee.Status is not (EmployeeStatus.OnLeave or EmployeeStatus.Resigned))
            employee.Status = active.GetBoolean() ? EmployeeStatus.Active : EmployeeStatus.Inactive;
        employee.IsDeleted = false;
        return employee.Id;
    }

    private Task<bool> HasOtherActiveIdentityAsync(LegacyEventIngestDto dto, Guid coreId, CancellationToken token) =>
        context.LegacySyncMappings.AnyAsync(x => x.Entity == dto.Entity && x.CoreId == coreId && !x.IsDeleted &&
            !(x.Source == dto.Source && x.EntityId == dto.EntityId), token);

    private async Task<bool> PreserveAlternateProjectionAsync(LegacyEventIngestDto dto, LegacySyncMapping? mapping, CancellationToken token)
    {
        if (dto.Entity == "customer" || mapping?.CoreId is null) return false;
        var otherSources = await context.LegacySyncMappings.AsNoTracking().Where(x =>
            x.Entity == dto.Entity && x.CoreId == mapping.CoreId && x.Source != dto.Source)
            .Select(x => x.Source).Distinct().ToListAsync(token);
        if (otherSources.Count == 0) return false;
        if (!settings.CurrentValue.ProjectionOwners.TryGetValue(dto.Entity, out var owner))
            throw new ConflictException("A shared asset or employee requires an explicit LegacySync projection owner.");
        if (owner == dto.Source) return false;
        if (!otherSources.Contains(owner, StringComparer.Ordinal))
            throw new ConflictException("The configured projection owner has no verified identity for this shared record.");
        if (await FindNativeAsync(dto.Entity, mapping.CoreId.Value, token) is null)
            throw new ConflictException("The mapped native record is missing.");
        // Keep the alternate source revision, tombstone and receipt without projecting its business fields.
        return true;
    }

    private async Task<BaseEntity?> FindNativeAsync(string entity, Guid id, CancellationToken token)
    {
        var tracked = context.ChangeTracker.Entries<BaseEntity>().FirstOrDefault(x => x.Entity.Id == id &&
            (entity == "customer" && x.Entity is Customer || entity == "tank" && x.Entity is Tank ||
             entity == "engine" && x.Entity is Engine || entity == "user" && x.Entity is Employee));
        if (tracked is not null) return tracked.Entity;
        return entity switch
        {
            "customer" => await context.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id, token),
            "tank" => await context.Tanks.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id, token),
            "engine" => await context.Engines.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id, token),
            "user" => await context.Employees.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id, token),
            _ => throw new BusinessException("Invalid legacy entity.")
        };
    }

    private async Task SetSourceIdAsync(string entity, Guid coreId, string sourceSystem, long sourceId, CancellationToken token)
    {
        var record = (ISourceIdentified?)await FindNativeAsync(entity, coreId, token)
            ?? throw new ConflictException("The mapped native record is missing.");
        var existing = sourceSystem == "quanly.plasma.vn" ? record.QuanLyId : record.NewAppId;
        if (existing is not null && existing != sourceId && entity != "customer")
            throw new ConflictException("The native record already has a different ID for this source system.");
        // Deduplicated customers keep one stable representative; no alias is discarded from the ledger.
        if (sourceSystem == "quanly.plasma.vn") record.QuanLyId ??= sourceId;
        else record.NewAppId ??= sourceId;
    }

    private async Task ValidateSourceSystemAsync(string source, string system, CancellationToken token)
    {
        if (await context.LegacySyncMappings.AnyAsync(x => x.Source == source && x.SourceSystem != system, token) ||
            await context.LegacySyncReceipts.AnyAsync(x => x.Source == source && x.SourceSystem != system, token))
            throw new ConflictException("The source namespace is permanently bound to another source system.");
    }

    private static string ResolveSourceSystem(string source, string? system)
    {
        // The original plasma-bv sender omitted this field. Its existing event hashes must remain retryable.
        var resolved = system ?? (source == "plasma-bv" ? "quanly.plasma.vn" : null);
        if (resolved is not ("quanly.plasma.vn" or "newapp.plasmamed.vn") ||
            source == "plasma-bv" && resolved != "quanly.plasma.vn" || source == "newapp" && resolved != "newapp.plasmamed.vn")
            throw new BusinessException("A valid source_system matching the source namespace is required.");
        return resolved;
    }

    private static void ValidateUserSnapshot(JsonElement data, bool deleting)
    {
        // The receiver is an employee-profile integration, never an authentication import endpoint.
        string[] allowed = ["id", "name", "email", "phone_number", "is_active", "deleted_at", "user_type", "created_at", "updated_at"];
        if (data.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal)))
            throw new BusinessException("User snapshots may only contain the supported public profile fields; credentials and roles are forbidden.");
        if (data.TryGetProperty("user_type", out var userType) && userType.ValueKind != JsonValueKind.Null)
        {
            if (userType.ValueKind != JsonValueKind.Number || !userType.TryGetInt32(out var value))
                throw new BusinessException("Invalid descriptive source user_type.");
            if (!deleting && value is 3 or 6 or 10)
                throw new BusinessException("Customer, device and third-party users cannot be imported as employees.");
        }
    }

    private static string? OptionalText(JsonElement data, string name, int maximum) =>
        data.TryGetProperty(name, out _) ? Text(data, name, maximum) : null;

    private async Task<Tank?> MatchTankAsync(string? serial, string? legacy, CancellationToken token)
    {
        // Canonical original IDs win only when serial/code hints do not point to another record.
        var legacyMatches = legacy is null ? [] : await context.Tanks.IgnoreQueryFilters()
            .Where(x => x.LegacyId == legacy).Take(2).ToListAsync(token);
        var normalized = TankKey(serial);
        var serialAliases = context.Database.IsSqlServer() ? CodeAliases.For(normalized, 100) : [];
        var codeAliases = context.Database.IsSqlServer() ? CodeAliases.For(normalized, 50) : [];
        var serialMatches = serial is null ? [] : context.Database.IsSqlServer()
            ? await context.Tanks.IgnoreQueryFilters().Where(x =>
                (x.SerialNumber != null && serialAliases.Contains(x.SerialNumber.Trim().ToUpper())) ||
                codeAliases.Contains(x.TankCode.Trim().ToUpper()))
                .Take(2).ToListAsync(token)
            : await context.Tanks.FromSqlInterpolated($"""
            SELECT * FROM "Tanks"
            WHERE regexp_replace(upper(btrim("SerialNumber")), '^([A-Z]+)0+([0-9]+)$', '\1\2') = {normalized}
               OR regexp_replace(upper(btrim("TankCode")), '^([A-Z]+)0+([0-9]+)$', '\1\2') = {normalized}
            """).IgnoreQueryFilters().Take(2).ToListAsync(token);
        if (legacyMatches.Count > 1 || serialMatches.Count > 1 ||
            (legacyMatches.Count == 1 && serialMatches.Count == 1 && legacyMatches[0].Id != serialMatches[0].Id))
            throw new ConflictException("The source tank identity is ambiguous or its legacy and serial hints disagree.");
        return legacyMatches.SingleOrDefault() ?? serialMatches.SingleOrDefault();
    }

    private async Task EnsureUnownedAsync(string entity, Guid coreId, CancellationToken token)
    {
        if (await context.LegacySyncMappings.AnyAsync(x => x.Entity == entity && x.CoreId == coreId, token))
            throw new ConflictException("The native Core record already belongs to another source identity.");
    }

    private static TankStatus ParseTankStatus(string status, Tank tank) => status switch
    {
        "occupied" => TankStatus.AtClient,
        "ready" => tank.AssignedEmployeeId is null ? TankStatus.InStock : TankStatus.Available,
        "empty" or "to_be_refill" => TankStatus.Empty, "broken" => TankStatus.Damaged,
        "loading" => TankStatus.Loading, "locked" => TankStatus.Locked, "disabled" => TankStatus.Inactive,
        // The established importer preserves unsupported intermediate workflow states in the source snapshot.
        "belong_to_order" or "shipping" or "attached" or "detached" or "waiting_for_verification" or
        "importing" or "exporting" or "moving" or "revoking" or "undefined" => tank.Status != TankStatus.Deleted ? tank.Status
            : throw new ConflictException("Restoring a tank requires a supported source status; its native deleted status cannot be retained."),
        _ => throw new BusinessException("Unknown legacy tank status.")
    };

    private static void ValidateIdentity(string source, string entity, long id)
    {
        if (source is null || !Regex.IsMatch(source, "\\A[a-z0-9][a-z0-9_-]{0,39}\\z", RegexOptions.CultureInvariant) ||
            entity is not ("customer" or "tank" or "engine" or "user") || id <= 0)
            throw new BusinessException("Invalid legacy source identity.");
    }

    private static string? Text(JsonElement data, string name, int maximum, bool required = false)
    {
        if (!data.TryGetProperty(name, out var property))
            throw new BusinessException("A required full-snapshot field is absent: " + name + ".");
        if (property.ValueKind != JsonValueKind.String && property.ValueKind != JsonValueKind.Null)
            throw new BusinessException("Invalid legacy field type: " + name + ".");
        var value = property.ValueKind == JsonValueKind.Null ? null : property.GetString();
        if (value?.Length > maximum || (required && string.IsNullOrWhiteSpace(value)))
            throw new BusinessException("Invalid legacy field length: " + name + ".");
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string SourceCode(LegacyEventIngestDto dto, string type) => dto.Source == "plasma-bv" && type == "C"
        ? "PLASMA-C" + dto.EntityId
        : $"LEG-{type}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dto.Source)))[..12]}-{dto.EntityId}";

    private static string TankKey(string? value)
    {
        var normalized = (value ?? "").Trim().ToUpperInvariant();
        var match = Regex.Match(normalized, @"\A([A-Z]+)([0-9]+)\z", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value + (match.Groups[2].Value.TrimStart('0') is { Length: > 0 } digits ? digits : "0") : normalized;
    }

    private static LegacyEventIngestResultDto Result(LegacyEventIngestDto dto, string status) =>
        new(dto.EventId, dto.Source, dto.Sequence, status);

    private static string PayloadHash(LegacyEventIngestDto dto, bool omitSourceSystem = false)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto));
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                if (omitSourceSystem && property.Name == "source_system") continue;
                writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Name); WriteCanonical(writer, property.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
