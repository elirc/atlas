using Atlas.Domain;
using Atlas.Domain.Access;
using Atlas.Domain.Approvals;
using Atlas.Domain.Expenses;
using Atlas.Domain.Leave;

namespace Atlas.Domain.Tests.Approvals;

/// <summary>
/// The extracted component's OWN unit tests (added with the extraction, commit 3). Distinct from
/// the characterization tests, which pin the two consumers' behaviour and stay untouched — these
/// assert the guard the consumers now share: every legal move, every illegal one, the exact
/// wording, and the noun that varies by subject.
/// </summary>
public class ApprovalWorkflowTests
{
    // ── The transition data: legal vs illegal, asserted exhaustively (the EngagementTransitions
    //    pattern). The expectation is rebuilt from the narrative, NOT copied from the table. ──────
    [Fact]
    public void Legal_transitions_are_exactly_the_lifecycle_and_nothing_else()
    {
        var expected = new HashSet<(ApprovalState, ApprovalState)>
        {
            (ApprovalState.Draft, ApprovalState.Submitted),
            (ApprovalState.Submitted, ApprovalState.Approved),
            (ApprovalState.Submitted, ApprovalState.Denied),
            (ApprovalState.Submitted, ApprovalState.Cancelled),
            (ApprovalState.Approved, ApprovalState.Cancelled),
        };

        foreach (ApprovalState from in ApprovalTransitions.AllStates)
        {
            foreach (ApprovalState to in ApprovalTransitions.AllStates)
            {
                Assert.Equal(expected.Contains((from, to)), ApprovalTransitions.IsLegal(from, to));
            }
        }
    }

    [Fact]
    public void Terminal_states_have_no_legal_targets()
    {
        Assert.Empty(ApprovalTransitions.LegalTargetsFrom(ApprovalState.Denied));
        Assert.Empty(ApprovalTransitions.LegalTargetsFrom(ApprovalState.Cancelled));
    }

    // ── The guards: pass silently on legality, throw a precise message otherwise. ────────────────
    [Fact]
    public void Submit_is_allowed_only_from_draft()
    {
        ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanSubmit(ApprovalState.Draft); // no throw

        var ex = Assert.Throws<DomainRuleException>(() =>
            ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanSubmit(ApprovalState.Submitted));
        Assert.Equal(
            "Only a draft leave request can be submitted (this one is submitted).", ex.Message);
    }

    [Fact]
    public void Approve_and_deny_are_allowed_only_from_submitted()
    {
        ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanApprove(ApprovalState.Submitted);
        ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanDeny(ApprovalState.Submitted);

        Assert.Equal(
            "Only a submitted leave request can be approved (this one is draft).",
            Assert.Throws<DomainRuleException>(() =>
                ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanApprove(ApprovalState.Draft)).Message);

        Assert.Equal(
            "Only a submitted leave request can be denied (this one is approved).",
            Assert.Throws<DomainRuleException>(() =>
                ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanDeny(ApprovalState.Approved)).Message);
    }

    [Theory]
    [InlineData(ApprovalState.Submitted)]
    [InlineData(ApprovalState.Approved)]
    public void Cancel_is_allowed_from_submitted_or_approved(ApprovalState from) =>
        ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanCancel(from); // no throw

    [Theory]
    [InlineData(ApprovalState.Draft, "draft")]
    [InlineData(ApprovalState.Denied, "denied")]
    [InlineData(ApprovalState.Cancelled, "cancelled")]
    public void Cancel_is_refused_from_terminal_or_draft(ApprovalState from, string word)
    {
        var ex = Assert.Throws<DomainRuleException>(() =>
            ApprovalWorkflow<LeaveApprovalSubject>.EnsureCanCancel(from));
        Assert.Equal($"A {word} leave request cannot be cancelled.", ex.Message);
    }

    // ── The ONE thing that varies by consumer: the noun. ─────────────────────────────────────────
    [Fact]
    public void The_subject_noun_is_the_only_thing_that_differs_between_consumers()
    {
        Assert.Equal(
            "Only a submitted expense claim can be approved (this one is draft).",
            Assert.Throws<DomainRuleException>(() =>
                ApprovalWorkflow<ExpenseApprovalSubject>.EnsureCanApprove(ApprovalState.Draft)).Message);
    }

    [Fact]
    public void Approver_roles_are_org_admin_and_hr_manager_for_every_subject()
    {
        Assert.Equal(
            new[] { Role.OrgAdmin, Role.HrManager },
            ApprovalWorkflow<LeaveApprovalSubject>.ApproverRoles);
        Assert.Equal(
            ApprovalWorkflow<LeaveApprovalSubject>.ApproverRoles,
            ApprovalWorkflow<ExpenseApprovalSubject>.ApproverRoles);
    }

    // ── The boundary contract: the consumers' own enums line up with ApprovalState, so the
    //    (ApprovalState)status casts in LeaveRequest/ExpenseClaim are total and safe. ─────────────
    [Fact]
    public void Consumer_status_enums_align_one_for_one_with_ApprovalState()
    {
        foreach (ApprovalState state in ApprovalTransitions.AllStates)
        {
            string name = state.ToString();
            Assert.Equal((int)state, (int)Enum.Parse<LeaveRequestStatus>(name));
            Assert.Equal((int)state, (int)Enum.Parse<ExpenseClaimStatus>(name));
        }
    }
}
