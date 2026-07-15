namespace Atlas.Application.Abstractions;

/// <summary>
/// The supported-country reference list — code, display name, currency. Reference data: the same for
/// every org, changing only when Atlas ships a new country (a deploy, S11's rule-pack thesis), never
/// on a request. A PORT so the read can be CACHED behind it without the caller knowing.
/// </summary>
// ⚠️ THIS IS THE ONE KIND OF THING WE CACHE, and the reason is written in the type: it is reference
// data, not per-org data. Its staleness budget is a deploy cycle, and its invalidation trigger is
// "a new country shipped" — both rare, both loud. Per-org mutable data (workers, invoices, payroll,
// compliance status) is explicitly NOT eligible: it changes on every request, its correct cache is
// no cache, and a stale worker count is a wrong answer wearing a fast one's clothes. The cache ledger
// (ADR-0017) records exactly two entries and the argument for each; this is the first.
public interface ISupportedCountries
{
    Task<IReadOnlyList<SupportedCountry>> AllAsync(CancellationToken cancellationToken);
}

/// <summary>A country Atlas supports, as reference data for reports and pickers.</summary>
public sealed record SupportedCountry(string Code, string Name, string Currency);
