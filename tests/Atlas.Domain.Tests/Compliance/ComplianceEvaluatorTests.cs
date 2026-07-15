using Atlas.Domain.Compliance;
using Atlas.Domain.Countries;
using Xunit;

namespace Atlas.Domain.Tests.Compliance;

public class ComplianceEvaluatorTests
{
    private static readonly DateOnly Today = new(2026, 7, 15);
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Worker = Guid.NewGuid();

    private static WorkerDocument Doc(DocumentKind kind, DateOnly? expiresOn) =>
        WorkerDocument.Record(Org, Worker, kind, "ref", expiresOn, DateTimeOffset.UnixEpoch);

    [Fact]
    public void A_missing_required_document_breaks_compliance()
    {
        var required = new[] { new RequiredDocument(DocumentKind.TaxRegistration, "TIN", ExpiryTracked: false) };

        WorkerComplianceReport report = ComplianceEvaluator.Evaluate(required, [], Today);

        Assert.False(report.IsCompliant);
        Assert.Equal(DocumentComplianceState.Missing, report.Lines.Single().State);
    }

    [Fact]
    public void A_provided_non_expiry_document_is_compliant()
    {
        var required = new[] { new RequiredDocument(DocumentKind.TaxRegistration, "TIN", ExpiryTracked: false) };

        WorkerComplianceReport report = ComplianceEvaluator.Evaluate(required, [Doc(DocumentKind.TaxRegistration, null)], Today);

        Assert.True(report.IsCompliant);
        Assert.Equal(DocumentComplianceState.Provided, report.Lines.Single().State);
    }

    [Fact]
    public void An_expired_document_breaks_compliance()
    {
        var required = new[] { new RequiredDocument(DocumentKind.WorkAuthorization, "Permit", ExpiryTracked: true) };

        WorkerComplianceReport report = ComplianceEvaluator.Evaluate(
            required, [Doc(DocumentKind.WorkAuthorization, Today.AddDays(-1))], Today);

        Assert.False(report.IsCompliant);
        Assert.Equal(DocumentComplianceState.Expired, report.Lines.Single().State);
    }

    [Fact]
    public void A_document_expiring_within_the_window_is_compliant_but_flagged()
    {
        var required = new[] { new RequiredDocument(DocumentKind.WorkAuthorization, "Permit", ExpiryTracked: true) };

        WorkerComplianceReport report = ComplianceEvaluator.Evaluate(
            required, [Doc(DocumentKind.WorkAuthorization, Today.AddDays(10))], Today);

        Assert.True(report.IsCompliant); // still valid…
        Assert.Equal(DocumentComplianceState.ExpiringSoon, report.Lines.Single().State); // …but a warning
    }

    [Fact]
    public void A_renewed_document_supersedes_the_lapsed_one()
    {
        var required = new[] { new RequiredDocument(DocumentKind.WorkAuthorization, "Permit", ExpiryTracked: true) };

        // The worker holds BOTH a lapsed permit and a fresh one; the latest-expiring wins.
        WorkerComplianceReport report = ComplianceEvaluator.Evaluate(
            required,
            [Doc(DocumentKind.WorkAuthorization, Today.AddDays(-5)), Doc(DocumentKind.WorkAuthorization, Today.AddYears(1))],
            Today);

        Assert.True(report.IsCompliant);
        Assert.Equal(DocumentComplianceState.Provided, report.Lines.Single().State);
    }
}
