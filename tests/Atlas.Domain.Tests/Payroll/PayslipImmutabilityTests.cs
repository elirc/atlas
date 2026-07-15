using System.Reflection;
using Atlas.Domain.Payroll;

namespace Atlas.Domain.Tests.Payroll;

/// <summary>
/// A payslip is a document: once computed it is never edited. This is enforced at
/// COMPILE time (no mutators exist), and pinned here as a fitness function so a future
/// "quick fix" setter turns the build red instead of quietly making corrections editable.
/// </summary>
// ⚠️ Corrections are new documents, never edits (accounting 101). If someone ever adds a
// public setter or an Adjust()/Recompute() to Payslip or PayslipLine to "just fix the
// number", this test is the tripwire — and ADR-0008 is the explanation of why the fix is
// a new run, not a mutation.
public class PayslipImmutabilityTests
{
    [Theory]
    [InlineData(typeof(Payslip))]
    [InlineData(typeof(PayslipLine))]
    public void Exposes_no_public_setters(Type type)
    {
        string[] settable = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToArray();

        Assert.True(settable.Length == 0,
            $"{type.Name} must be immutable, but exposes public setters: {string.Join(", ", settable)}");
    }

    [Theory]
    [InlineData(typeof(Payslip))]
    [InlineData(typeof(PayslipLine))]
    public void Exposes_no_mutating_methods(Type type)
    {
        // A payslip has no verbs. Any public instance method that isn't an object/record
        // built-in or a property accessor is a mutation surface we did not intend.
        string[] verbs = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // excludes get_/set_ accessors
            .Select(m => m.Name)
            .Where(name => name is not ("Equals" or "GetHashCode" or "ToString"))
            .ToArray();

        Assert.True(verbs.Length == 0,
            $"{type.Name} must have no mutators, but declares: {string.Join(", ", verbs)}");
    }
}
