using System.Collections;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

public class DscResourceInstanceTests
{
    #region Constructor / Default State

    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        var instance = new DscResourceInstance();

        Assert.Equal(string.Empty, instance.ResourceName);
        Assert.Equal(string.Empty, instance.ResourceInstanceName);
        Assert.NotNull(instance.Properties);
        Assert.Empty(instance.Properties);
    }

    #endregion

    #region AddProperty

    [Fact]
    public void AddProperty_ShouldAddNewProperty()
    {
        var instance = new DscResourceInstance();

        instance.AddProperty("Ensure", "Present");

        Assert.Single(instance.Properties);
        Assert.Equal("Present", instance.Properties["Ensure"]);
    }

    [Fact]
    public void AddProperty_ShouldOverwriteExistingProperty()
    {
        var instance = new DscResourceInstance();
        instance.AddProperty("Ensure", "Present");

        instance.AddProperty("Ensure", "Absent");

        Assert.Single(instance.Properties);
        Assert.Equal("Absent", instance.Properties["Ensure"]);
    }

    [Fact]
    public void AddProperty_ShouldAcceptNullValue()
    {
        var instance = new DscResourceInstance();

        instance.AddProperty("NullProp", null);

        Assert.Single(instance.Properties);
        Assert.Null(instance.Properties["NullProp"]);
    }

    private static readonly string[] value = ["a", "b"];

    [Fact]
    public void AddProperty_ShouldAcceptVariousTypes()
    {
        var instance = new DscResourceInstance();

        instance.AddProperty("StringProp", "value");
        instance.AddProperty("IntProp", 42);
        instance.AddProperty("BoolProp", true);
        instance.AddProperty("ArrayProp", value);

        Assert.Equal(4, instance.Properties.Count);
        Assert.Equal("value", instance.Properties["StringProp"]);
        Assert.Equal(42, instance.Properties["IntProp"]);
        Assert.Equal(true, instance.Properties["BoolProp"]);
    }

    #endregion

    #region GetProperty

    [Fact]
    public void GetProperty_ShouldReturnValueWhenKeyExists()
    {
        var instance = new DscResourceInstance();
        instance.AddProperty("Ensure", "Present");

        var result = instance.GetProperty("Ensure");

        Assert.Equal("Present", result);
    }

    [Fact]
    public void GetProperty_ShouldReturnNullWhenKeyDoesNotExist()
    {
        var instance = new DscResourceInstance();

        var result = instance.GetProperty("NonExistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetProperty_ShouldReturnNullValueWhenKeyExistsWithNullValue()
    {
        var instance = new DscResourceInstance();
        instance.AddProperty("NullProp", null);

        var result = instance.GetProperty("NullProp");

        Assert.Null(result);
    }

    #endregion

    #region ToHashtable

    [Fact]
    public void ToHashtable_ShouldIncludeResourceNameAndInstanceName()
    {
        var instance = new DscResourceInstance
        {
            ResourceName = "MSFT_TestResource",
            ResourceInstanceName = "TestInstance"
        };

        Hashtable result = instance.ToHashtable();

        Assert.Equal("MSFT_TestResource", result["ResourceName"]);
        Assert.Equal("TestInstance", result["ResourceInstanceName"]);
    }

    [Fact]
    public void ToHashtable_ShouldIncludeSimpleProperties()
    {
        var instance = new DscResourceInstance
        {
            ResourceName = "MSFT_TestResource",
            ResourceInstanceName = "TestInstance"
        };
        instance.AddProperty("Ensure", "Present");
        instance.AddProperty("Count", 5);
        instance.AddProperty("Enabled", true);

        Hashtable result = instance.ToHashtable();

        Assert.Equal("Present", result["Ensure"]);
        Assert.Equal(5, result["Count"]);
        Assert.Equal(true, result["Enabled"]);
    }

    [Fact]
    public void ToHashtable_ShouldConvertNestedDscResourceInstance()
    {
        var child = new DscResourceInstance
        {
            ResourceName = "MSFT_ChildResource",
            ResourceInstanceName = "ChildInstance"
        };
        child.AddProperty("ChildProp", "ChildValue");

        var parent = new DscResourceInstance
        {
            ResourceName = "MSFT_ParentResource",
            ResourceInstanceName = "ParentInstance"
        };
        parent.AddProperty("Child", child);

        Hashtable result = parent.ToHashtable();
        var childHt = result["Child"] as Hashtable;

        Assert.NotNull(childHt);
        Assert.Equal("MSFT_ChildResource", childHt["ResourceName"]);
        Assert.Equal("ChildInstance", childHt["ResourceInstanceName"]);
        Assert.Equal("ChildValue", childHt["ChildProp"]);
    }

    [Fact]
    public void ToHashtable_ShouldConvertDictionaryToHashtable()
    {
        var instance = new DscResourceInstance
        {
            ResourceName = "MSFT_TestResource",
            ResourceInstanceName = "TestInstance"
        };
        var dict = new Dictionary<string, object?>
        {
            ["Key1"] = "Value1",
            ["Key2"] = 42
        };
        instance.AddProperty("DictProp", dict);

        Hashtable result = instance.ToHashtable();
        var dictHt = result["DictProp"] as Hashtable;

        Assert.NotNull(dictHt);
        Assert.Equal("Value1", dictHt["Key1"]);
        Assert.Equal(42, dictHt["Key2"]);
    }

    [Fact]
    public void ToHashtable_ShouldConvertEnumerableToArray()
    {
        var instance = new DscResourceInstance
        {
            ResourceName = "MSFT_TestResource",
            ResourceInstanceName = "TestInstance"
        };
        var list = new List<object> { "a", "b", "c" };
        instance.AddProperty("ListProp", list);

        Hashtable result = instance.ToHashtable();
        var arrayResult = result["ListProp"] as object[];

        Assert.NotNull(arrayResult);
        Assert.Equal(3, arrayResult.Length);
        Assert.Equal("a", arrayResult[0]);
        Assert.Equal("b", arrayResult[1]);
        Assert.Equal("c", arrayResult[2]);
    }

    [Fact]
    public void ToHashtable_ShouldHandleNullPropertyValues()
    {
        var instance = new DscResourceInstance
        {
            ResourceName = "MSFT_TestResource",
            ResourceInstanceName = "TestInstance"
        };
        instance.AddProperty("NullProp", null);

        Hashtable result = instance.ToHashtable();

        Assert.True(result.ContainsKey("NullProp"));
        Assert.Null(result["NullProp"]);
    }

    [Fact]
    public void ToHashtable_ShouldNotConvertStringToArray()
    {
        var instance = new DscResourceInstance
        {
            ResourceName = "MSFT_TestResource",
            ResourceInstanceName = "TestInstance"
        };
        instance.AddProperty("StringProp", "hello");

        Hashtable result = instance.ToHashtable();

        Assert.IsType<string>(result["StringProp"]);
        Assert.Equal("hello", result["StringProp"]);
    }

    [Fact]
    public void ToHashtable_ShouldConvertDeeplyNestedStructures()
    {
        var grandchild = new DscResourceInstance
        {
            ResourceName = "MSFT_GrandChild",
            ResourceInstanceName = "GC1"
        };
        grandchild.AddProperty("Level", 3);

        var childList = new List<object> { grandchild };

        var parent = new DscResourceInstance
        {
            ResourceName = "MSFT_Parent",
            ResourceInstanceName = "P1"
        };
        parent.AddProperty("Children", childList);

        Hashtable result = parent.ToHashtable();
        var children = result["Children"] as object[];

        Assert.NotNull(children);
        Assert.Single(children);
        var gc = children[0] as Hashtable;
        Assert.NotNull(gc);
        Assert.Equal(3, gc["Level"]);
    }

    #endregion
}
