using Atlas.Api.Auth;
using Atlas.Api.Errors;
using Atlas.Application.Abstractions;
using Atlas.Application.Auth;
using Atlas.Application.Countries;
using Atlas.Application.Orgs;
using Atlas.Infrastructure;

namespace Atlas.Api.Composition;

/// <summary>
/// The composition root — the ONLY place in the Api allowed to see Infrastructure.
/// The architecture tests hold this line (Atlas.ArchitectureTests).
/// </summary>
public static class ServiceRegistration
{
    public static WebApplicationBuilder AddAtlas(this WebApplicationBuilder builder)
    {
        // Persistence (EF Core + port implementations) — Infrastructure's one entry point.
        builder.Services.AddAtlasPersistence(builder.Configuration);

        // The outbox dispatcher polls in the background — everywhere except the test host, where
        // the suite drives OutboxRelay by hand so a 5s poll can't race an assertion (ADR-0011).
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddAtlasOutboxDispatcher();
        }

        // WHY: TimeProvider.System is registered ONCE, here. Everything downstream
        // takes TimeProvider by constructor — which is why tests can freeze time.
        builder.Services.AddSingleton(TimeProvider.System);

        // Use cases: scoped, cheap, no interfaces — they ARE the application's API.
        // LEARN: no ICreateClientOrg. An interface with exactly one implementation
        // and no seam-consumer is ceremony; ports earn interfaces, use cases don't.
        builder.Services.AddScoped<ListCountries>();
        builder.Services.AddScoped<GetCountry>();
        builder.Services.AddScoped<CreateClientOrg>();
        builder.Services.AddScoped<GetClientOrg>();
        builder.Services.AddScoped<ListClientOrgs>();
        builder.Services.AddScoped<RegisterOrg>();
        builder.Services.AddScoped<Login>();
        builder.Services.AddScoped<Logout>();
        builder.Services.AddScoped<GetMe>();
        builder.Services.AddScoped<Application.Workforce.HireWorker>();
        builder.Services.AddScoped<Application.Workforce.GetWorker>();
        builder.Services.AddScoped<Application.Workforce.GetWorkerTerms>();
        builder.Services.AddScoped<Application.Workforce.UpdateEmploymentTerms>();
        builder.Services.AddScoped<Application.Workforce.ListWorkers>();
        builder.Services.AddScoped<Application.Auditing.ListAuditLog>();
        builder.Services.AddScoped<Application.Compliance.RecordWorkerDocument>();
        builder.Services.AddScoped<Application.Compliance.ListWorkerDocuments>();
        builder.Services.AddScoped<Application.Compliance.GetWorkerCompliance>();
        builder.Services.AddScoped<Application.Compliance.ListExpiringDocuments>();
        builder.Services.AddScoped<Application.Quotes.CreateQuote>();
        builder.Services.AddScoped<Application.Quotes.GetQuote>();
        builder.Services.AddScoped<Application.Quotes.ListQuotes>();
        builder.Services.AddScoped<Application.Engagements.CreateEngagement>();
        builder.Services.AddScoped<Application.Engagements.SendOffer>();
        builder.Services.AddScoped<Application.Engagements.AcceptOffer>();
        builder.Services.AddScoped<Application.Engagements.CompleteOnboardingItem>();
        builder.Services.AddScoped<Application.Engagements.ActivateEngagement>();
        builder.Services.AddScoped<Application.Engagements.WithdrawEngagement>();
        builder.Services.AddScoped<Application.Engagements.GetEngagement>();
        builder.Services.AddScoped<Application.Engagements.ListEngagements>();
        builder.Services.AddScoped<Application.Leave.CreateLeavePolicy>();
        builder.Services.AddScoped<Application.Leave.ListLeavePolicies>();
        builder.Services.AddScoped<Application.Leave.SubmitLeaveRequest>();
        builder.Services.AddScoped<Application.Leave.ApproveLeaveRequest>();
        builder.Services.AddScoped<Application.Leave.DenyLeaveRequest>();
        builder.Services.AddScoped<Application.Leave.CancelLeaveRequest>();
        builder.Services.AddScoped<Application.Leave.GetLeaveBalance>();
        builder.Services.AddScoped<Application.Leave.ListLeaveRequests>();
        builder.Services.AddScoped<Application.Payroll.CreatePayCycle>();
        builder.Services.AddScoped<Application.Payroll.ListPayCycles>();
        builder.Services.AddScoped<Application.Payroll.PreparePayrollRun>();
        builder.Services.AddScoped<Application.Payroll.SubmitPayrollRun>();
        builder.Services.AddScoped<Application.Payroll.ApprovePayrollRun>();
        builder.Services.AddScoped<Application.Payroll.RejectPayrollRun>();
        builder.Services.AddScoped<Application.Payroll.ExecutePayrollRun>();
        builder.Services.AddScoped<Application.Payroll.GetPayrollRun>();
        builder.Services.AddScoped<Application.Payroll.ListPayrollRuns>();
        builder.Services.AddScoped<Application.Payroll.GetPayslip>();
        // S12 — vendor selection + shadow running.
        builder.Services.AddScoped<Application.Payroll.AssignPayrollVendor>();
        builder.Services.AddScoped<Application.Payroll.ListVendorAssignments>();
        builder.Services.AddScoped<Application.Payroll.ShadowComparePayrollRun>();
        builder.Services.AddScoped<Application.Payroll.GetVendorComparison>();
        builder.Services.AddScoped<Application.Expenses.SubmitExpense>();
        builder.Services.AddScoped<Application.Expenses.ApproveExpense>();
        builder.Services.AddScoped<Application.Expenses.DenyExpense>();
        builder.Services.AddScoped<Application.Expenses.CancelExpense>();
        builder.Services.AddScoped<Application.Expenses.GetExpense>();
        builder.Services.AddScoped<Application.Expenses.ListExpenses>();
        builder.Services.AddScoped<Application.Approvals.ListPendingApprovals>();
        builder.Services.AddScoped<Application.Contractors.CreateContractor>();
        builder.Services.AddScoped<Application.Contractors.ListContractors>();
        builder.Services.AddScoped<Application.Invoicing.SubmitInvoice>();
        builder.Services.AddScoped<Application.Invoicing.HrApproveInvoice>();
        builder.Services.AddScoped<Application.Invoicing.AdminApproveInvoice>();
        builder.Services.AddScoped<Application.Invoicing.PayInvoice>();
        builder.Services.AddScoped<Application.Invoicing.ReconcileInvoicePayment>();
        builder.Services.AddScoped<Application.Invoicing.GetInvoice>();
        builder.Services.AddScoped<Application.Invoicing.ListInvoices>();
        builder.Services.AddScoped<Application.Webhooks.RegisterWebhook>();
        builder.Services.AddScoped<Application.Webhooks.ListWebhooks>();
        builder.Services.AddScoped<Application.Webhooks.PauseWebhook>();
        builder.Services.AddScoped<Application.Webhooks.ListWebhookDeliveries>();
        builder.Services.AddScoped<Application.Webhooks.RedeliverWebhook>();
        builder.Services.AddScoped<Application.ApiKeys.CreateApiKey>();
        builder.Services.AddScoped<Application.ApiKeys.ListApiKeys>();
        builder.Services.AddScoped<Application.ApiKeys.RevokeApiKey>();

        // The caller's identity: one RequestContext per request, exposed to the
        // Application layer as the ICurrentContext port.
        builder.Services.AddScoped<RequestContext>();
        builder.Services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<RequestContext>());

        // Errors → RFC 7807, one translation table for the whole API.
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<AtlasExceptionHandler>();

        // OpenAPI: generated from code, committed to the repo, drift-gated in CI.
        builder.Services.AddOpenApi();

        return builder;
    }
}
