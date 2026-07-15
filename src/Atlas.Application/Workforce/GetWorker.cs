using Atlas.Application.Abstractions;
using Atlas.Application.Errors;
using Atlas.Domain.Workforce;

namespace Atlas.Application.Workforce;

public sealed class GetWorker(IWorkerRepository workers, ICurrentContext context)
{
    public async Task<Worker> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        context.RequireUser();

        Worker? worker = await workers.FindAsync(id, cancellationToken);

        return worker ?? throw new NotFoundException("worker", id);
    }
}
