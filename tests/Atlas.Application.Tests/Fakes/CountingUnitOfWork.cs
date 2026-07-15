using Atlas.Application.Abstractions;

namespace Atlas.Application.Tests.Fakes;

public sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.CompletedTask;
    }
}
