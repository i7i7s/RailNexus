using System.Text;
using RailNexus.Core;
using RailNexus.Core.Config;
using RailNexus.Core.Process;
using RailNexus.Trainz;

namespace RailNexus.Tests;

public class KuidTests
{
    [Theory]
    [InlineData("<kuid:999001:1201>", 999001, 1201, null)]
    [InlineData("<KUID2:999001:1201:3>", 999001, 1201, 3)]
    [InlineData(" <kuid:-25:1093> ", -25, 1093, null)]
    public void Parses_and_keeps_revision(string text, int user, int content, int? rev)
    {
        var k = Kuid.Parse(text);
        Assert.Equal(new Kuid(user, content, rev), k);
        Assert.Equal(k, Kuid.Parse(k.ToString()));
    }

    [Theory]
    [InlineData("kuid:1:2")]
    [InlineData("<kuid:a:2>")]
    [InlineData("")]
    public void Rejects_invalid(string text) => Assert.False(Kuid.TryParse(text, out _));
}

/// <summary>Uji terhadap keluaran nyata TrainzUtil TRS19 build 117026 di fixtures/trainzutil/trs19.</summary>
public class OutputClassifierTests
{
    internal static RawOutput Fixture(string name) =>
        Corpus.ToRaw(Corpus.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "trainzutil", "trs19", name + ".json")));

    private static RawOutput Raw(int? exit, string stdout, bool timedOut = false) =>
        new("TrainzUtil.exe", ["x"], exit, stdout, "", TimeSpan.Zero, timedOut);

    [Theory]
    [InlineData("not-running", OutputKind.TrainzNotRunning)]
    [InlineData("probe-version", OutputKind.Clean)]
    [InlineData("generatekuid", OutputKind.Clean)]
    [InlineData("unknown-command", OutputKind.Error)]
    [InlineData("status-invalid-kuid", OutputKind.Error)]
    [InlineData("install-test-asset", OutputKind.Error)]
    [InlineData("install-test-asset-nospace", OutputKind.Clean)]
    [InlineData("commit-test-asset", OutputKind.Clean)]
    [InlineData("validate-after-commit", OutputKind.Error)]
    public void Real_outputs_are_classified(string fixture, OutputKind expected) =>
        Assert.Equal(expected, OutputClassifier.Classify(Fixture(fixture)));

    [Fact]
    public void Exit_code_zero_does_not_mean_success()
    {
        var raw = Fixture("unknown-command");
        Assert.Equal(0, raw.ExitCode);
        Assert.Equal(OutputKind.Error, OutputClassifier.Classify(raw));
    }

    [Fact]
    public void Commit_warning_line_is_parsed()
    {
        var p = TuResponse.Parse(Fixture("commit-test-asset"));
        Assert.Equal(0, p.Errors);
        Assert.Equal(1, p.Warnings);
        Assert.Single(p.Of(LineKind.Warning));
        Assert.Contains(p.Of(LineKind.Result), l => l.Message.EndsWith("submitted"));
    }

    [Fact] public void Timeout_is_timeout() => Assert.Equal(OutputKind.TimedOut, OutputClassifier.Classify(Raw(null, "", true)));
    [Fact] public void Missing_trailer_is_unrecognised() => Assert.Equal(OutputKind.Unrecognized, OutputClassifier.Classify(Raw(0, "+ <NULL> : hasil")));
}

