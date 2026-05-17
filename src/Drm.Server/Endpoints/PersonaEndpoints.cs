using Drm.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Drm.Server.Endpoints;

public static class PersonaEndpoints
{
    public static IEndpointRouteBuilder MapPersonaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/persona", GetPersonaAsync);
        endpoints.MapPut("/api/admin/personas/{userId:guid}", SetPersonaAsync);
        endpoints.MapGet("/api/admin/personas", ListPersonasAsync);
        return endpoints;
    }

    private static async Task<Results<Ok<PersonaResponse>, BadRequest<ErrorResponse>>> GetPersonaAsync(
        Guid tenantId,
        Guid userId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_identifiers"));
        }

        var assignment = await dbContext.TenantUserPersonas
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, cancellationToken);

        var persona = ParsePersona(assignment?.Persona);
        var capabilities = PersonaCapabilities.For(persona);
        return TypedResults.Ok(new PersonaResponse(
            tenantId,
            userId,
            persona.ToString(),
            capabilities.CanProtect,
            capabilities.CanRevoke,
            capabilities.CanInviteGuests,
            capabilities.CanViewAuditLog,
            capabilities.CanAdmin,
            assignment?.AssignedAtUtc));
    }

    private static async Task<Results<Created<PersonaResponse>, Ok<PersonaResponse>, BadRequest<ErrorResponse>>> SetPersonaAsync(
        Guid userId,
        SetPersonaRequest request,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || userId == Guid.Empty)
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_identifiers"));
        }
        if (!Enum.TryParse<DrmPersona>(request.Persona, ignoreCase: true, out var personaEnum))
        {
            return TypedResults.BadRequest(new ErrorResponse("invalid_persona"));
        }

        var existing = await dbContext.TenantUserPersonas
            .SingleOrDefaultAsync(p => p.TenantId == request.TenantId && p.UserId == userId, cancellationToken);

        var isNew = existing is null;
        if (existing is null)
        {
            existing = new TenantUserPersonaEntity
            {
                TenantId = request.TenantId,
                UserId = userId
            };
            dbContext.TenantUserPersonas.Add(existing);
        }
        existing.Persona = personaEnum.ToString();
        existing.AssignedAtUtc = DateTimeOffset.UtcNow;

        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            TenantId = request.TenantId,
            UserId = userId,
            EventType = "system_changed",
            ReasonCode = $"persona_assigned_{personaEnum}",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var capabilities = PersonaCapabilities.For(personaEnum);
        var response = new PersonaResponse(
            request.TenantId,
            userId,
            personaEnum.ToString(),
            capabilities.CanProtect,
            capabilities.CanRevoke,
            capabilities.CanInviteGuests,
            capabilities.CanViewAuditLog,
            capabilities.CanAdmin,
            existing.AssignedAtUtc);
        return isNew
            ? TypedResults.Created($"/api/admin/personas/{userId}?tenantId={request.TenantId}", response)
            : TypedResults.Ok(response);
    }

    private static async Task<IReadOnlyList<PersonaResponse>> ListPersonasAsync(
        Guid tenantId,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.TenantUserPersonas
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return rows.Select(p =>
        {
            var persona = ParsePersona(p.Persona);
            var caps = PersonaCapabilities.For(persona);
            return new PersonaResponse(
                p.TenantId, p.UserId, persona.ToString(),
                caps.CanProtect, caps.CanRevoke, caps.CanInviteGuests, caps.CanViewAuditLog, caps.CanAdmin,
                p.AssignedAtUtc);
        }).ToList();
    }

    private static DrmPersona ParsePersona(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DrmPersona.Employee;
        return Enum.TryParse<DrmPersona>(value, ignoreCase: true, out var p) ? p : DrmPersona.Employee;
    }

    private sealed record SetPersonaRequest(Guid TenantId, string Persona);

    private sealed record PersonaResponse(
        Guid TenantId,
        Guid UserId,
        string Persona,
        bool CanProtect,
        bool CanRevoke,
        bool CanInviteGuests,
        bool CanViewAuditLog,
        bool CanAdmin,
        DateTimeOffset? AssignedAtUtc);

    private sealed record ErrorResponse(string ReasonCode);
}
