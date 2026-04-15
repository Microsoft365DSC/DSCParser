using System.Reflection;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

public class RemoveModuleVersionInfoTests
{
    private static readonly MethodInfo _removeModuleVersionInfo =
        typeof(DscParser).GetMethod("RemoveModuleVersionInfo", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("RemoveModuleVersionInfo method not found");

    private static string InvokeRemoveModuleVersionInfo(string content, List<string>? uniqueModules = null)
    {
        return (string)_removeModuleVersionInfo.Invoke(null, [content, uniqueModules])!;
    }

    #region Null / Empty Module List

    [Fact]
    public void NullModuleList_ShouldReturnContentUnchanged()
    {
        string content = "Import-DscResource -ModuleName TestModule -ModuleVersion 1.0.0.0";

        string result = InvokeRemoveModuleVersionInfo(content, null);

        Assert.Equal(content, result);
    }

    [Fact]
    public void EmptyModuleList_ShouldReturnContentUnchanged()
    {
        string content = "Import-DscResource -ModuleName TestModule -ModuleVersion 1.0.0.0";

        string result = InvokeRemoveModuleVersionInfo(content, []);

        Assert.Equal(content, result);
    }

    #endregion

    #region Basic Removal

    [Fact]
    public void ModuleVersionAfterModuleName_ShouldRemoveVersionParameter()
    {
        string content = "Import-DscResource -ModuleName TestModule -ModuleVersion 1.0.0.0";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal("Import-DscResource -ModuleName TestModule", result);
    }

    [Fact]
    public void ModuleVersionBeforeModuleName_ShouldRemoveVersionParameter()
    {
        string content = "Import-DscResource -ModuleVersion 1.0.0.0 -ModuleName TestModule";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal("Import-DscResource -ModuleName TestModule", result);
    }

    [Fact]
    public void QuotedVersion_ShouldRemoveVersionParameter()
    {
        string content = "Import-DscResource -ModuleName TestModule -ModuleVersion \"1.0.0.0\"";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal("Import-DscResource -ModuleName TestModule", result);
    }

    [Fact]
    public void SingleQuotedVersion_ShouldRemoveVersionParameter()
    {
        string content = "Import-DscResource -ModuleName TestModule -ModuleVersion '1.0.0.0'";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal("Import-DscResource -ModuleName TestModule", result);
    }

    #endregion

    #region Case Insensitivity

    [Fact]
    public void CaseInsensitiveImportDscResource_ShouldRemoveVersionParameter()
    {
        string content = "IMPORT-DSCRESOURCE -ModuleName TestModule -ModuleVersion 1.0.0.0";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal("IMPORT-DSCRESOURCE -ModuleName TestModule", result);
    }

    [Fact]
    public void CaseInsensitiveModuleVersion_ShouldRemoveVersionParameter()
    {
        string content = "Import-DscResource -ModuleName TestModule -MODULEVERSION 1.0.0.0";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal("Import-DscResource -ModuleName TestModule", result);
    }

    [Fact]
    public void CaseInsensitiveModuleName_ShouldMatchAndRemoveVersion()
    {
        string content = "Import-DscResource -ModuleName testmodule -ModuleVersion 1.0.0.0";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal("Import-DscResource -ModuleName testmodule", result);
    }

    #endregion

    #region Module Filtering

    [Fact]
    public void ModuleNotInList_ShouldNotRemoveVersion()
    {
        string content = "Import-DscResource -ModuleName OtherModule -ModuleVersion 2.0.0.0";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal(content, result);
    }

    [Fact]
    public void MultipleModulesOnlyMatchingOneRemoved_ShouldRemoveOnlyMatching()
    {
        string content =
            "Import-DscResource -ModuleName TestModule -ModuleVersion 1.0.0.0\n" +
            "Import-DscResource -ModuleName OtherModule -ModuleVersion 2.0.0.0";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Contains("Import-DscResource -ModuleName TestModule\n", result);
        Assert.Contains("Import-DscResource -ModuleName OtherModule -ModuleVersion 2.0.0.0", result);
    }

    [Fact]
    public void MultipleModulesInList_ShouldRemoveAllMatching()
    {
        string content =
            "Import-DscResource -ModuleName ModuleA -ModuleVersion 1.0.0.0\n" +
            "Import-DscResource -ModuleName ModuleB -ModuleVersion 2.0.0.0\n" +
            "Import-DscResource -ModuleName ModuleC -ModuleVersion 3.0.0.0";

        string result = InvokeRemoveModuleVersionInfo(content, ["ModuleA", "ModuleC"]);

        Assert.Contains("Import-DscResource -ModuleName ModuleA\n", result);
        Assert.Contains("Import-DscResource -ModuleName ModuleB -ModuleVersion 2.0.0.0", result);
        Assert.Contains("Import-DscResource -ModuleName ModuleC", result);
        Assert.DoesNotContain("ModuleA -ModuleVersion", result);
        Assert.DoesNotContain("ModuleC -ModuleVersion", result);
    }

    #endregion

    #region Multiline Content

    [Fact]
    public void MultilineContent_ShouldOnlyAffectImportDscResourceLines()
    {
        string content =
            "Configuration TestConfig\n" +
            "{\n" +
            "    Import-DscResource -ModuleName TestModule -ModuleVersion 1.0.0.0\n" +
            "    Node localhost\n" +
            "    {\n" +
            "    }\n" +
            "}";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Contains("Import-DscResource -ModuleName TestModule\n", result);
        Assert.Contains("Configuration TestConfig", result);
        Assert.Contains("Node localhost", result);
        Assert.DoesNotContain("-ModuleVersion", result);
    }

    [Fact]
    public void NoImportDscResource_ShouldReturnContentUnchanged()
    {
        string content =
            "Configuration TestConfig\n" +
            "{\n" +
            "    Node localhost { }\n" +
            "}";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal(content, result);
    }

    #endregion

    #region Realistic DSC Configuration

    [Fact]
    public void RealisticConfiguration_ShouldRemoveVersionFromMatchingModuleOnly()
    {
        string content =
            "Configuration M365TenantConfig\n" +
            "{\n" +
            "    Import-DscResource -ModuleName Microsoft365DSC -ModuleVersion 1.25.0401.1\n" +
            "    Import-DscResource -ModuleName PSDesiredStateConfiguration -ModuleVersion 1.1\n" +
            "\n" +
            "    Node localhost\n" +
            "    {\n" +
            "        EXOGroupSettings \"EXOGroupSettings-TestGroup\"\n" +
            "        {\n" +
            "            DisplayName = \"TestGroup\"\n" +
            "        }\n" +
            "    }\n" +
            "}";

        string result = InvokeRemoveModuleVersionInfo(content, ["Microsoft365DSC"]);

        Assert.Contains("Import-DscResource -ModuleName Microsoft365DSC\n", result);
        Assert.Contains("Import-DscResource -ModuleName PSDesiredStateConfiguration -ModuleVersion 1.1", result);
    }

    [Fact]
    public void VersionWithThreePartNumber_ShouldRemoveVersionParameter()
    {
        string content = "Import-DscResource -ModuleName TestModule -ModuleVersion 1.2.3";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal("Import-DscResource -ModuleName TestModule", result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ImportDscResourceWithoutModuleVersion_ShouldReturnUnchanged()
    {
        string content = "Import-DscResource -ModuleName TestModule";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal(content, result);
    }

    [Fact]
    public void ContentWithModuleVersionTextOutsideImport_ShouldNotModify()
    {
        string content =
            "# -ModuleVersion 1.0.0.0 is used here\n" +
            "$version = \"-ModuleVersion 5.0\"";

        string result = InvokeRemoveModuleVersionInfo(content, ["TestModule"]);

        Assert.Equal(content, result);
    }

    #endregion
}
