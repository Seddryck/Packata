using NUnit.Framework;
using Packata.Core.Contracts;

namespace Packata.Core.Testing.Contracts;

public class ServiceCommitmentsTests
{
    [Test]
    public void Durations_compare_by_calendar_months_and_elapsed_ticks()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ContractDuration(0, TimeSpan.FromHours(2)),
                Is.EqualTo(new ContractDuration(0, TimeSpan.FromMinutes(120))));
            Assert.That(new ContractDuration(1, TimeSpan.Zero),
                Is.Not.EqualTo(new ContractDuration(0, TimeSpan.FromDays(30))));
        });
    }

    [Test]
    public void Lifecycle_compares_instants_independently_of_offsets()
    {
        var utc = new Lifecycle(GeneralAvailability: new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var offset = new Lifecycle(GeneralAvailability: new DateTimeOffset(2028, 1, 1, 1, 0, 0, TimeSpan.FromHours(1)));
        Assert.That(offset, Is.EqualTo(utc));
    }
}
