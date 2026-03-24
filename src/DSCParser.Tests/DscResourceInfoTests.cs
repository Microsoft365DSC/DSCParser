using Xunit;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;
using ImplementedAsType = Microsoft.PowerShell.DesiredStateConfiguration.ImplementedAsType;

namespace DSCParser.Tests;

public class DscResourceInfoTests
{
    #region Constructor / Default State

    [Fact]
    public void Constructor_ShouldInitializePropertiesAsList()
    {
        var info = new DscResourceInfo();

        Assert.NotNull(info.Properties);
        Assert.Empty(info.Properties);
    }

    [Fact]
    public void Constructor_ShouldHaveNullModuleName()
    {
        var info = new DscResourceInfo();

        Assert.Null(info.ModuleName);
    }

    [Fact]
    public void Constructor_ShouldHaveNullVersion()
    {
        var info = new DscResourceInfo();

        Assert.Null(info.Version);
    }

    #endregion

    #region Property Getters/Setters

    [Fact]
    public void Name_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { Name = "TestResource" };

        Assert.Equal("TestResource", info.Name);
    }

    [Fact]
    public void ResourceType_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { ResourceType = "MSFT_TestResource" };

        Assert.Equal("MSFT_TestResource", info.ResourceType);
    }

    [Fact]
    public void FriendlyName_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { FriendlyName = "TestFriendly" };

        Assert.Equal("TestFriendly", info.FriendlyName);
    }

    [Fact]
    public void Path_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { Path = @"C:\DSC\test.psm1" };

        Assert.Equal(@"C:\DSC\test.psm1", info.Path);
    }

    [Fact]
    public void ParentPath_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { ParentPath = @"C:\DSC" };

        Assert.Equal(@"C:\DSC", info.ParentPath);
    }

    [Fact]
    public void ImplementedAs_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { ImplementedAs = ImplementedAsType.PowerShell };

        Assert.Equal(ImplementedAsType.PowerShell, info.ImplementedAs);
    }

    [Fact]
    public void CompanyName_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { CompanyName = "Microsoft" };

        Assert.Equal("Microsoft", info.CompanyName);
    }

    [Fact]
    public void ImplementationDetail_ShouldBeSettableAndGettable()
    {
        var info = new DscResourceInfo { ImplementationDetail = "ScriptBased" };

        Assert.Equal("ScriptBased", info.ImplementationDetail);
    }

    #endregion

    #region ImplementedAsType Enum

    [Fact]
    public void ImplementedAsType_None_ShouldBeZero()
    {
        Assert.Equal(0, (int)ImplementedAsType.None);
    }

    [Fact]
    public void ImplementedAsType_PowerShell_ShouldBeOne()
    {
        Assert.Equal(1, (int)ImplementedAsType.PowerShell);
    }

    [Fact]
    public void ImplementedAsType_Binary_ShouldBeTwo()
    {
        Assert.Equal(2, (int)ImplementedAsType.Binary);
    }

    [Fact]
    public void ImplementedAsType_Composite_ShouldBeThree()
    {
        Assert.Equal(3, (int)ImplementedAsType.Composite);
    }

    #endregion

    #region UpdateProperties

    [Fact]
    public void UpdateProperties_WithDscResourcePropertyInfoList_ShouldSetProperties()
    {
        var info = new DscResourceInfo();
        var props = new List<DscResourcePropertyInfo>
        {
            new() { Name = "Prop1", PropertyType = "[String]", IsMandatory = true },
            new() { Name = "Prop2", PropertyType = "[Int32]", IsMandatory = false }
        };

        info.UpdateProperties(props);

        Assert.Equal(2, info.Properties.Count);
    }

    [Fact]
    public void UpdateProperties_WithObjectList_ShouldSetProperties()
    {
        var info = new DscResourceInfo();
        var props = new List<object>
        {
            new DscResourcePropertyInfo { Name = "Prop1", PropertyType = "[String]" }
        };

        info.UpdateProperties(props);

        Assert.Single(info.Properties);
    }

    [Fact]
    public void PropertiesAsResourceInfo_ShouldReturnConvertedList()
    {
        var info = new DscResourceInfo();
        var props = new List<DscResourcePropertyInfo>
        {
            new() { Name = "Ensure", PropertyType = "[String]", IsMandatory = true },
            new() { Name = "Path", PropertyType = "[String]", IsMandatory = false }
        };
        info.UpdateProperties(props);

        var result = info.PropertiesAsResourceInfo;

        Assert.Equal(2, result.Count);
        Assert.Equal("Ensure", result[0].Name);
        Assert.Equal("Path", result[1].Name);
    }

    #endregion
}
