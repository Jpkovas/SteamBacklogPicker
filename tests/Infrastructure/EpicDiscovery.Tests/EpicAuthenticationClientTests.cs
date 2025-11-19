using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using EpicDiscovery;
using FluentAssertions;
using Xunit;

namespace EpicDiscovery.Tests;

[Collection("EpicAuthenticationClientTests")]
public sealed class EpicAuthenticationClientTests : IDisposable
{
    private readonly List<string> directoriesToCleanup = new();

    [Fact]
    public void ExtractToken_ShouldParseRefreshTokenFromJson()
    {
        var token = InvokeExtractToken("{ \"refresh_token\": \"eg1~json-token\" }");
        token.Should().Be("eg1~json-token");
    }

    [Fact]
    public void ExtractToken_ShouldParseRefreshTokenFromLogLine()
    {
        var token = InvokeExtractToken("[Log] Refresh token eg1~log-token completed");
        token.Should().Be("eg1~log-token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldCacheSuccessfulToken()
    {
        CreateTokenFixtureFile("{ \"refresh_token\": \"eg1~refresh-json\" }");

        var callCount = 0;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Interlocked.Increment(ref callCount);
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.ToString().Should().Contain("oauth/token");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{ \"access_token\": \"eg1~access\" }", Encoding.UTF8, "application/json"),
            });
        });

        var client = new EpicAuthenticationClient(new HttpClient(handler), new TestFileAccessor());

        var first = await client.GetAccessTokenAsync();
        var second = await client.GetAccessTokenAsync();

        first.Should().Be("eg1~access");
        second.Should().Be("eg1~access");
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ShouldReturnNullWhenHttpRequestFails()
    {
        CreateTokenFixtureFile("{ \"refreshToken\": \"eg1~refresh-log\" }");

        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var client = new EpicAuthenticationClient(new HttpClient(handler), new TestFileAccessor());

        var token = await client.GetAccessTokenAsync();
        token.Should().BeNull();
    }

    private void CreateTokenFixtureFile(string content)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "webcache", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        directoriesToCleanup.Add(directory);
        File.WriteAllText(Path.Combine(directory, "token.json"), content);
    }

    private static string? InvokeExtractToken(string input)
    {
        var method = typeof(EpicAuthenticationClient)
            .GetMethod("ExtractToken", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string?)method.Invoke(null, new object[] { input });
    }

    public void Dispose()
    {
        foreach (var directory in directoriesToCleanup)
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
            }
        }
    }
}

[CollectionDefinition("EpicAuthenticationClientTests", DisableParallelization = true)]
public sealed class EpicAuthenticationClientCollectionDefinition
{
}
