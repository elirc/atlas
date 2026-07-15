using Atlas.Api.Mapping;
using Atlas.Application.Webhooks;
using Atlas.Contracts.V1;

namespace Atlas.Api.Endpoints;

public static class WebhookEndpoints
{
    public static RouteGroupBuilder MapWebhooks(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/webhooks").WithTags("Webhooks");

        group.MapPost("/", async (
            RegisterWebhookRequest request, RegisterWebhook useCase, CancellationToken ct) =>
        {
            WebhookRegistration registration = await useCase.ExecuteAsync(request.Url, request.Events, ct);
            return Results.Created(
                $"/api/v1/webhooks/{registration.Subscription.Id}", registration.ToDto());
        })
        .Produces<WebhookRegisteredDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (ListWebhooks useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(ct)).Select(s => s.ToDto())))
        .Produces<IEnumerable<WebhookSubscriptionDto>>();

        group.MapDelete("/{id:guid}", async (Guid id, PauseWebhook useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<WebhookSubscriptionDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        // The delivery log + on-demand redelivery — the "watch backoff, then redeliver" surface.
        group.MapGet("/deliveries", async (
            Guid? subscriptionId, ListWebhookDeliveries useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(subscriptionId, ct)).Select(d => d.ToDto())))
        .Produces<IEnumerable<WebhookDeliveryDto>>();

        group.MapPost("/deliveries/{id:guid}/redeliver", async (
            Guid id, RedeliverWebhook useCase, CancellationToken ct) =>
            Results.Ok((await useCase.ExecuteAsync(id, ct)).ToDto()))
        .Produces<WebhookDeliveryDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }
}
