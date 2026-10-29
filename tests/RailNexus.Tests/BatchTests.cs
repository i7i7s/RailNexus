using System.Text;
using RailNexus.Core;
using RailNexus.Core.Batch;
using RailNexus.Core.Tx;

namespace RailNexus.Tests;

public sealed class BatchTests : IDisposable
{
    private static readonly Kuid OldHorn = Kuid.Parse("<kuid:999002:3101>");
    private readonly string _root = Directory.CreateTempSubdirectory("rn batch ").FullName;
    private readonly SimulatedTrainz _sim = new();

    public void Dispose()
    {
        _sim.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static string Config(int id, string horn) =>
        $"kuid <kuid:999001:{id}>\r\nhornsound {horn}\r\nkuid-table\r\n{{\r\n  horn {horn}\r\n}}\r\n";

    private static ReplacePreset Preset => new("Klakson KL-3", "hornsound", "<kuid:999002:3101>", "<kuid:999002:3103>");

    [Fact]
    public void Planner_separates_ready_needs_choice_and_skipped()
    {
        var uses = Encoding.UTF8.GetBytes(Config(1, "<kuid:999002:3101>"));
        var other = Encoding.UTF8.GetBytes(Config(2, "<kuid:999002:9999>"));
        Assert.Equal(BatchItemState.Ready, BatchPlanner.PlanReplace(new Kuid(999001, 1, null), "a", uses, null, Preset, true).State);
        Assert.Equal(BatchItemState.NeedsChoice, BatchPlanner.PlanReplace(new Kuid(999001, 1, null), "a", uses, null, Preset, false).State);
        var skipped = BatchPlanner.PlanReplace(new Kuid(999001, 2, null), "b", other, null, Preset, true);
        Assert.Equal(BatchItemState.Skipped, skipped.State);
        Assert.Contains("Tidak memakai", skipped.Detail);
        Assert.Equal(BatchItemState.Skipped, BatchPlanner.PlanReplace(new Kuid(999001, 3, null), "c", null, "terbuka", Preset, true).State);
        Assert.NotNull((Preset with { RoleId = "tidak-ada" }).Problem);
        Assert.NotNull((Preset with { To = Preset.From }).Problem);
    }

    [Fact]
    public async Task Failure_on_fifth_asset_keeps_four_successes_AC15()
    {
        var assets = Enumerable.Range(1, 6).Select(i => new Kuid(999001, i, null)).ToList();
        foreach (var (k, i) in assets.Select((k, i) => (k, i + 1)))
            _sim.Install(k, new() { ["config.txt"] = Encoding.UTF8.GetBytes(Config(i, "<kuid:999002:3101>")) });
        var plans = assets.Select((k, i) => BatchPlanner.PlanReplace(k, $"aset {i + 1}", _sim.InstalledFile(k, "config.txt"), null, Preset, true)).ToList();
        Assert.All(plans, p => Assert.Equal(BatchItemState.Ready, p.State));

        _sim.OpenExternally(assets[4]); // aset kelima sedang dibuka editor lain
        var runner = new TransactionRunner(_sim, new TxStore(Path.Combine(_root, "tx")));
        var stop = new CancellationTokenSource();
        var seen = new List<BatchResultItem>();
        var results = await new BatchRunner(runner).RunAsync(plans, new SyncProgress(seen, r => { if (r.Name == "aset 5") stop.Cancel(); }), stop.Token);

        Assert.Equal(6, results.Count);
        Assert.All(results.Take(4), r => Assert.Equal(TxStatus.Succeeded, r.Result!.Status));
        Assert.Equal(TxStatus.Blocked, results[4].Result!.Status);
        Assert.Null(results[5].Result); // dihentikan setelah aset kelima selesai
        Assert.Contains("dihentikan", results[5].Message);
        foreach (var k in assets.Take(4))
            Assert.Contains("<kuid:999002:3103>", Encoding.UTF8.GetString(_sim.InstalledFile(k, "config.txt")!));
        Assert.Contains("horn <kuid:999002:3103>", Encoding.UTF8.GetString(_sim.InstalledFile(assets[0], "config.txt")!));
        Assert.DoesNotContain("3103", Encoding.UTF8.GetString(_sim.InstalledFile(assets[5], "config.txt")!));
    }

    [Fact]
    public void Preset_round_trips_and_future_versions_are_refused()
    {
        var path = Path.Combine(_root, "presets", "kl3.json");
        Preset.Save(path);
        Assert.Equal(Preset, ReplacePreset.Load(path));
        Assert.Contains("versi aturan", (Preset with { Version = 99 }).Problem);
        File.WriteAllText(path, "{rusak");
        Assert.Null(ReplacePreset.Load(path));
    }

    private sealed class SyncProgress(List<BatchResultItem> sink, Action<BatchResultItem> then) : IProgress<BatchResultItem>
    {
        public void Report(BatchResultItem value) { sink.Add(value); then(value); }
    }
}
