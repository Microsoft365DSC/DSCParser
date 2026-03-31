using System.Collections;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

public class DscParserConvertFromDscObjectTests
{
    #region ConvertFromDscObject - Basic Scenarios

    [Fact]
    public void ConvertFromDscObject_WithResourceNameAndInstanceName_ShouldFormatCorrectly()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["ResourceName"] = "MSFT_TestResource",
                ["ResourceInstanceName"] = "TestInstance",
                ["Ensure"] = "Present"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries);

        Assert.Contains("MSFT_TestResource \"TestInstance\"", result);
        Assert.Contains("Ensure", result);
        Assert.Contains("\"Present\"", result);
    }

    [Fact]
    public void ConvertFromDscObject_WithCIMInstance_ShouldFormatCorrectly()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "MSFT_Credential",
                ["UserName"] = "admin"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, childLevel: 1);

        Assert.Contains("MSFT_Credential{", result);
        Assert.Contains("UserName", result);
    }

    [Fact]
    public void ConvertFromDscObject_WithNeitherResourceNorCIMInstance_ShouldUseHashtableSyntax()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["Key1"] = "Value1"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries);

        Assert.Contains("@{", result);
    }

    #endregion

    #region ConvertFromDscObject - Property Types

    [Fact]
    public void ConvertFromDscObject_StringProperty_ShouldBeQuoted()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Name"] = "TestValue"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("\"TestValue\"", result);
    }

    [Fact]
    public void ConvertFromDscObject_StringWithSpecialChars_ShouldBeEscaped()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Name"] = "Value with \"quotes\" and `backticks`"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("`\"", result);
        Assert.Contains("``", result);
    }

    [Fact]
    public void ConvertFromDscObject_VariableString_ShouldNotBeQuoted()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Credential"] = "$ConfigData.Credential"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("= $ConfigData.Credential", result);
        Assert.DoesNotContain("\"$ConfigData.Credential\"", result);
    }

    [Fact]
    public void ConvertFromDscObject_IntegerProperty_ShouldNotBeQuoted()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Port"] = 443
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("= 443", result);
    }

    [Fact]
    public void ConvertFromDscObject_BooleanProperty_ShouldHaveDollarPrefix()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Enabled"] = true
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("= $True", result);
    }

    [Fact]
    public void ConvertFromDscObject_EmptyArrayProperty_ShouldFormatAsEmptyArray()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Items"] = Array.Empty<object>()
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("@()", result);
    }

    [Fact]
    public void ConvertFromDscObject_SingleElementArray_ShouldFormatInline()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Items"] = new object[] { "value1" }
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("@(\"value1\")", result);
    }

    [Fact]
    public void ConvertFromDscObject_MultiElementArray_ShouldFormatMultiLine()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Items"] = new object[] { "value1", "value2", "value3" }
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("@(", result);
        Assert.Contains("\"value1\"", result);
        Assert.Contains("\"value2\"", result);
        Assert.Contains("\"value3\"", result);
    }

    [Fact]
    public void ConvertFromDscObject_NewObjectString_ShouldNotBeQuoted()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Cred"] = "New-Object PSCredential('user', (ConvertTo-SecureString 'pass' -AsPlainText -Force))"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("= New-Object", result);
    }

    #endregion

    #region ConvertFromDscObject - Child Levels / Indentation

    [Fact]
    public void ConvertFromDscObject_ChildLevel0_ShouldHaveNoIndentation()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["ResourceName"] = "MSFT_Test",
                ["ResourceInstanceName"] = "Instance1",
                ["Ensure"] = "Present"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 0);

        // At child level 0, resource line starts at col 0
        Assert.StartsWith("MSFT_Test", result);
    }

    [Fact]
    public void ConvertFromDscObject_ChildLevel1_ShouldHave4SpacesIndentation()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "MSFT_Cred",
                ["UserName"] = "admin"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.StartsWith("    ", result);
    }

    #endregion

    #region ConvertFromDscObject - Nested Hashtables

    [Fact]
    public void ConvertFromDscObject_NestedCIMInstance_ShouldFormatRecursively()
    {
        var childHt = new Hashtable
        {
            ["CIMInstance"] = "MSFT_Inner",
            ["InnerProp"] = "InnerValue"
        };

        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "MSFT_Outer",
                ["NestedResource"] = childHt
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("MSFT_Outer", result);
        Assert.Contains("MSFT_Inner{", result);
        Assert.Contains("InnerProp", result);
    }

    [Fact]
    public void ConvertFromDscObject_RegularHashtable_ShouldUseAtSignSyntax()
    {
        var ht = new Hashtable
        {
            ["Key1"] = "Value1"
        };

        var entries = new List<Hashtable>
        {
            new()
            {
                ["CIMInstance"] = "Test",
                ["Config"] = ht
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 1);

        Assert.Contains("@", result);
    }

    #endregion

    #region ConvertFromDscObject - Skipped Parameters

    [Fact]
    public void ConvertFromDscObject_AtChildLevel0_ShouldSkipResourceName()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["ResourceName"] = "MSFT_Test",
                ["ResourceInstanceName"] = "Instance1",
                ["Ensure"] = "Present"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 0);

        // ResourceName should appear in the header line but not as a property
        var lines = result.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(lines, l => l.Trim().StartsWith("ResourceName ") && l.Contains('='));
    }

    [Fact]
    public void ConvertFromDscObject_ShouldAlwaysSkipResourceInstanceNameProperty()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["ResourceName"] = "MSFT_Test",
                ["ResourceInstanceName"] = "Instance1",
                ["Ensure"] = "Present"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 0);

        var lines = result.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(lines, l => l.Trim().StartsWith("ResourceInstanceName ") && l.Contains('='));
    }

    [Fact]
    public void ConvertFromDscObject_ShouldAlwaysEndWithNewLine()
    {
        var entries = new List<Hashtable>
        {
            new()
            {
                ["ResourceName"] = "MSFT_Test",
                ["ResourceInstanceName"] = "Instance1",
                ["Ensure"] = "Present"
            }
        };

        string result = DscParser.ConvertFromDscObject(entries, 0);

        Assert.EndsWith(Environment.NewLine, result);
    }

    #endregion
}
