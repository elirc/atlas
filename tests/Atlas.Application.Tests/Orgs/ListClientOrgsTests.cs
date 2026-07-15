using Atlas.Application.Errors;
using Atlas.Application.Orgs;
using Atlas.Application.Pagination;
using Atlas.Application.Tests.Fakes;
using Atlas.Domain.Orgs;

namespace Atlas.Application.Tests.Orgs;

public class ListClientOrgsTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private static InMemoryClientOrgRepository Seeded(params string[] names)
    {
        var repo = new InMemoryClientOrgRepository();
        for (int i = 0; i < names.Length; i++)
        {
            // Distinct creation instants: index i created i minutes after T0.
            repo.AddAsync(
                ClientOrg.Create(names[i], T0.AddMinutes(i)),
                CancellationToken.None).GetAwaiter().GetResult();
        }

        return repo;
    }

    private static ListClientOrgs OpsList(InMemoryClientOrgRepository repo) =>
        new(repo, StubContext.Ops());

    [Fact]
    public async Task Pages_newest_first_and_walks_to_exhaustion()
    {
        var useCase = OpsList(Seeded("alpha", "beta", "gamma"));

        Page<ClientOrg> first = await useCase.ExecuteAsync(null, 2, CancellationToken.None);

        Assert.Equal(["gamma", "beta"], first.Items.Select(o => o.Name));
        Assert.NotNull(first.NextCursor);

        Page<ClientOrg> second =
            await useCase.ExecuteAsync(first.NextCursor, 2, CancellationToken.None);

        Assert.Equal(["alpha"], second.Items.Select(o => o.Name));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Exact_page_boundary_yields_no_phantom_next_cursor()
    {
        // LEARN: the limit+1 probe's edge case — exactly `limit` rows must produce
        // NextCursor = null, not a cursor pointing at an empty page.
        var useCase = OpsList(Seeded("alpha", "beta"));

        Page<ClientOrg> page = await useCase.ExecuteAsync(null, 2, CancellationToken.None);

        Assert.Equal(2, page.Items.Count);
        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task Out_of_range_limit_is_rejected(int limit)
    {
        var useCase = OpsList(Seeded("alpha"));

        await Assert.ThrowsAsync<RequestValidationException>(
            () => useCase.ExecuteAsync(null, limit, CancellationToken.None));
    }

    [Fact]
    public async Task Garbage_cursor_is_rejected()
    {
        var useCase = OpsList(Seeded("alpha"));

        await Assert.ThrowsAsync<RequestValidationException>(
            () => useCase.ExecuteAsync("???", 10, CancellationToken.None));
    }
}
