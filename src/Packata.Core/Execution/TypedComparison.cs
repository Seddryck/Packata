using System.Globalization;
using System.Numerics;
using Packata.Core.Contracts;

namespace Packata.Core.Execution;

/// <summary>Backend-neutral mathematical comparisons. Strings are never parsed as numbers or dates.</summary>
public static class TypedComparison
{
    public static bool Equal(QualityValue left, QualityValue right)
    {
        if (left.Kind != right.Kind) return false;
        if (left.Kind == QualityValueKind.Scalar)
        {
            var a = (ScalarValue)left.Value!;
            var b = (ScalarValue)right.Value!;
            if (IsNumeric(a) && IsNumeric(b)) return CompareNumbers(a.Value!, b.Value!) == 0;
            return a.Equals(b);
        }
        if (left.Kind is QualityValueKind.List or QualityValueKind.Object)
            throw new ArgumentException("Assertions require scalar or temporal operands.");
        return Equals(left.Value, right.Value);
    }

    public static int Compare(QualityValue left, QualityValue right)
    {
        if (left.Kind != right.Kind) throw new ArgumentException("Incompatible comparison types.");
        if (left.Kind == QualityValueKind.Scalar)
        {
            var a = (ScalarValue)left.Value!;
            var b = (ScalarValue)right.Value!;
            if (IsNumeric(a) && IsNumeric(b)) return CompareNumbers(a.Value!, b.Value!);
            throw new ArgumentException("Ordered comparisons require numeric or temporal values.");
        }
        return (left.Value, right.Value) switch
        {
            (DateTimeOffset a, DateTimeOffset b) => a.CompareTo(b),
            (DateOnly a, DateOnly b) => a.CompareTo(b),
            (TimeOnly a, TimeOnly b) => a.CompareTo(b),
            (ContractDuration a, ContractDuration b) when a.CalendarMonths == b.CalendarMonths => a.Elapsed.CompareTo(b.Elapsed),
            (ContractDuration a, ContractDuration b) when a.Elapsed == b.Elapsed => a.CalendarMonths.CompareTo(b.CalendarMonths),
            _ => throw new ArgumentException("Calendar months cannot be compared with elapsed durations without a reference date.")
        };
    }

    private static bool IsNumeric(ScalarValue value) => value.Kind is ScalarKind.Integer or ScalarKind.Number;
    private static int CompareNumbers(object left, object right)
    {
        var (a, aExponent) = Number(left);
        var (b, bExponent) = Number(right);
        var exponent = Math.Min(aExponent, bExponent);
        return (a * BigInteger.Pow(10, aExponent - exponent)).CompareTo(b * BigInteger.Pow(10, bExponent - exponent));
    }

    private static (BigInteger Coefficient, int Exponent) Number(object number)
    {
        var text = number switch
        {
            double value => value.ToString("R", CultureInfo.InvariantCulture),
            float value => value.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToString(number, CultureInfo.InvariantCulture)!
        };
        var parts = text.ToLowerInvariant().Split('e');
        var exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        var point = parts[0].IndexOf('.');
        if (point >= 0) exponent -= parts[0].Length - point - 1;
        return (BigInteger.Parse(parts[0].Replace(".", ""), CultureInfo.InvariantCulture), exponent);
    }
}

public sealed class ExpectationAssertionPlan : IAssertionPlan
{
    public IReadOnlyList<QualityExpectation> Expectations { get; }
    public bool ImplicitBoolean { get; }
    public ExpectationAssertionPlan(IEnumerable<QualityExpectation> expectations, bool implicitBoolean = false)
        => (Expectations, ImplicitBoolean) = (Array.AsReadOnly(expectations.ToArray()), implicitBoolean);

    public RuleOutcome Evaluate(QualityValue value)
    {
        if (Expectations.Count == 0)
        {
            if (!ImplicitBoolean || value.ToObject() is not bool verdict) return RuleOutcome.Inconclusive;
            return verdict ? RuleOutcome.Passed : RuleOutcome.Failed;
        }
        return Expectations.All(expectation => Matches(value, expectation)) ? RuleOutcome.Passed : RuleOutcome.Failed;
    }

    public static bool Matches(QualityValue value, QualityExpectation expectation) => expectation.Operator switch
    {
        ExpectationOperator.Equal => TypedComparison.Equal(value, expectation.Operand),
        ExpectationOperator.NotEqual => !TypedComparison.Equal(value, expectation.Operand),
        ExpectationOperator.Greater => TypedComparison.Compare(value, expectation.Operand) > 0,
        ExpectationOperator.GreaterOrEqual => TypedComparison.Compare(value, expectation.Operand) >= 0,
        ExpectationOperator.Less => TypedComparison.Compare(value, expectation.Operand) < 0,
        ExpectationOperator.LessOrEqual => TypedComparison.Compare(value, expectation.Operand) <= 0,
        ExpectationOperator.Between => TypedComparison.Compare(value, expectation.Operand) > 0 && TypedComparison.Compare(value, expectation.Upper!) < 0,
        ExpectationOperator.NotBetween => TypedComparison.Compare(value, expectation.Operand) <= 0 || TypedComparison.Compare(value, expectation.Upper!) >= 0,
        _ => throw new ArgumentException("Unknown expectation operator.")
    };

    public static IReadOnlyList<string> Validate(IReadOnlyList<QualityExpectation> expectations)
        => ExpectationValidation.Validate(expectations);
}
