using Xunit;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;

namespace DSCParser.Tests;

public class DscResourcePropertyInfoTests
{
    [Fact]
    public void Constructor_ShouldInitializeValuesAsEmptyList()
    {
        var prop = new DscResourcePropertyInfo();

        Assert.NotNull(prop.Values);
        Assert.Empty(prop.Values);
    }

    [Fact]
    public void Name_ShouldBeSettableAndGettable()
    {
        var prop = new DscResourcePropertyInfo { Name = "Ensure" };

        Assert.Equal("Ensure", prop.Name);
    }

    [Fact]
    public void PropertyType_ShouldBeSettableAndGettable()
    {
        var prop = new DscResourcePropertyInfo { PropertyType = "[String]" };

        Assert.Equal("[String]", prop.PropertyType);
    }

    [Fact]
    public void IsMandatory_ShouldDefaultToFalse()
    {
        var prop = new DscResourcePropertyInfo();

        Assert.False(prop.IsMandatory);
    }

    [Fact]
    public void IsMandatory_ShouldBeSettableToTrue()
    {
        var prop = new DscResourcePropertyInfo { IsMandatory = true };

        Assert.True(prop.IsMandatory);
    }

    [Fact]
    public void Values_ShouldBeSettable()
    {
        var prop = new DscResourcePropertyInfo
        {
            Values = ["Present", "Absent"]
        };

        Assert.Equal(2, prop.Values.Count);
        Assert.Contains("Present", prop.Values);
        Assert.Contains("Absent", prop.Values);
    }
}
