using Atlas.Domain;
using Atlas.Domain.Invoicing;

namespace Atlas.Domain.Tests.Invoicing;

/// <summary>
/// The exhaustive legality matrix. It asserts against the SAME table the guard methods consult,
/// so the code and its test can never disagree about what "legal" means — and the illegal half of
/// the matrix (much larger than the legal half) is checked as the complement, not enumerated.
/// </summary>
public class InvoiceStateMachineTests
{
    [Fact]
    public void Legal_moves_are_exactly_the_documented_table()
    {
        var expected = new HashSet<(InvoiceStatus, InvoiceTransition, InvoiceStatus)>
        {
            (InvoiceStatus.Draft, InvoiceTransition.Submit, InvoiceStatus.Submitted),
            (InvoiceStatus.Submitted, InvoiceTransition.HrApprove, InvoiceStatus.HrApproved),
            (InvoiceStatus.HrApproved, InvoiceTransition.AdminApprove, InvoiceStatus.Approved),
            (InvoiceStatus.Approved, InvoiceTransition.Pay, InvoiceStatus.Paid),
            (InvoiceStatus.Approved, InvoiceTransition.FailPayment, InvoiceStatus.Failed),
            (InvoiceStatus.Failed, InvoiceTransition.Pay, InvoiceStatus.Paid),
            (InvoiceStatus.Failed, InvoiceTransition.FailPayment, InvoiceStatus.Failed),
        };

        HashSet<(InvoiceStatus, InvoiceTransition, InvoiceStatus)> actual =
            [.. InvoiceStateMachine.LegalMoves.Select(m => (m.From, m.Move, m.To))];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_state_transition_pair_is_legal_iff_it_is_in_the_table()
    {
        HashSet<(InvoiceStatus, InvoiceTransition)> legal =
            [.. InvoiceStateMachine.LegalMoves.Select(m => (m.From, m.Move))];

        foreach (InvoiceStatus from in Enum.GetValues<InvoiceStatus>())
        {
            foreach (InvoiceTransition move in Enum.GetValues<InvoiceTransition>())
            {
                bool reachable = InvoiceStateMachine.TryNext(from, move, out _);
                Assert.Equal(legal.Contains((from, move)), reachable);
            }
        }
    }

    [Fact]
    public void Next_throws_on_an_illegal_move()
    {
        // Paid is terminal — nothing follows it.
        Assert.Throws<DomainRuleException>(() =>
            InvoiceStateMachine.Next(InvoiceStatus.Paid, InvoiceTransition.Pay));
    }
}
