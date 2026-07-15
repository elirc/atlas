namespace Atlas.Contracts.V1;

/// <summary>Body of POST /api/v1/leave-policies — define an org's leave rules for a country.</summary>
public sealed record CreateLeavePolicyRequest(
    string? Country,
    decimal AnnualEntitlementDays,
    decimal CarryoverCapDays);

public sealed record LeavePolicyDto(
    Guid Id,
    string Country,
    decimal AnnualEntitlementDays,
    string AccrualCadence,
    decimal CarryoverCapDays,
    DateTimeOffset CreatedAtUtc);

/// <summary>Body of POST /api/v1/leave-requests — submit leave for a worker (HR on behalf, v1).</summary>
public sealed record SubmitLeaveRequestRequest(
    Guid WorkerId,
    DateOnly From,
    DateOnly To,
    string? Note);

public sealed record LeaveRequestDto(
    Guid Id,
    Guid WorkerId,
    DateOnly From,
    DateOnly To,
    decimal WorkingDays,
    string Status,
    string? Note,
    Guid RequestedByUserId,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAtUtc,
    DateTimeOffset CreatedAtUtc);

/// <summary>GET /api/v1/workers/{id}/leave-balance?asOf= — derived leave standing.</summary>
public sealed record LeaveBalanceDto(
    Guid WorkerId,
    string Country,
    DateOnly AsOf,
    decimal AnnualEntitlementDays,
    decimal AccruedDays,
    decimal SpentDays,
    decimal AvailableDays);
