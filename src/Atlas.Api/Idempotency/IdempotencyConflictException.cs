namespace Atlas.Api.Idempotency;

/// <summary>
/// The same <c>Idempotency-Key</c> was replayed with a DIFFERENT request body — a client bug we
/// surface (422) rather than mask by replaying the wrong stored response.
/// </summary>
// LEARN: this lives in Atlas.Api, not Application, because idempotency is a TRANSPORT concern — it is
// about HTTP retries, request bodies and headers, none of which the domain knows or should. The
// exception handler maps it like any other; the failure family just happens to originate at the edge.
public sealed class IdempotencyConflictException(string message) : Exception(message);
