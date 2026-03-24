using Microsoft.PowerShell.DesiredStateConfiguration.V2;
using Xunit;

namespace DSCParser.Tests;

public class StringExtensionsTests
{
    [Fact]
    public void Contains_WithOrdinalIgnoreCase_ShouldMatchCaseInsensitively()
    {
        string source = "Hello World";

        Assert.True(source.Contains("hello", StringComparison.OrdinalIgnoreCase));
        Assert.True(source.Contains("HELLO", StringComparison.OrdinalIgnoreCase));
        Assert.True(source.Contains("Hello", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contains_WithOrdinal_ShouldMatchCaseSensitively()
    {
        string source = "Hello World";

        Assert.True(source.Contains("Hello", StringComparison.Ordinal));
        Assert.False(source.Contains("hello", StringComparison.Ordinal));
    }

    [Fact]
    public void Contains_WithSubstring_ShouldReturnTrue()
    {
        string source = "SomeFile.schema.mof";

        Assert.True(source.Contains(".schema.mof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contains_WithNonExistentSubstring_ShouldReturnFalse()
    {
        string source = "Hello World";

        Assert.False(source.Contains("xyz", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contains_WithEmptySearchString_ShouldReturnTrue()
    {
        string source = "Hello World";

        Assert.True(source.Contains("", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contains_WithFullMatch_ShouldReturnTrue()
    {
        string source = "ExactMatch";

        Assert.True(source.Contains("ExactMatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contains_SchemaMof_ShouldMatchCaseInsensitively()
    {
        Assert.True("TestResource.Schema.Mof".Contains(".schema.mof", StringComparison.OrdinalIgnoreCase));
        Assert.True("TestResource.SCHEMA.MOF".Contains(".schema.mof", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Contains_SchemaPsm1_ShouldMatchCaseInsensitively()
    {
        Assert.True("TestResource.Schema.Psm1".Contains(".schema.psm1", StringComparison.OrdinalIgnoreCase));
    }
}
