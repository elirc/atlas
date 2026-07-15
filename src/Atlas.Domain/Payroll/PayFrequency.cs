namespace Atlas.Domain.Payroll;

/// <summary>How often a pay cycle produces a run. v1 ships exactly one cadence.</summary>
// WHY one value in an enum: a bool "isMonthly" would be a lie about the future, and a
// magic string "monthly" would be a lie about the present. The enum names the axis we
// KNOW will grow (weekly, biweekly, semi-monthly all exist in real payroll) while
// committing to the one cadence Atlas actually runs today. Adding "Biweekly" later is a
// new enum member plus the period-generation maths for it — the shape already has a slot
// for it, so the change stays local. That locality is the whole point of naming the axis
// now instead of discovering it under pressure later.
// GOTCHA: the number of PAY PERIODS PER YEAR is a property of the frequency, and the
// allocation of annual statutory figures across periods (see PayrollCalculator) depends
// on it. Monthly = 12. When a second cadence lands, that constant becomes a switch.
public enum PayFrequency
{
    Monthly = 1,
}
