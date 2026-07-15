namespace Atlas.Domain.Approvals;

/// <summary>
/// A marker a consumer implements so the generic <see cref="ApprovalWorkflow{T}"/> can name
/// the thing it is guarding in its error messages — "leave request", "expense claim" — WITHOUT
/// the workflow depending on either aggregate.
/// </summary>
// WHY a static-abstract member, not an instance property: the noun is a property of the TYPE,
// not of any one instance, and the workflow is a pure static guard with no instance to ask.
// `T.SubjectNoun` reads the marker straight off the type parameter. This is the ONE type
// parameter ApprovalWorkflow carries, and the debate (PR thread) is exactly about whether it
// stays one: a second parameter is the first step toward a framework — see the tripwire in
// ApprovalWorkflow.
public interface IApprovalSubject
{
    /// <summary>The lower-case noun for guard messages, e.g. "leave request".</summary>
    static abstract string SubjectNoun { get; }
}
