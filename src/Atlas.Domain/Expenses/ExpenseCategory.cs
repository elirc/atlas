namespace Atlas.Domain.Expenses;

/// <summary>What kind of expense a claim is — a small, closed vocabulary (v1).</summary>
// WHY a closed enum, not a free-text string: categories drive reporting and (later) policy
// ("meals over X need a receipt"), so they must be a fixed set the code can switch on, not
// prose. Adding one is a one-line edit here; the enum is stored by NAME so reordering never
// remaps existing rows (ExpenseConfigurations). A per-org custom taxonomy is a real feature —
// and a deliberate non-goal this sprint (see ADR-0009's scope note).
public enum ExpenseCategory
{
    Travel = 1,
    Meals = 2,
    Lodging = 3,
    Equipment = 4,
    Software = 5,
    Other = 6,
}
