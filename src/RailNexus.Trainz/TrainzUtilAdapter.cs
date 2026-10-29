using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using RailNexus.Core;
using RailNexus.Core.Library;
using RailNexus.Core.Process;

namespace RailNexus.Trainz;

public sealed record TrainzUtilOptions(
    string TrainzUtilPath,
    CommandProfile Profile,
    /// <summary>Hanya KUID di daftar ini yang boleh diubah. Tahap 0 menulis ke aset uji saja.</summary>
    IReadOnlySet<(int UserId, int ContentId)> WritableKuids);

/// <summary>
/// Flag status dari 'TrainzUtil status' (format resmi di keluaran 'help'; bukti status-after-*.json).
/// Huruf besar = benar.
/// </summary>
public sealed record StatusFlags(string Raw)
{
    private bool Has(char upper) => Raw.Contains(upper, StringComparison.Ordinal);
    public bool InBackup => Has('A');
    public bool Packaged => Has('B');
    public bool BaseContent => Has('C');
    public bool OnDownloadStation => Has('D');
    public bool OpenForEdit => Has('E');
    public bool Faulty => Has('F');
    public bool Builtin => Has('G');
    public bool Installed => Has('I');
    public bool LocallyModified => Has('L');
    public bool MissingDependencies => Has('M');
    public bool Obsolete => Has('O');
    public bool Payware => Has('P');
    public bool UpdateAvailable => Has('U');
    public bool Authorised => Has('X');
    public bool Compatible => Has('Y');

    public AssetState ToState() =>
        !Installed ? AssetState.Missing
        : OpenForEdit ? AssetState.OpenForEdit
        : Faulty ? AssetState.Faulty
        : AssetState.Installed;
}

/// <summary>
/// Menerjemahkan operasi konseptual ke TrainzUtil sesuai profil build. Prinsip:
/// spesifikasi belum terverifikasi tidak pernah menghasilkan Success; operasi tulis
/// di luar daftar aset uji ditolak sebelum proses dijalankan.
/// </summary>
public sealed partial class TrainzUtilAdapter(TrainzUtilOptions options, IProcessRunner runner) : ITrainzAdapter
{
    public Action<TrainzOp, RawOutput>? OnRaw { get; init; }

    [GeneratedRegex(@"^(?<flags>[A-Za-z]{10,}) : (?<cat>.*?) : (?<name>.*)$")]
    private static partial Regex StatusRe();

    [GeneratedRegex(@"^\+?\s*<[^>]+>\s:\s(?<path>[A-Za-z]:[\\/].*)$")]
    private static partial Regex EditPathRe();

    public async Task<OpResult<RawOutput>> RunAsync(TrainzOp op, Kuid? kuid = null, string? path = null, CancellationToken ct = default, string? arg = null)
    {
        if (!options.Profile.Commands.TryGetValue(op, out var spec))
            return OpResult<RawOutput>.Unsupported($"Profil {options.Profile.Id} tidak memiliki perintah untuk {op}.");

        if (CommandProfile.IsMutating(op))
        {
            if (!spec.Verified)
                return OpResult<RawOutput>.Unsupported($"Perintah {op} belum terverifikasi di build ini; operasi tulis dinonaktifkan.");
            if (kuid is { } k && !options.WritableKuids.Contains((k.UserId, k.ContentId)))
                return OpResult<RawOutput>.Unsupported($"{k} bukan aset uji yang diizinkan untuk ditulis.");
        }

        var args = spec.Args
            .Select(a => a.Replace("{kuid}", kuid?.ToString() ?? "").Replace("{path}", path ?? "").Replace("{arg}", arg ?? ""))
            .ToList();
        var raw = await runner.RunAsync(options.TrainzUtilPath, args, spec.Timeout, ct).ConfigureAwait(false);
        OnRaw?.Invoke(op, raw);

        return OutputClassifier.Classify(raw) switch
        {
            OutputKind.TrainzNotRunning => OpResult<RawOutput>.NotRunning(raw),
            OutputKind.TimedOut => OpResult<RawOutput>.Unknown($"{op}: tidak ada jawaban dalam {spec.Timeout.TotalSeconds:0.###} detik.", raw),
            OutputKind.Error => OpResult<RawOutput>.Fail(OutputClassifier.ErrorSummary(raw), raw),
            OutputKind.Unrecognized => OpResult<RawOutput>.Unknown($"{op}: keluaran tidak dikenali â€” {OutputClassifier.FirstLine(raw)}", raw),
            _ when !spec.Verified => OpResult<RawOutput>.Unknown($"{op}: keluaran belum terverifikasi untuk build ini.", raw),
            _ => OpResult<RawOutput>.Ok(raw, raw),
        };
    }

