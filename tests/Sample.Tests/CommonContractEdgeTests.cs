using Sample.Planning;

namespace Sample.Tests;

public sealed class CommonContractEdgeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void When_CallerBypassesDomainMeasurementValidation_Should_CoreStillRejectInvalidUnits(int units)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            WorkPlanBuilder.Build([new("item", units, 0)], 5, 2));

        Assert.Equal("items", error.ParamName);
    }

    [Fact]
    public void When_CommonScopeOrElementIsNull_Should_RejectExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => WorkPlanBuilder.Build(null!, 5, 2));
        Assert.Throws<ArgumentException>(() => WorkPlanBuilder.Build([null!], 5, 2));
    }
}
