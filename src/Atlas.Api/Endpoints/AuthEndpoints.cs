using Atlas.Api.Auth;
using Atlas.Application.Auth;
using Atlas.Contracts.V1;

namespace Atlas.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register-org", async (
            RegisterOrgRequest request,
            RegisterOrg useCase,
            CancellationToken ct) =>
        {
            RegisteredOrg result = await useCase.ExecuteAsync(
                request.OrgName, request.Email, request.Password, request.DisplayName, ct);

            var me = new MeDto(
                result.User.Id, result.User.Email.Value, result.User.DisplayName,
                IsAtlasOps: false, result.Org.Id, nameof(Domain.Access.Role.OrgAdmin));

            return Results.Created(
                $"/api/v1/client-orgs/{result.Org.Id}",
                new AuthSessionDto(result.Token, result.ExpiresAtUtc, me));
        })
        .Produces<AuthSessionDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/login", async (
            LoginRequest request,
            Login useCase,
            CancellationToken ct) =>
        {
            LoggedIn result = await useCase.ExecuteAsync(request.Email, request.Password, ct);

            // WHY: login can't reuse GetMe (no session middleware ran for THIS new
            // token), so it shapes its own MeDto from what the use case returned.
            var me = new MeDto(
                result.User.Id, result.User.Email.Value, result.User.DisplayName,
                result.User.IsAtlasOps, OrgId: null, Role: null);

            return Results.Ok(new AuthSessionDto(result.Token, result.ExpiresAtUtc, me));
        })
        .Produces<AuthSessionDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", async (
            RequestContext context,
            Logout useCase,
            CancellationToken ct) =>
        {
            context.RequireUser();
            await useCase.ExecuteAsync(context.RawToken!, ct);
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", async (GetMe useCase, CancellationToken ct) =>
        {
            Me me = await useCase.ExecuteAsync(ct);

            return Results.Ok(new MeDto(
                me.User.Id, me.User.Email.Value, me.User.DisplayName, me.User.IsAtlasOps,
                me.Membership?.OrgId, me.Membership?.Role.ToString()));
        })
        .Produces<MeDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        return api;
    }
}
