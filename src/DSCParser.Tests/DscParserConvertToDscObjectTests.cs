using DSCParser.CSharp;
using Xunit;

namespace DSCParser.Tests;

public class DscParserConvertToDscObjectTests
{
    #region ConvertToDscObject - Argument Validation

    [Fact]
    public void ConvertToDscObject_WithNoDscResourcesAndNullParam_ShouldThrowInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DscParser.ConvertToDscObject(dscResources: null));

        Assert.Contains("No DSC resources loaded", ex.Message);
    }

    [Fact]
    public void ConvertToDscObject_WithEmptyPathAndContent_ShouldThrowArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DscParser.ConvertToDscObject(path: "", content: "", dscResources: []));

        Assert.Contains("Either path or content must be provided", ex.Message);
    }

    [Fact]
    public void ConvertToDscObject_WithNullPathAndEmptyContent_ShouldThrowArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            DscParser.ConvertToDscObject(path: null, content: "", dscResources: []));

        Assert.Contains("Either path or content must be provided", ex.Message);
    }

    #endregion
}