public class TrainzUtilAdapterTests
{
    private sealed class FakeRunner(RawOutput response) : IProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public Task<RawOutput> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct = default)
        {
            Calls.Add(args);
            return Task.FromResult(response with { Arguments = args });
        }
    }

    private static readonly Kuid TestAsset = Kuid.Parse("<kuid:-2:117104311>");

    private static CommandProfile Profile(bool verified) => new("uji", "uji", "",
        new Dictionary<TrainzOp, CommandSpec>
        {
            [TrainzOp.Status] = new(["status", "{kuid}"], verified),
            [TrainzOp.BeginEdit] = new(["edit", "{kuid}"], verified),
            [TrainzOp.Commit] = new(["commit", "{kuid}"], verified),
            [TrainzOp.Validate] = new(["validate", "{kuid}"], verified),
        });

    private static (TrainzUtilAdapter, FakeRunner) Make(bool verified, RawOutput response)
    {
        var runner = new FakeRunner(response);
        var adapter = new TrainzUtilAdapter(new TrainzUtilOptions("TrainzUtil.exe", Profile(verified),
            new HashSet<(int, int)> { (TestAsset.UserId, TestAsset.ContentId) }), runner);
        return (adapter, runner);
    }

    private static RawOutput Text(string stdout) => new("TrainzUtil.exe", [], 0, stdout, "", TimeSpan.Zero, false);

    [Fact]
    public async Task Unverified_write_command_never_runs()
    {
        var (adapter, runner) = Make(false, OutputClassifierTests.Fixture("commit-test-asset"));
        Assert.Equal(Outcome.NotSupported, (await adapter.CommitAsync(TestAsset)).Outcome);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Write_to_non_test_asset_never_runs()
    {
        var (adapter, runner) = Make(true, OutputClassifierTests.Fixture("commit-test-asset"));
        Assert.Equal(Outcome.NotSupported, (await adapter.CommitAsync(new Kuid(1, 2, null))).Outcome);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Unverified_read_is_unknown_not_success()
    {
        var (adapter, runner) = Make(false, OutputClassifierTests.Fixture("status-after-commit"));
        Assert.Equal(Outcome.Unknown, (await adapter.GetStatusAsync(TestAsset)).Outcome);
        Assert.Equal(["status", "<kuid:-2:117104311>"], runner.Calls.Single());
    }

    [Theory]
    [InlineData("status-after-install", AssetState.OpenForEdit, "abcdEfgILmopuXY")]
    [InlineData("status-after-commit", AssetState.Faulty, "abcdeFgILmopuXY")]
    [InlineData("status-test-asset", AssetState.Missing, "Unknown KUID")]
    public async Task Real_status_outputs_map_to_states(string fixture, AssetState state, string raw)
    {
        var (adapter, _) = Make(true, OutputClassifierTests.Fixture(fixture));
        var r = await adapter.GetStatusAsync(TestAsset);
        Assert.Equal(Outcome.Success, r.Outcome);
        Assert.Equal(state, r.Value!.State);
        Assert.Equal(raw, r.Value.RawState);
    }

    [Fact]
    public async Task Unparseable_status_is_unknown()
    {
        var (adapter, _) = Make(true, Text("+ <kuid:-2:117104311> : sesuatu yang baru\nOK (0 Errors, 0 Warnings)\n"));
        Assert.Equal(Outcome.Unknown, (await adapter.GetStatusAsync(TestAsset)).Outcome);
    }

    [Fact]
    public async Task Commit_without_submitted_line_is_unknown()
    {
        var (adapter, _) = Make(true, Text("; <NULL> : commit x\nOK (0 Errors, 0 Warnings)\n"));
        Assert.Equal(Outcome.Unknown, (await adapter.CommitAsync(TestAsset)).Outcome);
    }

    [Fact]
    public async Task Real_commit_output_is_success()
    {
        var (adapter, _) = Make(true, OutputClassifierTests.Fixture("commit-test-asset"));
        Assert.Equal(Outcome.Success, (await adapter.CommitAsync(TestAsset)).Outcome);
    }

    [Fact]
    public async Task Real_validate_errors_become_report()
    {
        var (adapter, _) = Make(true, OutputClassifierTests.Fixture("validate-after-commit"));
        var r = await adapter.ValidateAsync(TestAsset);
        Assert.Equal(Outcome.Success, r.Outcome);
        Assert.Equal(6, r.Value!.Errors.Count);
        Assert.StartsWith("VE45", r.Value.Errors[0]);
    }

    [Fact]
    public async Task Edit_folder_from_real_output_shape_must_exist()
    {
        var dir = Directory.CreateTempSubdirectory("rn edit ").FullName;
        try
        {
            var shape = $"; <NULL> : edit \"<kuid:-2:117104311>\"\n; <NULL> : + <kuid:-2:117104311> : {dir.Replace('\\', '/')}/\nOK (0 Errors, 0 Warnings)\n";
            var (ok, _) = Make(true, Text(shape));
            Assert.Equal(Path.GetFullPath(dir), (await ok.BeginEditAsync(TestAsset)).Value!.TrimEnd(Path.DirectorySeparatorChar));

            var (missing, _) = Make(true, Text("; <NULL> : + <kuid:-2:117104311> : C:/tidak/ada/folder/\nOK (0 Errors, 0 Warnings)\n"));
            Assert.Equal(Outcome.Unknown, (await missing.BeginEditAsync(TestAsset)).Outcome);
        }
        finally { Directory.Delete(dir); }
    }

    [Theory]
    [InlineData("abcdeFgILmopuXY", AssetState.Faulty)]
    [InlineData("abcdEfgILmopuXY", AssetState.OpenForEdit)]
    [InlineData("abcdefgILmopuXY", AssetState.Installed)]
    [InlineData("abcdefgilmopuxy", AssetState.Missing)]
    public void Status_flags(string flags, AssetState expected) => Assert.Equal(expected, TrainzUtilAdapter.ParseState(flags));

    [Fact]
    public void Path_with_spaces_becomes_spaceless_or_null()
    {
        var dir = Directory.CreateTempSubdirectory("rn spasi ").FullName;
        try
        {
            var p = TrainzUtilAdapter.ToSpacelessPath(dir);
            if (p is not null) Assert.DoesNotContain(' ', p);
            Assert.Equal("C:\\TanpaSpasi", TrainzUtilAdapter.ToSpacelessPath("C:\\TanpaSpasi"));
        }
        finally { Directory.Delete(dir); }
    }
}
public class CorpusTests
{
    [Fact]
    public void User_profile_path_is_redacted()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.DoesNotContain(home, Corpus.Redact($"{home}\\Documents\\x"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test_asset_tga_header_is_valid()
    {
        var tga = TestAssetFactory.Tga24(32, 16);
        Assert.Equal(2, tga[2]);
        Assert.Equal(32, BitConverter.ToUInt16(tga, 12));
        Assert.Equal(16, BitConverter.ToUInt16(tga, 14));
        Assert.Equal(24, tga[16]);
        Assert.Equal(18 + 32 * 16 * 3, tga.Length);
    }

    [Fact]
    public void Test_asset_config_contains_marker_and_patchable_description()
    {
        var dir = Directory.CreateTempSubdirectory("rn-asset-").FullName;
        try
        {
            var folder = TestAssetFactory.Create(dir, new Kuid(999001, 1201, null));
            var text = File.ReadAllText(Path.Combine(folder, "config.txt"), Encoding.UTF8);
            Assert.Contains(TestAssetFactory.Marker, text);
            Assert.True(ConfigTests.SetText(text, "description", "x").Ok);
            var doc = RailNexus.Core.Config.ConfigDocument.Parse(text);
            Assert.Empty(doc.Diagnostics);
            Assert.Equal("rn_uji.texture", doc.TopLevelString("texture"));
            Assert.True(File.Exists(Path.Combine(folder, "rn_uji.texture.txt")));
            Assert.Equal(TestAssetFactory.ThumbnailName, doc.Find("thumbnails/0/image")[0].Value!.Text);
            Assert.True(File.Exists(Path.Combine(folder, TestAssetFactory.ThumbnailName)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Thumbnail_png_has_valid_signature_and_crc()
    {
        var png = TestAssetFactory.Png24(240, 180);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal(240, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(180, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        // CRC IHDR dihitung atas tipe + data (13 byte).
        var crc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(29));
        Assert.Equal(TestAssetFactory.Crc32(png.AsSpan(12, 17)), crc);
        // Nilai baku CRC-32 untuk "123456789".
        Assert.Equal(0xCBF43926u, TestAssetFactory.Crc32("123456789"u8));
    }
}
