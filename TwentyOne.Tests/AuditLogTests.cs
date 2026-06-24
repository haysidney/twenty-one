using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TwentyOne.Game;
using Xunit;

namespace TwentyOne.Tests;

public class AuditLogTests : IDisposable
{
    private readonly string dir;
    private readonly string? savedRoot;

    public AuditLogTests()
    {
        savedRoot = AuditLog.Root;
        dir = Path.Combine(Path.GetTempPath(), "TwentyOne-audit-test-" + Guid.NewGuid().ToString("N"));
        AuditLog.Root = dir;
    }

    public void Dispose()
    {
        AuditLog.Root = savedRoot;
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private JObject[] ReadLines(string venueId)
    {
        var path = Path.Combine(dir, $"{venueId}-{DateTime.Now:yyyy-MM-dd}.jsonl");
        return File.ReadAllLines(path)
            .Where(l => l.Length > 0)
            .Select(JObject.Parse)
            .ToArray();
    }

    [Fact]
    public void Bank_WritesExpectedShape()
    {
        AuditLog.Bank("venue1", "Lorah", "Withdrawal", 725000, 725000, 0, 1_000_000);
        var line = Assert.Single(ReadLines("venue1"));
        Assert.Equal("bank", (string?)line["kind"]);
        Assert.Equal("Lorah", (string?)line["player"]);
        Assert.Equal("Withdrawal", (string?)line["op"]);
        Assert.Equal(725000, (long)line["amount"]!);
        Assert.Equal(725000, (long)line["balanceBefore"]!);
        Assert.Equal(0, (long)line["balanceAfter"]!);
        Assert.Equal(1_000_000, (long)line["dealerGil"]!);
        Assert.NotNull((string?)line["t"]);
    }

    [Fact]
    public void Wallet_RecordsDeltaAndTotal()
    {
        AuditLog.Wallet("venue1", 1_250_000, 250_000);
        var line = Assert.Single(ReadLines("venue1"));
        Assert.Equal("wallet", (string?)line["kind"]);
        Assert.Equal(1_250_000, (long)line["gil"]!);
        Assert.Equal(250_000, (long)line["delta"]!);
    }

    [Fact]
    public void Append_ProducesOneLinePerEvent()
    {
        AuditLog.Trade("venue1", "Bekki", 0, 500, "Deposit", 500);
        AuditLog.Prompt("venue1", "Deposit", "Bekki", 500, "Confirm");
        var lines = ReadLines("venue1");
        Assert.Equal(2, lines.Length);
        Assert.Equal("trade", (string?)lines[0]["kind"]);
        Assert.Equal("prompt", (string?)lines[1]["kind"]);
    }

    [Fact]
    public void NullRoot_NoOps()
    {
        AuditLog.Root = null;
        AuditLog.Bank("venue1", "Lorah", "Deposit", 1, 0, 1, 0); // must not throw
        Assert.False(Directory.Exists(dir) && Directory.EnumerateFiles(dir).Any());
    }
}
