using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

public class DscParseOptionsTests
{
    [Fact]
    public void Constructor_ShouldSetDefaultValues()
    {
        var options = new DscParseOptions();

        Assert.False(options.IncludeComments);
        Assert.True(options.IncludeCIMInstanceInfo);
        Assert.Null(options.Schema);
    }

    [Fact]
    public void IncludeComments_ShouldBeSettable()
    {
        var options = new DscParseOptions { IncludeComments = true };

        Assert.True(options.IncludeComments);
    }

    [Fact]
    public void IncludeCIMInstanceInfo_ShouldBeSettable()
    {
        var options = new DscParseOptions { IncludeCIMInstanceInfo = false };

        Assert.False(options.IncludeCIMInstanceInfo);
    }

    [Fact]
    public void Schema_ShouldBeSettable()
    {
        var options = new DscParseOptions { Schema = "SomeSchema" };

        Assert.Equal("SomeSchema", options.Schema);
    }
}
