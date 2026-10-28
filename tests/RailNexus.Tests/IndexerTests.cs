using RailNexus.App.Core;
using RailNexus.Core;
using RailNexus.Core.Library;
using RailNexus.Core.Process;
using RailNexus.Trainz;

namespace RailNexus.Tests;

public sealed class IndexerTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "rn-idx-" + Guid.NewGuid().ToString("N")[..8] + ".db");

    public void Dispose()
    {
        foreach (var f in new[] { _db, _db + "-wal", _db + "-shm" }) try { File.Delete(f); } catch (IOException) { }
    }

    private sealed class StubRunner(Func<IReadOnlyList<string>, RawOutput> respond) : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public Task<RawOutput> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        {
            Calls.Add(args);
            return Task.FromResult(respond(args) with { Arguments = args });
        }
    }

    private static RawOutput Text(string s) => new("TrainzUtil.exe", [], 0, s, "", TimeSpan.Zero, false);

    /// <summary>Profil uji; status massal sudah terverifikasi di profil nyata, pencarian/printconfig sesuai parameter.</summary>
    private static CommandProfile Profile(bool readCommandsVerified) => new("uji", "uji", "", new Dictionary<TrainzOp, CommandSpec>
    {
        [TrainzOp.Status] = new(["status", "{kuid}"], true),
        [TrainzOp.StatusBatch] = new(["filterbystatus", "I"], true),
        [TrainzOp.SearchByCategory] = new(["searchbycategory", "{arg}"], readCommandsVerified),
        [TrainzOp.PrintConfig] = new(["printconfig", "{kuid}"], readCommandsVerified),
    });

    private static TrainzUtilAdapter Adapter(bool verified, StubRunner runner) =>
        new(new TrainzUtilOptions("TrainzUtil.exe", Profile(verified), new HashSet<(int, int)>()), runner);

    [Fact]
    public void Real_searchbycategory_output_yields_only_hit_lines()
    {
        var kuids = TrainzOutputParsers.SearchCategory(OutputClassifierTests.Fixture("searchbycategory-VE"));
        Assert.Equal(Kuid.Parse("<kuid:942117:23>"), kuids[0]);
        Assert.Contains(Kuid.Parse("<kuid2:68213:29018:2>"), kuids);
        Assert.True(kuids.Count >= 30); // fixture dipotong 40 baris
        Assert.NotEmpty(TrainzOutputParsers.SearchCategory(OutputClassifierTests.Fixture("searchbycategory-TV")));
    }

    [Fact]
    public void Real_printconfig_dump_is_extracted_and_error_output_is_null()
    {
        var text = TrainzOutputParsers.PrintConfig(OutputClassifierTests.Fixture("printconfig-after-install"));
        Assert.NotNull(text);
        Assert.StartsWith("kuid ", text);
        Assert.Contains("kind                                    \"texture\"", text);
        Assert.Null(TrainzOutputParsers.PrintConfig(OutputClassifierTests.Fixture("printconfig-test-asset")));
    }

    [Fact]
    public async Task Unverified_search_is_unknown_and_index_is_untouched()
    {
        // Bentuk sama dengan searchbycategory-VE.json, lengkap dengan baris penutup (fixture dipotong 40 baris).
        var runner = new StubRunner(_ => Text("; <NULL> : searchbycategory \"VE\"\n  + <NULL> : 1 : assets matching the specified criteria\n" +
                                              "  + <kuid:942117:23> : + #0 : <kuid:942117:23>\nOK (0 Errors, 0 Warnings)\n"));
        using var lib = new LibraryIndex(_db, "uji");
        var indexer = new TrainzLibraryIndexer(Adapter(false, runner), lib);
        var r = await indexer.IndexAsync(["VE"]);
        Assert.False(r.Ok);
        Assert.Contains("belum terverifikasi", r.Message);
        Assert.Contains("railnexus index", r.Message);
        Assert.Equal(0, lib.Count());
        Assert.Equal(["searchbycategory", "VE"], runner.Calls.Single());
    }

    [Fact]
    public async Task Verified_profile_indexes_search_hits_through_status_batch()
    {
        var progress = new List<string>();
        var runner = new StubRunner(args => args[0] == "searchbycategory"
            ? Text("; <NULL> : searchbycategory \"VE\"\n  + <NULL> : 2 : assets matching the specified criteria\n" +
                   "  + <kuid:942117:23> : + #0 : <kuid:942117:23>\n  + <kuid2:68213:29018:2> : + #1 : <kuid2:68213:29018:2>\nOK (0 Errors, 0 Warnings)\n")
            : Text("; <NULL> : filterbystatus\n+ <kuid:942117:23> : aBcdefGIlmoPuXY : VE;LND : Chrysler 300 Black\n" +
                   "+ <kuid2:68213:29018:2> : aBcDefGIlmopuXY : VE;LND : Truck shotfirer's 02\nOK (0 Errors, 0 Warnings)\n"));
        using var lib = new LibraryIndex(_db, "uji");
        var r = await new TrainzLibraryIndexer(Adapter(true, runner), lib).IndexAsync(["VE"], new SyncProgress(progress));
        Assert.True(r.Ok, r.Message);
        Assert.Equal(2, r.Stored);
        Assert.Equal("Chrysler 300 Black", lib.Get(Kuid.Parse("<kuid:942117:23>"))!.Name);
        Assert.Equal(["filterbystatus", "I", "<kuid:942117:23>", "<kuid2:68213:29018:2>"], runner.Calls[1]);
        Assert.Contains(progress, p => p.Contains("2/2"));
    }

    [Fact]
    public async Task Kind_is_read_from_printconfig_and_kept_across_upserts()
    {
        var asset = Kuid.Parse("<kuid:-2:117104311>");
        var runner = new StubRunner(_ => OutputClassifierTests.Fixture("printconfig-after-install"));
        using var lib = new LibraryIndex(_db, "uji");
        var now = DateTimeOffset.UtcNow;
        lib.Upsert([new LibraryEntry(asset, "uji", "CMP", "x", AssetState.Installed, false, false, false, false, now)]);

        var kind = await new TrainzLibraryIndexer(Adapter(true, runner), lib).ReadKindAsync(asset);
        Assert.Equal("texture", kind.Value);
        Assert.Equal("texture", lib.Get(asset)!.Kind);
        // Pembaruan status tanpa KIND tidak menghapus KIND yang sudah terbaca.
        lib.Upsert([new LibraryEntry(asset, "uji 2", "CMP", "x", AssetState.Installed, false, false, false, false, now)]);
        Assert.Equal("texture", lib.Get(asset)!.Kind);

        var unverified = await new TrainzLibraryIndexer(Adapter(false, runner), lib).ReadKindAsync(asset);
        Assert.Equal(Outcome.Unknown, unverified.Outcome);
        Assert.Contains("belum terverifikasi", unverified.Message);
    }

    private sealed class SyncProgress(List<string> sink) : IProgress<string>
    {
        public void Report(string value) => sink.Add(value);
    }
}
