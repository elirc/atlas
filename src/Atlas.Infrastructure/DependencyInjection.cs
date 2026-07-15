using Atlas.Application.Abstractions;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence: EF Core (Postgres for real life, SQLite for a
    /// zero-dependency local start) plus every port implementation this layer owns.
    /// </summary>
    public static IServiceCollection AddAtlasPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // WHY: provider selection is configuration, not code — the same binary runs
        // against compose Postgres (Database:Provider=postgres) or a local SQLite
        // file (the default when nothing is configured). Tests bypass this entirely
        // and inject an in-memory SQLite connection.
        string provider = configuration["Database:Provider"] ?? "sqlite";
        string connectionString = configuration.GetConnectionString("atlas")
            ?? "Data Source=atlas.dev.db";

        services.AddDbContext<AtlasDbContext>(options =>
        {
            _ = provider.ToLowerInvariant() switch
            {
                "postgres" => options.UseNpgsql(connectionString),
                "sqlite" => options.UseSqlite(connectionString),
                _ => throw new InvalidOperationException(
                    $"Unknown Database:Provider '{provider}'. Use 'postgres' or 'sqlite'."),
            };
        });

        services.AddScoped<ICountryRepository, CountryRepository>();
        services.AddScoped<IClientOrgRepository, ClientOrgRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IWorkerRepository, WorkerRepository>();
        services.AddScoped<IEmploymentRecordRepository, EmploymentRecordRepository>();
        services.AddScoped<IWorkerDocumentRepository, WorkerDocumentRepository>();
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddScoped<IEngagementRepository, EngagementRepository>();
        // S10 — events no longer publish inline; IEventSink now ENQUEUES an outbox row in the
        // use case's own transaction (OutboxEventSink), and the dispatcher delivers post-commit
        // through the IEventDelivery adapters. The publish-before-commit ordering in the use cases
        // is now correct rather than a phantom-event bug (ADR-0011).
        services.AddScoped<IEventSink, Outbox.OutboxEventSink>();
        services.AddScoped<IEventDelivery, Outbox.LoggingEventDelivery>();
        services.AddScoped<IEventDelivery, Webhooks.WebhookEventDelivery>();
        services.AddScoped<Outbox.OutboxRelay>();
        // S10 — idempotency store for the Idempotency-Key middleware (harvests the double-pay flaw).
        services.AddScoped<IIdempotencyStore, Idempotency.EfIdempotencyStore>();
        // S10 — webhooks: subscriptions + delivery log + the HTTP transport (a named HttpClient).
        services.AddScoped<IWebhookSubscriptionRepository, WebhookSubscriptionRepository>();
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();
        services.AddScoped<IWebhookTransport, Webhooks.HttpWebhookTransport>();
        services.AddHttpClient("webhooks");
        // S10 — partner API keys.
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IQuoteReader, Adapters.QuoteReader>();
        services.AddScoped<ILeavePolicyRepository, LeavePolicyRepository>();
        services.AddScoped<ILeaveRequestRepository, LeaveRequestRepository>();
        services.AddScoped<IPayCycleRepository, PayCycleRepository>();
        services.AddScoped<IPayrollRunRepository, PayrollRunRepository>();
        services.AddScoped<IPayslipRepository, PayslipRepository>();
        services.AddScoped<IExpenseClaimRepository, ExpenseClaimRepository>();
        // S09 — contractor invoicing persistence.
        services.AddScoped<IContractorRepository, ContractorRepository>();
        services.AddScoped<IContractorInvoiceRepository, ContractorInvoiceRepository>();
        // S09 — the PayGrid mock vendor and the payment anti-corruption layer. Nothing above
        // IPaymentProvider ever sees a PayGrid type; the adapter is where the dialect stops (ADR-0010).
        services.AddSingleton(Vendors.PayGrid.PayGridSettings.Default);
        services.AddSingleton<Vendors.PayGrid.PayGridClient>();
        services.AddScoped<IPaymentProvider, Vendors.PayGrid.PayGridPaymentProvider>();
        // WHY: IEventSink is already registered (S05, Messaging.LoggingEventSink) — payroll
        // reuses that seam. Only the payroll-specific unpaid-leave stand-in is new here.
        services.AddScoped<IUnpaidLeaveCalendar, Payroll.NoUnpaidLeaveCalendar>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Stateless immutable cross-rate table — a singleton, like the crypto services.
        services.AddSingleton<IFxRates, Fx.FxRateTable>();

        // Stateless crypto services — singletons on purpose.
        services.AddSingleton<IPasswordHasher, Security.Pbkdf2PasswordHasher>();
        services.AddSingleton<ISessionTokens, Security.SessionTokenService>();
        services.AddSingleton<ISecretGenerator, Security.SecretGenerator>();

        return services;
    }

    /// <summary>
    /// Registers the in-process outbox dispatcher (a hosted background service). Kept separate so the
    /// composition root can decline it in the test environment — tests drive <see cref="Outbox.OutboxRelay"/>
    /// directly for determinism, without a 5-second poll racing their assertions.
    /// </summary>
    public static IServiceCollection AddAtlasOutboxDispatcher(this IServiceCollection services)
    {
        services.AddHostedService<Outbox.OutboxDrainService>();
        return services;
    }
}
