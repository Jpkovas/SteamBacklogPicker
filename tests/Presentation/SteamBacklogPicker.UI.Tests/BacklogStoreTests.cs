using System.IO;
using System.Text.Json;
using Domain;
using FluentAssertions;
using SteamBacklogPicker.UI.Services.Library;
using Xunit;

namespace SteamBacklogPicker.UI.Tests;

public sealed class BacklogStoreTests : IDisposable
{
    private const string AccountA = "76561198000000000";
    private const string AccountB = "76561198000000001";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BacklogStoreTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "backlog.json");

    public BacklogStoreTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{ broken")]
    [InlineData("{\"76561198000000000\":null}")]
    [InlineData("{\"76561198000000000\":{\"Games\":null,\"History\":null}}")]
    public void Constructor_ShouldRecoverFromMalformedOrNullDocuments(string json)
    {
        File.WriteAllText(FilePath, json);
        var store = new BacklogStore(FilePath);
        store.GetStatus(AccountA, 10).Should().Be(BacklogStatus.None);
        store.GetHistory(AccountA).Should().BeEmpty();
        store.SetStatus(AccountA, 10, BacklogStatus.Playing);
        new BacklogStore(FilePath).GetStatus(AccountA, 10).Should().Be(BacklogStatus.Playing);
    }

    [Fact]
    public void Constructor_ShouldDiscardOnlyInvalidEntriesAndKeepValidDecisions()
    {
        File.WriteAllText(FilePath, """
        {"invalid-account":{"Games":{"10":4}},"76561198000000000":{
          "Games":{"0":2,"10":3,"20":99,"30":-1,"40":"Playing","bad":2},
          "AvoidRepeats":"false",
          "History":[null,{}, {"AppId":0,"Title":"Bad","SelectedAt":"2026-01-01T00:00:00Z"},
            {"AppId":20,"Title":null,"SelectedAt":"2026-01-01T00:00:00Z"},
            {"AppId":30,"Title":"Bad","SelectedAt":"not-a-date"},
            {"AppId":10,"Title":"Good","SelectedAt":"2026-01-01T00:00:00Z"}]
        }}
        """);
        var store = new BacklogStore(FilePath);
        store.GetStatus(AccountA, 10).Should().Be(BacklogStatus.Completed);
        foreach (var id in new[] { 0u, 20u, 30u, 40u }) store.GetStatus(AccountA, id).Should().Be(BacklogStatus.None);
        store.GetHistory(AccountA).Should().ContainSingle().Which.Title.Should().Be("Good");
        store.GetAvoidRepeats(AccountA).Should().BeTrue();
    }

    [Fact]
    public void DecisionsAndHistory_ShouldPersistSeparatelyForEachAccount()
    {
        var store = new BacklogStore(FilePath);
        store.SetStatus(AccountA, 10, BacklogStatus.Completed);
        store.SetStatus(AccountB, 10, BacklogStatus.WantToPlay);
        store.SetStatus("local", 10, BacklogStatus.Hidden);
        store.RecordDraw(AccountA, Game(10));
        store.RecordDraw(AccountB, Game(20));
        store.SetAvoidRepeats(AccountA, false);
        var reloaded = new BacklogStore(FilePath);
        reloaded.GetStatus(AccountA, 10).Should().Be(BacklogStatus.Completed);
        reloaded.GetStatus(AccountB, 10).Should().Be(BacklogStatus.WantToPlay);
        reloaded.GetStatus("local", 10).Should().Be(BacklogStatus.Hidden);
        reloaded.GetHistory(AccountA).Should().ContainSingle().Which.AppId.Should().Be(10);
        reloaded.GetHistory(AccountB).Should().ContainSingle().Which.AppId.Should().Be(20);
        reloaded.GetAvoidRepeats(AccountA).Should().BeFalse();
        reloaded.GetAvoidRepeats(AccountB).Should().BeTrue();
        reloaded.ClearHistory(AccountA);
        reloaded.SetStatus(AccountA, 10, BacklogStatus.None);
        var final = new BacklogStore(FilePath);
        final.GetHistory(AccountA).Should().BeEmpty();
        final.GetHistory(AccountB).Should().ContainSingle();
        final.GetStatus(AccountA, 10).Should().Be(BacklogStatus.None);
        Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        document.RootElement.GetProperty(AccountA).GetProperty("Games").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void RecordDraw_ShouldKeepNewestTwoHundredAndReturnIndependentHistorySnapshots()
    {
        var store = new BacklogStore();
        for (uint id = 1; id <= 205; id++) store.RecordDraw(AccountA, Game(id));
        var history = store.GetHistory(AccountA);
        history.Should().HaveCount(200);
        history.First().AppId.Should().Be(205);
        history.Last().AppId.Should().Be(6);
        ((BacklogHistory[])history)[0] = new(999, "Mutated caller copy", DateTimeOffset.UtcNow);
        store.GetHistory(AccountA).First().AppId.Should().Be(205);
    }

    [Fact]
    public void Constructor_ShouldEnforceHistoryLimitWhenLoadingExistingDocuments()
    {
        var entries = Enumerable.Range(1, 250).Select(id => new BacklogHistory((uint)id, "Game", DateTimeOffset.UtcNow)).ToList();
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new Dictionary<string, BacklogAccount>
        {
            [AccountA] = new() { History = entries }
        }));
        var history = new BacklogStore(FilePath).GetHistory(AccountA);
        history.Should().HaveCount(200);
        history.First().AppId.Should().Be(1);
        history.Last().AppId.Should().Be(200);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("history")]
    [InlineData("avoid")]
    public void FailedCommit_ShouldPreservePreviousFileAndMemoryAndCleanTemporaryFile(string operation)
    {
        var store = new BacklogStore(FilePath);
        store.SetStatus(AccountA, 10, BacklogStatus.Playing);
        var previous = File.ReadAllText(FilePath);
        using (var locked = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Action update = operation switch
            {
                "status" => () => store.SetStatus(AccountA, 10, BacklogStatus.Completed),
                "history" => () => store.RecordDraw(AccountA, Game(10)),
                _ => () => store.SetAvoidRepeats(AccountA, false)
            };
            var error = Record.Exception(update);
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            store.GetStatus(AccountA, 10).Should().Be(BacklogStatus.Playing);
            store.GetHistory(AccountA).Should().BeEmpty();
            store.GetAvoidRepeats(AccountA).Should().BeTrue();
            File.ReadAllText(FilePath).Should().Be(previous);
            Directory.GetFiles(_directory, "*.tmp").Should().BeEmpty();
        }
        store.SetStatus(AccountA, 10, BacklogStatus.Completed);
        new BacklogStore(FilePath).GetStatus(AccountA, 10).Should().Be(BacklogStatus.Completed);
    }

    [Fact]
    public void Mutations_ShouldIgnoreInvalidIdentifiersAndStatuses()
    {
        var store = new BacklogStore(FilePath);
        foreach (var account in new string?[] { null, "", "../other", "123", "local ", "-1" })
        {
            store.SetStatus(account!, 10, BacklogStatus.Completed);
            store.SetAvoidRepeats(account!, false);
            store.RecordDraw(account!, Game(10));
            store.ClearHistory(account!);
            store.GetStatus(account!, 10).Should().Be(BacklogStatus.None);
            store.GetHistory(account!).Should().BeEmpty();
        }
        store.SetStatus(AccountA, 0, BacklogStatus.Playing);
        store.SetStatus(AccountA, 10, (BacklogStatus)99);
        store.RecordDraw(AccountA, Game(0));
        store.RecordDraw(AccountA, new GameEntry { Id = GameIdentifier.Unknown });
        File.Exists(FilePath).Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldIgnoreOversizedFileAndAllowAValidReplacement()
    {
        using (var stream = File.Create(FilePath)) stream.SetLength(4 * 1024 * 1024 + 1);
        var store = new BacklogStore(FilePath);
        store.GetHistory(AccountA).Should().BeEmpty();
        store.SetStatus(AccountA, 10, BacklogStatus.Later);
        new FileInfo(FilePath).Length.Should().BeLessThan(4 * 1024 * 1024);
        new BacklogStore(FilePath).GetStatus(AccountA, 10).Should().Be(BacklogStatus.Later);
    }

    [Fact]
    public void ConcurrentUpdates_ShouldNotLoseDecisions()
    {
        var store = new BacklogStore();
        Parallel.For(1, 100, id => store.SetStatus(AccountA, (uint)id, BacklogStatus.Playing));
        for (uint id = 1; id < 100; id++) store.GetStatus(AccountA, id).Should().Be(BacklogStatus.Playing);
    }

    private static GameEntry Game(uint id) => new() { Id = GameIdentifier.ForSteam(id), Title = "Game " + id };

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
