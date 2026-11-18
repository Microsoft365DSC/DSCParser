using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;
using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

public class DscParserLiteralEmitTests
{
    private static readonly DscEmitOptions LiteralOptions = new() { IsLiteralOnly = true, NewLine = "\n" };

    [Fact]
    public void ConvertFromDscObject_InLiteralMode_ShouldQuoteCodeAsLiteralsAndKeepExportReferencesLive()
    {
        Hashtable resource = new()
        {
            ["ResourceName"] = "ContosoPolicy",
            ["ResourceInstanceName"] = "Corp \"main\"",
            ["AppId"] = "$ConfigurationData.NonNodeData.ApplicationId",
            ["Assignment"] = new Hashtable { ["CIMInstance"] = "MSFT_ContosoAssignment", ["Target"] = "$(Remove-Item x)" },
            ["Code"] = "$(Get-Date)",
            ["Count"] = 3,
            ["Credential"] = "$Credscredential",
            ["Dollar"] = "cost $5",
            ["Enabled"] = true,
            ["Live"] = "$($OrganizationName.Split('.')[0])-admins",
            ["Names"] = new object[] { "$foo", "plain" },
            ["Plain"] = "Corp policy",
            ["Quote"] = "it's",
            ["Smart"] = "“quoted”",
            ["Tick"] = "a`b",
        };

        DscEmitResult result = DscParser.ConvertFromDscObject([resource], LiteralOptions);

        Assert.Null(result.Refusal);
        Assert.Equal(
            [
                "ContosoPolicy 'Corp \"main\"'",
                "{",
                Line("AppId", "$ConfigurationData.NonNodeData.ApplicationId"),
                Line("Assignment", "MSFT_ContosoAssignment{"),
                "        Target      = '$(Remove-Item x)'",
                "    }",
                Line("Code", "'$(Get-Date)'"),
                Line("Count", "3"),
                Line("Credential", "$Credscredential"),
                Line("Dollar", "'cost $5'"),
                Line("Enabled", "$True"),
                Line("Live", "\"$($OrganizationName.Split('.')[0])-admins\""),
                Line("Names", "@("),
                "        '$foo'",
                "        \"plain\"",
                "    )",
                Line("Plain", "\"Corp policy\""),
                Line("Quote", "\"it's\""),
                Line("Smart", "'“quoted”'"),
                Line("Tick", "\"a``b\""),
                "}",
                "",
            ],
            result.Configuration.Split('\n'));
        Assert.DoesNotContain('\r', result.Configuration);
        AssertLiteralsReadBack(result.Configuration, ["Corp \"main\"", "$(Remove-Item x)", "$(Get-Date)", "cost $5", "$foo", "“quoted”"]);
    }

    [Fact]
    public void ConvertFromDscObject_WithOptions_ShouldWriteNoResourceNameLineInsideABlock()
    {
        Hashtable resource = new() { ["ResourceName"] = "ContosoPolicy", ["ResourceInstanceName"] = "Corp", ["Plain"] = "a" };

        string legacy = DscParser.ConvertFromDscObject([resource], childLevel: 2);
        DscEmitResult emitted = DscParser.ConvertFromDscObject([resource], new DscEmitOptions(), childLevel: 2);

        Assert.Contains("ResourceName", legacy, StringComparison.Ordinal);
        Assert.Equal(legacy.Replace("            ResourceName         = \"ContosoPolicy\"" + Environment.NewLine, string.Empty), emitted.Configuration);
    }

    public static TheoryData<string, object, string?, string?> RefusedResources() => new()
    {
        { "ResourceInstanceName", "first\nsecond", "first\nsecond", null },
        { "ResourceName", "Contoso-Policy", "Corp", null },
        { "Bad key", "value", "Corp", "Bad key" },
        { "Settings", new Hashtable { ["Name"] = "value" }, "Corp", "Settings" },
        { "Assignment", new Hashtable { ["CIMInstance"] = "MSFT_ContosoAssignment", ["bad-key"] = "x" }, "Corp", "Assignment.bad-key" },
        { "Script", ScriptBlock.Create("Remove-Item x"), "Corp", "Script" },
        { "CIMInstance", "MSFT_ContosoAssignment", "Corp", "CIMInstance" },
    };

    [Theory]
    [MemberData(nameof(RefusedResources))]
    public void ConvertFromDscObject_InLiteralMode_ShouldRefuseAValueNoLiteralHolds(
        string key, object value, string? refusedInstance, string? refusedSetting)
    {
        Hashtable resource = new() { ["ResourceName"] = "ContosoPolicy", ["ResourceInstanceName"] = "Corp" };
        resource[key] = value;

        DscEmitResult result = DscParser.ConvertFromDscObject([resource], LiteralOptions);

        Assert.NotNull(result.Refusal);
        Assert.Equal((refusedInstance, refusedSetting), (result.Refusal.InstanceName, result.Refusal.Setting));
        Assert.Empty(result.Configuration);
    }

    private static string Line(string key, string value) => $"    {key}{new string(' ', 21 - key.Length)}= {value}";

    private static void AssertLiteralsReadBack(string configuration, string[] values)
    {
        _ = Parser.ParseInput(configuration, out Token[] tokens, out ParseError[] errors);

        Assert.Empty(errors);
        Assert.Equal(
            values,
            tokens.OfType<StringToken>().Where(token => token.Kind == TokenKind.StringLiteral).Select(token => token.Value));
    }
}
