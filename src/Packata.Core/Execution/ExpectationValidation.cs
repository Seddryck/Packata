using Packata.Core.Contracts;

namespace Packata.Core.Execution;

internal static class ExpectationValidation
{
    internal static IReadOnlyList<string> Validate(IReadOnlyList<QualityExpectation> expectations)
    {
        try
        {
            var shapeError = expectations.Select(ValidateOperand).FirstOrDefault(error => error is not null);
            if (shapeError is not null) return [shapeError];
            var bounds = new Bounds();
            foreach (var expectation in expectations) bounds.Add(expectation);
            var boundsError = bounds.ValidateConsistency(expectations);
            if (boundsError is not null) return [boundsError];
            var contradictory = expectations.Where(e => e.Operator == ExpectationOperator.Equal)
                .Any(equality => expectations.Any(e => !ExpectationAssertionPlan.Matches(equality.Operand, e)));
            return contradictory ? ["Conflicting equality expectations."] : [];
        }
        catch (ArgumentException) { return ["Incompatible or invalid expectation operands."]; }
    }

    private static string? ValidateOperand(QualityExpectation expectation)
    {
        if (!Enum.IsDefined(expectation.Operator)) return "Unknown expectation operator.";
        if (!IsScalar(expectation.Operand)) return "Expectation operand must be scalar or temporal.";
        if (IsRange(expectation.Operator)) return ValidateRange(expectation);
        if (expectation.Upper is not null) return "A non-range expectation has an extra operand.";
        if (!IsEquality(expectation.Operator)) _ = TypedComparison.Compare(expectation.Operand, expectation.Operand);
        return null;
    }

    private static bool IsScalar(QualityValue value) => value.Kind is not (QualityValueKind.List or QualityValueKind.Object);
    private static bool IsRange(ExpectationOperator op) => op is ExpectationOperator.Between or ExpectationOperator.NotBetween;
    private static bool IsEquality(ExpectationOperator op) => op is ExpectationOperator.Equal or ExpectationOperator.NotEqual;
    private static string? ValidateRange(QualityExpectation expectation)
        => expectation.Upper is null || TypedComparison.Compare(expectation.Operand, expectation.Upper) >= 0
            ? "Range requires two ordered, increasing operands." : null;

    private sealed class Bounds
    {
        private QualityValue? _lower;
        private QualityValue? _upper;
        private bool _lowerInclusive;
        private bool _upperInclusive;

        internal void Add(QualityExpectation expectation)
        {
            switch (expectation.Operator)
            {
                case ExpectationOperator.Greater: Lower(expectation.Operand, false); break;
                case ExpectationOperator.GreaterOrEqual: Lower(expectation.Operand, true); break;
                case ExpectationOperator.Less: Upper(expectation.Operand, false); break;
                case ExpectationOperator.LessOrEqual: Upper(expectation.Operand, true); break;
                case ExpectationOperator.Between: Lower(expectation.Operand, false); Upper(expectation.Upper!, false); break;
            }
        }

        private void Lower(QualityValue value, bool inclusive)
        {
            var comparison = _lower is null ? 1 : TypedComparison.Compare(value, _lower);
            if (comparison > 0) { _lower = value; _lowerInclusive = inclusive; }
            else if (comparison == 0)
            {
                _lowerInclusive &= inclusive;
            }
        }
        private void Upper(QualityValue value, bool inclusive)
        {
            var comparison = _upper is null ? -1 : TypedComparison.Compare(value, _upper);
            if (comparison < 0) { _upper = value; _upperInclusive = inclusive; }
            else if (comparison == 0)
            {
                _upperInclusive &= inclusive;
            }
        }

        internal string? ValidateConsistency(IReadOnlyList<QualityExpectation> expectations)
        {
            if (_lower is null || _upper is null) return null;
            var comparison = TypedComparison.Compare(_lower, _upper);
            if (comparison > 0 || (comparison == 0 && !(_lowerInclusive && _upperInclusive))) return "Conflicting expectation bounds.";
            if (comparison == 0 && expectations.Any(e => !ExpectationAssertionPlan.Matches(_lower, e))) return "Conflicting expectations.";
            if (expectations.Any(e => e.Operator == ExpectationOperator.NotBetween
                && TypedComparison.Compare(_lower, e.Operand) > 0 && TypedComparison.Compare(_upper, e.Upper!) < 0))
                return "Conflicting range exclusions.";
            return null;
        }
    }
}
