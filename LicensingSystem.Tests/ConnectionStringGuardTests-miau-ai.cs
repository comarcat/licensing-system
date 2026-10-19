using System;
using LicensingCore.Configuration;
using Xunit;

namespace LicensingSystem.Tests;

public class ConnectionStringGuardTests
{
    [Fact]
    public void Require_Null_Throws_InvalidOperationException_Naming_Both_Hints()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConnectionStringGuard.Require(null));

        Assert.Contains("dotnet user-secrets", ex.Message);
        Assert.Contains("ConnectionStrings__LicensingDb", ex.Message);
    }

    [Fact]
    public void Require_EmptyString_Throws_InvalidOperationException_Naming_Both_Hints()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConnectionStringGuard.Require(""));

        Assert.Contains("dotnet user-secrets", ex.Message);
        Assert.Contains("ConnectionStrings__LicensingDb", ex.Message);
    }

    [Fact]
    public void Require_WhitespaceOnly_Throws_InvalidOperationException_Naming_Both_Hints()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConnectionStringGuard.Require("   "));

        Assert.Contains("dotnet user-secrets", ex.Message);
        Assert.Contains("ConnectionStrings__LicensingDb", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [InlineData("   \t  ")]
    public void Require_BlankValues_Throw_InvalidOperationException_Naming_Both_Hints(string? blank)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConnectionStringGuard.Require(blank));

        Assert.Contains("dotnet user-secrets", ex.Message);
        Assert.Contains("ConnectionStrings__LicensingDb", ex.Message);
    }

    [Fact]
    public void Require_NonBlankValue_Returns_Exact_Same_String()
    {
        const string connectionString = "Host=db;Database=x";

        var result = ConnectionStringGuard.Require(connectionString);

        Assert.Equal("Host=db;Database=x", result);
        Assert.Same(connectionString, result);
    }
}
