using System.Diagnostics;
using RailNexus.Core;
using RailNexus.Core.Library;
using RailNexus.Core.Process;
using RailNexus.Trainz;

namespace RailNexus.Tests;

public sealed class LibraryTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rn-lib-").FullName;
    private string Db => Path.Combine(_dir, "library.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static LibraryEntry E(string kuid, string name, AssetState state = AssetState.Installed, string cat = "TV", bool missing = false) =>
        new(Kuid.Parse(kuid), name, cat, "x", state, missing, false, false, false, DateTimeOffset.UtcNow);

    [Fact]
    public void Search_by_name_and_by_kuid_AC35()
    {
        using var lib = new LibraryIndex(Db, "trs19-utama");
        lib.Upsert([E("<kuid:999001:1201>", "Lokomotif Diesel DX-200"), E("<kuid2:999001:1202:3>", "Gerbong Barang GB-40")]);

        Assert.Equal("Lokomotif Diesel DX-200", Assert.Single(lib.Search(new LibraryQuery("diesel"))).Name);
        Assert.Equal("Gerbong Barang GB-40", Assert.Single(lib.Search(new LibraryQuery("<kuid:999001:1202>"))).Name);
        Assert.Equal(2, lib.Search(new LibraryQuery()).Count);
    }

    [Fact]
    public void Like_wildcards_in_user_text_are_literal()
    {
        using var lib = new LibraryIndex(Db, "p");
        lib.Upsert([E("<kuid:1:1>", "Rel 100%"), E("<kuid:1:2>", "Rel 1000")]);
        Assert.Single(lib.Search(new LibraryQuery("100%")));
        Assert.Empty(lib.Search(new LibraryQuery("_")));
    }

    [Fact]
    public void Filters_by_problem_state()
    {
        using var lib = new LibraryIndex(Db, "p");
        lib.Upsert([
            E("<kuid:1:1>", "Sehat"),
            E("<kuid:1:2>", "Rusak", AssetState.Faulty),
            E("<kuid:1:3>", "Kurang komponen", missing: true),
            E("<kuid:1:4>", "Sedang diedit", AssetState.OpenForEdit),
        ]);
        Assert.Equal("Rusak", Assert.Single(lib.Search(new LibraryQuery(Filter: LibraryFilter.Faulty))).Name);
        Assert.Equal("Kurang komponen", Assert.Single(lib.Search(new LibraryQuery(Filter: LibraryFilter.MissingDependencies))).Name);
        Assert.Equal("Sedang diedit", Assert.Single(lib.Search(new LibraryQuery(Filter: LibraryFilter.OpenForEdit))).Name);
    }

    [Fact]
    public void Same_kuid_in_two_profiles_stays_separate_CONNECT01_AC21()
    {
        using (var a = new LibraryIndex(Db, "trs19"))
            a.Upsert([E("<kuid:5:5>", "Versi di TRS19")]);
        using (var b = new LibraryIndex(Db, "trs22"))
            b.Upsert([E("<kuid:5:5>", "Versi di TRS22", AssetState.Faulty)]);

        using var a2 = new LibraryIndex(Db, "trs19");
        var only = Assert.Single(a2.Search(new LibraryQuery("<kuid:5:5>")));
        Assert.Equal("Versi di TRS19", only.Name);
        Assert.Equal(AssetState.Installed, only.State);
    }

    [Fact]
    public void Upsert_updates_existing_record()
    {
        using var lib = new LibraryIndex(Db, "p");
        lib.Upsert([E("<kuid:1:1>", "Lama")]);
        lib.Upsert([E("<kuid:1:1>", "Baru", AssetState.Faulty)]);
        var e = lib.Get(Kuid.Parse("<kuid:1:1>"))!;
        Assert.Equal("Baru", e.Name);
        Assert.Equal(AssetState.Faulty, e.State);
        Assert.Equal(1, lib.Count());
    }

    [Fact]
    public void Category_filter_matches_first_segment()
    {
        using var lib = new LibraryIndex(Db, "p");
        lib.Upsert([E("<kuid:1:1>", "A", cat: "CMP;TX"), E("<kuid:1:2>", "B", cat: "TV")]);
        Assert.Equal("A", Assert.Single(lib.Search(new LibraryQuery(Category: "CMP"))).Name);
    }

    /// <summary>Target PRD 25: pencarian cache p95 â‰¤ 300 ms pada 50.000 catatan (mesin uji ini).</summary>
    [Fact]
    public void Search_p95_on_50k_records_within_target()
    {
        using var lib = new LibraryIndex(Db, "bench");
        var rnd = new Random(42);
        string[] words = ["Lokomotif", "Gerbong", "Rel", "Stasiun", "Klakson", "Bogie", "Diesel", "Listrik", "Barang", "Penumpang"];
        lib.Upsert(Enumerable.Range(0, 50_000).Select(i => E(
            $"<kuid:{100000 + i % 977}:{i}>",
            $"{words[rnd.Next(words.Length)]} {words[rnd.Next(words.Length)]} {i}",
            i % 97 == 0 ? AssetState.Faulty : AssetState.Installed)));
        Assert.Equal(50_000, lib.Count());

        string[] queries = ["diesel", "gerbong barang", "123", "<kuid:100005:5>", "rel", "zzz", "klakson", "4999"];
        var times = new List<double>();
        var worst = new Dictionary<string, double>();
        for (var round = 0; round < 5; round++)
        foreach (var q in queries)
        foreach (var filter in new[] { LibraryFilter.All, LibraryFilter.Faulty })
        {
            var sw = Stopwatch.StartNew();
            lib.Search(new LibraryQuery(q, filter));
            var ms = sw.Elapsed.TotalMilliseconds;
            times.Add(ms);
            var key = $"{q}/{filter}";
            worst[key] = Math.Max(worst.GetValueOrDefault(key), ms);
        }
        times.Sort();
        var p95 = times[(int)Math.Ceiling(times.Count * 0.95) - 1];
        output.WriteLine($"Benchmark pustaka 50.000 catatan: p95 = {p95:0.0} ms, median = {times[times.Count / 2]:0.0} ms, maks = {times[^1]:0.0} ms");
        Assert.True(p95 <= 300, $"p95 = {p95:0.0} ms; terlambat: " +
            string.Join(", ", worst.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key}={kv.Value:0}ms")));
    }

    [Fact]
    public async Task Status_is_read_one_kuid_per_call()
    {
        // Build 117026 hanya memproses KUID pertama dari 'status <KUID> [..]' (status-batch-VE.json).
        var runner = new StubRunner(args => args[1] switch
        {
            "<kuid:-2:117104311>" => "; <NULL> : status x\n+ <kuid:-2:117104311> : abcdefgILmopuXY : CMP;TX : RailNexus aset uji\nOK (0 Errors, 0 Warnings)\n",
            _ => "; <NULL> : status x\n- <kuid:1:1> : Unknown KUID: <kuid:1:1>\nOK (1 Errors, 0 Warnings)\n",
        });
        var profile = new CommandProfile("t", "t", "", new Dictionary<TrainzOp, CommandSpec> { [TrainzOp.Status] = new(["status", "{kuid}"], true) });
        var adapter = new TrainzUtilAdapter(new TrainzUtilOptions("TrainzUtil.exe", profile, new HashSet<(int, int)>()), runner);

        var r = await adapter.GetStatusesAsync([Kuid.Parse("<kuid:-2:117104311>"), Kuid.Parse("<kuid:1:1>")]);
        var e = Assert.Single(r.Value!);
        Assert.Equal("RailNexus aset uji", e.Name);
        Assert.Equal("CMP;TX", e.Category);
        Assert.Equal(AssetState.Installed, e.State);
        Assert.Equal(2, runner.Calls.Count);
        Assert.All(runner.Calls, c => Assert.Equal(2, c.Count));
    }

    [Fact]
    public async Task Status_batch_uses_filterbystatus_in_one_call()
    {
        var runner = new StubRunner(_ => string.Join("\n",
            "; <NULL> : filterbystatus \"I\" ...",
            "+ <kuid:942117:23> : aBcdefGIlmoPuXY : VE;LND;SY;SCEN : Chrysler 300 Black",
            "+ <kuid2:68213:29018:2> : aBcDefGIlmopuXY : VE;LND;SY;SCEN : Truck shotfirer's 02",
            "+ <kuid:-2:117104311> : abcdefgILmopuXY : CMP;TX : RailNexus aset uji",
            "OK (0 Errors, 0 Warnings)", ""));
        var profile = new CommandProfile("t", "t", "", new Dictionary<TrainzOp, CommandSpec>
        {
            [TrainzOp.Status] = new(["status", "{kuid}"], true),
            [TrainzOp.StatusBatch] = new(["filterbystatus", "I"], true),
        });
        var adapter = new TrainzUtilAdapter(new TrainzUtilOptions("TrainzUtil.exe", profile, new HashSet<(int, int)>()), runner);

        var kuids = new[] { "<kuid:942117:23>", "<kuid2:68213:29018:2>", "<kuid:-2:117104311>" }.Select(Kuid.Parse).ToList();
        var r = await adapter.GetStatusesAsync(kuids);
        Assert.Equal(3, r.Value!.Count);
        Assert.True(r.Value[0].Payware);
        Assert.Equal("Truck shotfirer's 02", r.Value[1].Name);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(["filterbystatus", "I", "<kuid:942117:23>", "<kuid2:68213:29018:2>", "<kuid:-2:117104311>"], call);
    }

    private sealed class StubRunner(Func<IReadOnlyList<string>, string> respond) : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public Task<RawOutput> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        {
            Calls.Add(args);
            return Task.FromResult(new RawOutput(exe, args, 0, respond(args), "", TimeSpan.Zero, false));
        }
    }
}
