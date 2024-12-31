using FluentAssertions;

namespace GitSnapshotter.UnitTests;

public class Tests
{
    [Fact]
    public void Test1()
    {
        true.Should().BeTrue();
    }
}