    public async Task<OpResult<string>> GetVersionAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(TrainzOp.Version, ct: ct).ConfigureAwait(false);
        if (!r.IsSuccess) return r.Map(_ => "");
        var line = TuResponse.Parse(r.Value!).Of(LineKind.Result).Select(l => l.Message).FirstOrDefault();
        return line is null ? OpResult<string>.Unknown("Versi tidak ada di keluaran.", r.Raw) : OpResult<string>.Ok(line, r.Raw);
    }

    public async Task<OpResult<AssetStatus>> GetStatusAsync(Kuid kuid, CancellationToken ct = default)
    {
        var r = await RunAsync(TrainzOp.Status, kuid, ct: ct).ConfigureAwait(false);
        // "Unknown KUID" adalah jawaban yang pasti: aset tidak ada di profil ini (bukti status-test-asset.json).
        if (r.Outcome == Outcome.Failed && r.Raw is { } failRaw &&
            TuResponse.Parse(failRaw).Of(LineKind.Error).Any(l => l.Message.StartsWith("Unknown KUID", StringComparison.OrdinalIgnoreCase)))
            return OpResult<AssetStatus>.Ok(new AssetStatus(kuid, AssetState.Missing, "Unknown KUID"), failRaw);
        if (!r.IsSuccess) return r.Map(_ => new AssetStatus(kuid, AssetState.Unknown, ""));

        var line = TuResponse.Parse(r.Value!).Of(LineKind.Result).FirstOrDefault();
        var m = line is null ? Match.Empty : StatusRe().Match(line.Message);
        if (!m.Success) return OpResult<AssetStatus>.Unknown($"Status tidak dikenali: {line?.Raw}", r.Raw);
        var flags = new StatusFlags(m.Groups["flags"].Value);
        return OpResult<AssetStatus>.Ok(new AssetStatus(kuid, flags.ToState(), flags.Raw), r.Raw);
    }

    /// <summary>
    /// Status banyak aset untuk mengisi indeks pustaka. Walau 'help' menulis 'status &lt;KUID&gt; [..]',
    /// build 117026 hanya memproses KUID pertama (bukti status-batch-VE.json: 90 KUID → 1 baris),
    /// sehingga setiap KUID dibaca dengan panggilan sendiri.
    /// </summary>
    public async Task<OpResult<IReadOnlyList<LibraryEntry>>> GetStatusesAsync(IReadOnlyList<Kuid> kuids, CancellationToken ct = default)
    {
        // Jalur massal: 'filterbystatus I <KUID>..' mengembalikan baris berformat status untuk setiap
        // aset terpasang (bukti filterbystatus-I-3.json). Aset yang tidak terpasang tidak muncul.
        if (options.Profile.Commands.TryGetValue(TrainzOp.StatusBatch, out var batchSpec) && batchSpec.Verified)
        {
            var args = batchSpec.Args.Concat(kuids.Select(k => k.ToString())).ToList();
            var raw = await runner.RunAsync(options.TrainzUtilPath, args, batchSpec.Timeout, ct).ConfigureAwait(false);
            OnRaw?.Invoke(TrainzOp.StatusBatch, raw);
            var kind = OutputClassifier.Classify(raw);
            if (kind == OutputKind.TrainzNotRunning) return OpResult<IReadOnlyList<LibraryEntry>>.NotRunning(raw);
            if (kind == OutputKind.Clean) return OpResult<IReadOnlyList<LibraryEntry>>.Ok(ParseStatusLines(raw), raw);
            // Selain itu jatuh ke jalur satu per satu.
        }

        var all = new List<LibraryEntry>();
        RawOutput? last = null;
        foreach (var k in kuids)
        {
            var r = await GetStatusBatchAsync([k], ct).ConfigureAwait(false);
            last = r.Raw ?? last;
            if (r.Outcome is Outcome.TrainzNotRunning or Outcome.NotSupported) return r;
            if (r.IsSuccess) all.AddRange(r.Value!);
        }
        return OpResult<IReadOnlyList<LibraryEntry>>.Ok(all, last);
    }

    private async Task<OpResult<IReadOnlyList<LibraryEntry>>> GetStatusBatchAsync(IReadOnlyList<Kuid> kuids, CancellationToken ct)
    {
        if (!options.Profile.Commands.TryGetValue(TrainzOp.Status, out var spec) || !spec.Verified)
            return OpResult<IReadOnlyList<LibraryEntry>>.Unsupported("Perintah status belum terverifikasi.");
        var args = new List<string> { spec.Args[0] };
        args.AddRange(kuids.Select(k => k.ToString()));
        var raw = await runner.RunAsync(options.TrainzUtilPath, args, spec.Timeout, ct).ConfigureAwait(false);
        OnRaw?.Invoke(TrainzOp.Status, raw);

        var kind = OutputClassifier.Classify(raw);
        if (kind == OutputKind.TrainzNotRunning) return OpResult<IReadOnlyList<LibraryEntry>>.NotRunning(raw);
        if (kind is OutputKind.TimedOut or OutputKind.Unrecognized)
            return OpResult<IReadOnlyList<LibraryEntry>>.Unknown("Status massal tidak dapat dibaca.", raw);

        return OpResult<IReadOnlyList<LibraryEntry>>.Ok(ParseStatusLines(raw), raw);
    }

    private static List<LibraryEntry> ParseStatusLines(RawOutput raw)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = new List<LibraryEntry>();
        foreach (var line in TuResponse.Parse(raw).Of(LineKind.Result))
        {
            if (line.ContextKuid is not { } k) continue;
            var m = StatusRe().Match(line.Message);
            if (!m.Success) continue;
            var f = new StatusFlags(m.Groups["flags"].Value);
            entries.Add(new LibraryEntry(k, m.Groups["name"].Value, m.Groups["cat"].Value, f.Raw, f.ToState(),
                f.MissingDependencies, f.LocallyModified, f.Packaged || f.Builtin, f.Payware, now));
        }
        return entries;
    }

    public async Task<OpResult<StatusFlags>> GetFlagsAsync(Kuid kuid, CancellationToken ct = default)
    {
        var s = await GetStatusAsync(kuid, ct).ConfigureAwait(false);
        return s.Map(v => new StatusFlags(v.RawState));
    }

    public async Task<OpResult<string>> BeginEditAsync(Kuid kuid, CancellationToken ct = default)
    {
        var r = await RunAsync(TrainzOp.BeginEdit, kuid, ct: ct).ConfigureAwait(false);
        if (!r.IsSuccess) return r.Map(_ => "");
        // Bukti edit-already-open.json: "; <NULL> : + <kuid> : C:/.../editing/kuid -2 117104311 .../"
        var folder = TuResponse.Parse(r.Value!).Lines
            .Select(l => EditPathRe().Match(l.Message))
            .Where(m => m.Success)
            .Select(m => Path.GetFullPath(m.Groups["path"].Value.Trim().Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(Directory.Exists);
        return folder is null
            ? OpResult<string>.Unknown("Aset mungkin terbuka, tetapi folder edit tidak ditemukan di keluaran.", r.Raw)
            : OpResult<string>.Ok(folder, r.Raw);
    }

    public async Task<OpResult<bool>> CommitAsync(Kuid kuid, CancellationToken ct = default)
    {
        var r = await RunAsync(TrainzOp.Commit, kuid, ct: ct).ConfigureAwait(false);
        if (!r.IsSuccess) return r.Map(_ => false);
        // Bukti commit-test-asset.json: "+ <kuid> : <kuid> submitted".
        var submitted = TuResponse.Parse(r.Value!).Of(LineKind.Result)
            .Any(l => l.Message.EndsWith("submitted", StringComparison.OrdinalIgnoreCase));
        return submitted
            ? OpResult<bool>.Ok(true, r.Raw)
            : OpResult<bool>.Unknown("Commit selesai tanpa konfirmasi 'submitted'.", r.Raw);
    }

    public async Task<OpResult<bool>> RevertAsync(Kuid kuid, CancellationToken ct = default) =>
        (await RunAsync(TrainzOp.Revert, kuid, ct: ct).ConfigureAwait(false)).Map(_ => true);

    public async Task<OpResult<ValidationReport>> ValidateAsync(Kuid kuid, CancellationToken ct = default)
    {
        var r = await RunAsync(TrainzOp.Validate, kuid, ct: ct).ConfigureAwait(false);
        // Validasi yang menemukan error adalah hasil yang terbaca (bukti validate-after-commit.json).
        if (r.Raw is { } raw && r.Outcome is Outcome.Success or Outcome.Failed)
        {
            var p = TuResponse.Parse(raw);
            return OpResult<ValidationReport>.Ok(new ValidationReport(kuid,
                p.Of(LineKind.Error).Select(l => l.Message).ToList(),
                p.Of(LineKind.Warning).Select(l => l.Message).ToList()), raw);
        }
        return r.Map(_ => new ValidationReport(kuid, [], []));
    }

    /// <summary>
    /// Mengimpor folder. TrainzUtil menolak path berspasi walau dikutip (bukti install-test-asset*.json),
    /// sehingga path diubah ke nama pendek 8.3 atau ditolak dengan alasan yang jelas.
    /// Aset hasil impor dibiarkan terbuka untuk diedit oleh Trainz ("Skipped commit").
    /// </summary>
    public async Task<OpResult<IReadOnlyList<Kuid>>> ImportFolderAsync(string folder, CancellationToken ct = default)
    {
        var usable = ToSpacelessPath(Path.GetFullPath(folder));
        if (usable is null)
            return OpResult<IReadOnlyList<Kuid>>.Fail($"Path berisi spasi dan tidak punya nama pendek: {folder}. Pindahkan ke folder tanpa spasi.");
        var r = await RunAsync(TrainzOp.ImportFolder, path: usable, ct: ct).ConfigureAwait(false);
        if (!r.IsSuccess) return r.Map<IReadOnlyList<Kuid>>(_ => []);
        var installed = TuResponse.Parse(r.Value!).Of(LineKind.Result)
            .Where(l => l.Message.StartsWith("Installed asset", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.ContextKuid)
            .OfType<Kuid>()
            .Distinct()
            .ToList();
        return installed.Count == 0
            ? OpResult<IReadOnlyList<Kuid>>.Unknown("Impor selesai tanpa baris 'Installed asset'.", r.Raw)
            : OpResult<IReadOnlyList<Kuid>>.Ok(installed, r.Raw, "Aset terpasang dan masih terbuka untuk diedit; perlu commit.");
    }

    internal static string? ToSpacelessPath(string path)
    {
        if (!path.Contains(' ')) return path;
        if (!OperatingSystem.IsWindows()) return null;
        var buffer = new char[1024];
        var n = GetShortPathNameW(path, buffer, (uint)buffer.Length);
        if (n == 0 || n > buffer.Length) return null;
        var shortPath = new string(buffer, 0, (int)n);
        return shortPath.Contains(' ') ? null : shortPath;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);

    internal static AssetState ParseState(string flags) => new StatusFlags(flags).ToState();

    /// <summary>KUID aset lokal dalam satu kategori. Hanya Success bila perintahnya terverifikasi di profil.</summary>
    public async Task<OpResult<IReadOnlyList<Kuid>>> SearchCategoryAsync(string category, CancellationToken ct = default)
    {
        var r = await RunAsync(TrainzOp.SearchByCategory, ct: ct, arg: category).ConfigureAwait(false);
        return r.IsSuccess ? OpResult<IReadOnlyList<Kuid>>.Ok(TrainzOutputParsers.SearchCategory(r.Value!), r.Raw) : r.Map<IReadOnlyList<Kuid>>(_ => []);
    }

    /// <summary>
    /// Teks config lewat printconfig (baca saja, tidak membuka edit). Teks ini dinormalkan Trainz,
    /// jadi tidak dipakai sebagai dasar hash transaksi.
    /// </summary>
    public async Task<OpResult<string>> PrintConfigAsync(Kuid kuid, CancellationToken ct = default)
    {
        var r = await RunAsync(TrainzOp.PrintConfig, kuid, ct: ct).ConfigureAwait(false);
        if (!r.IsSuccess) return r.Map(_ => "");
        var text = TrainzOutputParsers.PrintConfig(r.Value!);
        return text is null ? OpResult<string>.Unknown("Keluaran printconfig tidak memuat dump lengkap.", r.Raw) : OpResult<string>.Ok(text, r.Raw);
    }
}
