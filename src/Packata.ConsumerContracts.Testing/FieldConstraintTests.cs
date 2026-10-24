using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.ConsumerContracts.Testing;

public class FieldConstraintTests
{
    private static ConsumerContract Requirement(Action<FieldRequirementBuilder> configure) =>
        ConsumerContract.For("report").Dataset("orders", d => d.Field("value", configure)).Build();
    private static ContractSatisfaction Evaluate(DataField produced, Action<FieldRequirementBuilder> configure) =>
        ConsumerContractTests.Producer(produced).Satisfies(Requirement(configure));

    [TestCase(false, false, SatisfactionStatus.Satisfied)]
    [TestCase(false, true, SatisfactionStatus.Unsatisfied)]
    [TestCase(true, false, SatisfactionStatus.Satisfied)]
    [TestCase(true, true, SatisfactionStatus.Satisfied)]
    [TestCase(false, null, SatisfactionStatus.Unknown)]
    public void Nullability_is_directional(bool required, bool? produced, SatisfactionStatus expected)
    {
        var result = Evaluate(new("value", "string", Nullable: produced), f => f.Type(LogicalType.String).Nullable(required));
        Assert.That(result.Results.Single(x => x.Constraint == "nullable").Status, Is.EqualTo(expected));
    }

    [TestCase(30, SatisfactionStatus.Satisfied)]
    [TestCase(50, SatisfactionStatus.Satisfied)]
    [TestCase(100, SatisfactionStatus.Unsatisfied)]
    [TestCase(null, SatisfactionStatus.Unknown)]
    public void String_length_is_directional(int? length, SatisfactionStatus expected)
    {
        var result = Evaluate(new("value", "string", Length: length), f => f.Type(LogicalType.String).Length(50));
        Assert.That(result.Results.Single(x => x.Constraint == "length").Status, Is.EqualTo(expected));
    }

    [TestCase(18, 2, SatisfactionStatus.Satisfied, SatisfactionStatus.Satisfied)]
    [TestCase(18, 4, SatisfactionStatus.Satisfied, SatisfactionStatus.Unsatisfied)]
    [TestCase(18, 0, SatisfactionStatus.Unsatisfied, SatisfactionStatus.Satisfied)]
    [TestCase(null, null, SatisfactionStatus.Unknown, SatisfactionStatus.Unknown)]
    public void Decimal_capacity_compares_integral_digits_and_scale(int? precision, int? scale,
        SatisfactionStatus precisionStatus, SatisfactionStatus scaleStatus)
    {
        var result = Evaluate(new("value", "number", Precision: precision, Scale: scale),
            f => f.Type(LogicalType.Decimal).Precision(18).Scale(2));
        Assert.That(result.Results.Single(x => x.Constraint == "precision").Status, Is.EqualTo(precisionStatus));
        Assert.That(result.Results.Single(x => x.Constraint == "scale").Status, Is.EqualTo(scaleStatus));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void All_parameters_round_trip(bool json)
    {
        var requirement = Requirement(f => f.Type(LogicalType.Decimal).Nullable(false).Precision(18).Scale(0));
        var text = ConsumerContractSerializer.Serialize(requirement, json);
        var copy = ConsumerContractSerializer.Deserialize(text, json);
        Assert.That(copy.Datasets[0].Fields[0].Nullable, Is.False);
        Assert.That(copy.Datasets[0].Fields[0].Scale, Is.Zero);
        Assert.That(ConsumerContractSerializer.Serialize(copy, json), Is.EqualTo(text));
        var str = Requirement(f => f.Type(LogicalType.String).Length(0));
        Assert.That(ConsumerContractSerializer.Deserialize(ConsumerContractSerializer.Serialize(str, json), json)
            .Datasets[0].Fields[0].Length, Is.Zero);
    }

    [Test]
    public void Failed_type_blocks_all_dependent_constraints()
    {
        var result = Evaluate(new("value", "string", Nullable: false, Precision: 18, Scale: 2),
            f => f.Type(LogicalType.Decimal).Nullable(false).Precision(18).Scale(2));
        Assert.That(result.Results.Where(x => x.Constraint is "nullable" or "precision" or "scale")
            .All(x => x.Status == SatisfactionStatus.Unknown), Is.True);
    }

    [Test]
    public void Invalid_type_parameters_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Requirement(f => f.Type(LogicalType.Boolean).Length(50)));
        Assert.Throws<ArgumentException>(() => Requirement(f => f.Type(LogicalType.String).Precision(18)));
        Assert.Throws<ArgumentException>(() => Requirement(f => f.Type(LogicalType.Decimal).Precision(2).Scale(3)));
        Assert.Throws<ArgumentException>(() => Requirement(f => f.Length(50)));
        Assert.Throws<ArgumentException>(() => Requirement(f => f.Type(LogicalType.String).Length(-1)));
    }

    private sealed class CustomPolicy : ILogicalTypeCompatibilityPolicy
    {
        public int Calls { get; private set; }
        public SatisfactionStatus Evaluate(LogicalType required, string? produced) => SatisfactionStatus.Satisfied;
        public SatisfactionStatus EvaluateParameter(IFieldRequirement required, DataField produced, string parameter)
        { Calls++; return SatisfactionStatus.Unsatisfied; }
    }

    [Test]
    public void Decimal_parameters_use_configured_policy()
    {
        var policy = new CustomPolicy();
        var result = ConsumerContractTests.Producer(new DataField("value", "decimal", Precision: 18, Scale: 2))
            .Satisfies(Requirement(f => f.Type(LogicalType.Decimal).Precision(18).Scale(2)), policy);
        Assert.That(policy.Calls, Is.EqualTo(2));
        Assert.That(result.IsSatisfied, Is.False);
    }
}
