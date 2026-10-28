using System.Text;
using RailNexus.Core;
using RailNexus.Core.Config;
using RailNexus.Core.Process;
using RailNexus.Core.Roles;
using RailNexus.Core.Tx;
using RailNexus.Trainz;

Console.OutputEncoding = Encoding.UTF8;

var (command, positional, opts) = ParseArgs(args);
var repoRoot = FindRepoRoot();
var trainzDir = opts.GetValueOrDefault("trainz") ?? FindTrainz();
var trainzUtil = trainzDir is null ? null : Path.Combine(trainzDir, "TrainzUtil.exe");
var profilePath = opts.GetValueOrDefault("profile") ?? Path.Combine(AppContext.BaseDirectory, "profiles", "trs19.json");
var storeRoot = opts.GetValueOrDefault("store")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RailNexus", "tahap0");
var corpusDir = opts.GetValueOrDefault("corpus")
    ?? Path.Combine(repoRoot ?? Environment.CurrentDirectory, "fixtures", "trainzutil", "trs19");

try
{
    return command switch
    {
        "probe" => await Probe(),
        "capture" => await Capture(),
        "make-test-asset" => MakeTestAsset(),
        "import" => await Import(),
        "cycle" => await Cycle(),
        "repair-test-asset" => await RepairTestAsset(),
        "undo" => await Undo(),
        "index" => await IndexLibrary(),
        "parse-check" => await ParseCheck(),
        "list" => ListTx(),
        "reconcile" => await Reconcile(),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is IOException or ArgumentException or FormatException or InvalidDataException)
{
    Console.Error.WriteLine($"Gagal: {ex.Message}");
    return 2;
}

// ---------------------------------------------------------------- perintah

async Task<int> Probe()
{
    RequireTrainzUtil();
    var profile = CommandProfile.Load(profilePath);
    var runner = new ProcessRunner();
    Console.WriteLine($"TrainzUtil : {Corpus.Redact(trainzUtil!)}");
    Console.WriteLine($"Profil     : {profile.Id} ({Path.GetFileName(profilePath)})");

    foreach (var (name, op) in new[] { ("help", TrainzOp.Help), ("version", TrainzOp.Version) })
    {
        var spec = profile.Commands[op];
        var raw = await runner.RunAsync(trainzUtil!, spec.Args, TimeSpan.FromSeconds(spec.TimeoutSeconds));
        var kind = OutputClassifier.Classify(raw);
        var saved = Corpus.Save(corpusDir, Corpus.FromRaw($"probe-{name}", profile.Build, raw, "railnexus probe"));
        Console.WriteLine();
        Console.WriteLine($"[{name}] exit={raw.ExitCode?.ToString() ?? "-"} timeout={raw.TimedOut} klasifikasi={kind} ({raw.Duration.TotalMilliseconds:0} ms)");
        Console.WriteLine($"  disimpan: {Path.GetRelativePath(repoRoot ?? ".", saved)}");
        foreach (var line in OutputClassifier.Lines(raw).Take(60)) Console.WriteLine("  | " + Corpus.Redact(line));

        if (op == TrainzOp.Help && kind == OutputKind.TrainzNotRunning)
        {
            Console.WriteLine();
            Console.WriteLine("TrainzUtil belum tersambung ke Trainz. Pastikan 'Advanced Debug Tools' aktif di Launcher → Trainz Settings, lalu jalankan TRS19 dan ulangi probe.");
            return 3;
        }
        if (op == TrainzOp.Help && kind == OutputKind.Clean)
        {
            var helpText = (raw.StdOut + raw.StdErr).ToLowerInvariant();
            Console.WriteLine();
            Console.WriteLine("  Kecocokan profil dengan daftar perintah di 'help':");
            foreach (var (o, s) in profile.Commands)
            {
                var verb = s.Args.FirstOrDefault() ?? "";
                var found = verb.Length > 0 && helpText.Contains(verb.ToLowerInvariant());
                Console.WriteLine($"   {(found ? "ada " : "TIDAK")}  {o,-13} '{verb}'  verified={s.Verified}");
            }
        }
    }
    Console.WriteLine();
    Console.WriteLine("Keluaran di atas adalah corpus nyata. Profil diperbarui (verified=true) hanya setelah sintaks dan bentuk keluarannya dibuktikan.");
    return 0;
}

async Task<int> Capture()
{
    RequireTrainzUtil();
    if (positional.Count < 2) return Usage("capture <nama> <argumen TrainzUtil...>");
    var name = positional[0];
    var tuArgs = positional.Skip(1).ToList();
    if (tuArgs.Any(a => a.Contains("commit", StringComparison.OrdinalIgnoreCase) || a.Contains("install", StringComparison.OrdinalIgnoreCase)
                     || a.Contains("delete", StringComparison.OrdinalIgnoreCase) || a.Contains("revert", StringComparison.OrdinalIgnoreCase)
                     || a.Equals("edit", StringComparison.OrdinalIgnoreCase)) && !opts.ContainsKey("i-know-this-writes"))
    {
        Console.Error.WriteLine("Perintah ini mungkin mengubah data Trainz. 'capture' hanya untuk perintah baca. Gunakan 'cycle' untuk aset uji.");
        return 2;
    }
    var timeout = TimeSpan.FromSeconds(int.TryParse(opts.GetValueOrDefault("timeout"), out var t) ? t : 60);
    var raw = await new ProcessRunner().RunAsync(trainzUtil!, tuArgs, timeout);
    var saved = Corpus.Save(corpusDir, Corpus.FromRaw(name, opts.GetValueOrDefault("build") ?? "TRS19", raw, opts.GetValueOrDefault("note") ?? ""));
    Console.WriteLine($"exit={raw.ExitCode?.ToString() ?? "-"} timeout={raw.TimedOut} klasifikasi={OutputClassifier.Classify(raw)}");
    foreach (var line in OutputClassifier.Lines(raw).Take(80)) Console.WriteLine("| " + Corpus.Redact(line));
    Console.WriteLine($"disimpan: {saved}");
    return 0;
}

int MakeTestAsset()
{
    var kuid = RequireKuid();
    var outDir = opts.GetValueOrDefault("out") ?? Path.Combine(storeRoot, "aset-uji");
    var folder = TestAssetFactory.Create(outDir, kuid);
    Console.WriteLine($"Aset uji dibuat: {folder}");
    Console.WriteLine($"Langkah berikut: railnexus import \"{folder}\" --kuid \"{kuid}\"");
    return 0;
}

async Task<int> Import()
{
    var kuid = RequireKuid();
    if (positional.Count < 1) return Usage("import <folder> --kuid <kuid>");
    var folder = Path.GetFullPath(positional[0]);
    var config = Path.Combine(folder, "config.txt");
    if (!File.Exists(config) || !File.ReadAllText(config).Contains(TestAssetFactory.Marker))
    {
        Console.Error.WriteLine("Hanya folder aset uji buatan 'make-test-asset' yang boleh diimpor di Tahap 0.");
        return 2;
    }
    var adapter = CreateAdapter(kuid);
    var r = await adapter.ImportFolderAsync(folder);
    Report("import", r.Outcome, r.Message, r.Raw);
    if (!r.IsSuccess) return 1;
    Console.WriteLine("KUID terpasang: " + string.Join(", ", r.Value!));
    // installfrompath membiarkan aset terbuka untuk diedit (bukti install-test-asset-nospace.json).
    var c = await adapter.CommitAsync(kuid);
    Report("commit", c.Outcome, c.Message, c.Raw);
    var v = await adapter.ValidateAsync(kuid);
    Report("validasi", v.Outcome, v.Value is { } vr ? $"{vr.Errors.Count} error, {vr.Warnings.Count} peringatan" : v.Message, v.Raw);
    foreach (var e in v.Value?.Errors ?? []) Console.WriteLine("    error  : " + e);
    return c.IsSuccess && v.Value is { HasErrors: false } ? 0 : 1;
}

async Task<int> Cycle()
{
    var kuid = RequireKuid();
    var adapter = CreateAdapter(kuid);
    var store = new TxStore(storeRoot);
    var tx = new TransactionRunner(adapter, store);

    // Uji crash nyata: proses mati tepat setelah fase tertentu tercatat di jurnal.
    if (opts.GetValueOrDefault("crash-after") is { } crashAt)
    {
        var phase = Enum.Parse<TxPhase>(crashAt, ignoreCase: true);
        store.AfterAppend = (id, p) =>
        {
            if (p != phase || store.LoadManifest(id)?.Kind != "edit") return;
            Console.WriteLine($"!! Uji crash: proses dihentikan paksa setelah fase {p} (tx {id}).");
            Console.Out.Flush();
            Environment.Exit(99);
        };
    }

    Console.WriteLine($"Siklus Tahap 0 untuk aset uji {kuid}");
    Console.WriteLine($"Penyimpanan transaksi: {Corpus.Redact(store.Root)}");

    // 1. Baca isi terpasang lewat edit lalu revert, untuk menyusun draft.
    var status = await adapter.GetStatusAsync(kuid);
    Report("status", status.Outcome, status.Message, status.Raw);
    if (!status.IsSuccess) return 1;

    var open = await adapter.BeginEditAsync(kuid);
    Report("baca (edit)", open.Outcome, open.Message, open.Raw);
    if (!open.IsSuccess) return 1;
    var configPath = SafePath.Combine(open.Value!, "config.txt");
    var originalBytes = await File.ReadAllBytesAsync(configPath);
    var original = Encoding.UTF8.GetString(originalBytes);
    var rv = await adapter.RevertAsync(kuid);
    Report("tutup baca (revert)", rv.Outcome, rv.Message, rv.Raw);
    if (!rv.IsSuccess) return 1;
    if (!original.Contains(TestAssetFactory.Marker))
    {
        Console.Error.WriteLine("Config tidak memuat penanda aset uji. Siklus dihentikan untuk melindungi aset ini.");
        return 2;
    }

    // 2. Draft: ubah deskripsi saja.
    var newDescription = TestAssetFactory.DescriptionFor(DateTimeOffset.UtcNow);
    var draft = new Draft(kuid);
    draft.Put(new DraftItem.SetText("description", newDescription, "Tahap 0", "Ubah deskripsi aset uji"));
    var (plan, build) = draft.ToEditPlan(originalBytes, "Tahap 0: ubah deskripsi aset uji");
    if (plan is null) { Console.Error.WriteLine($"Draft gagal: {build.Error}"); return 1; }

    // 3. Terapkan.
    var applied = await tx.ApplyAsync(plan);
    PrintTx("terapkan", applied, store);
    if (applied.Status is not (TxStatus.Succeeded or TxStatus.SucceededWithWarnings)) return 1;

    // 4. Periksa isi terpasang: deskripsi berubah, semua baris lain identik.
    var check = await ReadConfig(adapter, kuid);
    var checkText = check is null ? null : Encoding.UTF8.GetString(check);
    var changed = checkText?.Contains(newDescription) == true;
    var otherLinesKept = checkText is not null && LinesExcept(checkText, "description").SequenceEqual(LinesExcept(original, "description"));
    Console.WriteLine($"  isi terpasang memuat deskripsi baru: {changed}; semua baris lain identik: {otherLinesKept}");

    if (opts.ContainsKey("no-undo")) return changed && otherLinesKept ? 0 : 1;

    // 5. Undo sebagai transaksi baru, lalu periksa byte asli kembali.
    var undone = await tx.UndoAsync(applied.TxId);
    PrintTx("undo", undone, store);
    var afterUndo = await ReadConfig(adapter, kuid);
    var restored = afterUndo is not null && afterUndo.AsSpan().SequenceEqual(originalBytes);
    Console.WriteLine($"  isi kembali sama persis (byte) dengan sebelum siklus: {restored}");

    var ok = changed && otherLinesKept && restored && undone.Status is TxStatus.Succeeded or TxStatus.SucceededWithWarnings;
    Console.WriteLine(ok ? "HASIL: siklus baca → ubah → submit → periksa → pulihkan berhasil." : "HASIL: siklus belum lolos; lihat detail di atas dan jurnal.");
    return ok ? 0 : 1;
}

int ListTx()
{
    var store = new TxStore(storeRoot);
    foreach (var id in store.AllTxIds())
    {
        var m = store.LoadManifest(id);
        var last = store.LastPhase(id);
        Console.WriteLine($"{id}  {last,-16} {m?.Kind,-5} {m?.Kuid}  {m?.Purpose}");
    }
    var open = store.FindIncomplete();
    if (open.Count > 0) Console.WriteLine($"\n{open.Count} transaksi belum ditutup. Jalankan: railnexus reconcile");
    return 0;
}

async Task<int> Reconcile()
{
    var store = new TxStore(storeRoot);
    var ids = positional.Count > 0 ? positional : store.FindIncomplete().ToList();
    if (ids.Count == 0) { Console.WriteLine("Tidak ada transaksi yang perlu direkonsiliasi."); return 0; }
    var code = 0;
    foreach (var id in ids)
    {
        var m = store.LoadManifest(id);
        if (m is null) { Console.WriteLine($"{id}: manifest tidak ada"); code = 1; continue; }
        var tx = new TransactionRunner(CreateAdapter(m.Kuid), store);
        var r = await tx.ReconcileAsync(id, discardPendingEdit: opts.ContainsKey("discard-pending-edit"));
        if (r.Status == TxStatus.NeedsAttention && !opts.ContainsKey("discard-pending-edit"))
            Console.WriteLine("    Untuk membatalkan edit tertunda: railnexus reconcile " + id + " --discard-pending-edit");
        PrintTx("rekonsiliasi", r, store);
        if (r.Status is TxStatus.NeedsAttention or TxStatus.Uncertain) code = 1;
    }
    return code;
}

// ---------------------------------------------------------------- bantuan

async Task<byte[]?> ReadConfig(TrainzUtilAdapter adapter, Kuid kuid)
{
    var e = await adapter.BeginEditAsync(kuid);
    if (!e.IsSuccess) { Report("baca ulang", e.Outcome, e.Message, e.Raw); return null; }
    var bytes = await File.ReadAllBytesAsync(SafePath.Combine(e.Value!, "config.txt"));
    var r = await adapter.RevertAsync(kuid);
    if (!r.IsSuccess) Report("tutup baca ulang", r.Outcome, r.Message, r.Raw);
    return bytes;
}

static List<string> LinesExcept(string text, string tag) =>
    text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !l.TrimStart().StartsWith(tag, StringComparison.OrdinalIgnoreCase)).ToList();

// Mencari KUID per kategori. Bentuk keluaran searchbycategory belum terverifikasi; semua KUID pada
// baris hasil ('+') dan log (';') diambil, dan keluaran mentahnya disimpan ke corpus untuk ditinjau.
async Task<List<Kuid>> SearchCategory(string category)
{
    RequireTrainzUtil();
    var raw = await new ProcessRunner().RunAsync(trainzUtil!, ["searchbycategory", category], TimeSpan.FromMinutes(5));
    Corpus.Save(corpusDir, Corpus.FromRaw($"searchbycategory-{category}", CommandProfile.Load(profilePath).Build,
        raw with { StdOut = string.Join("\n", raw.StdOut.Split('\n').Take(40)) }, "dipotong 40 baris pertama"));
    var kind = OutputClassifier.Classify(raw);
    if (kind != OutputKind.Clean) throw new IOException($"searchbycategory {category}: {kind} — {OutputClassifier.ErrorSummary(raw)}");
    return TuResponse.Parse(raw).Lines
        .Where(l => l.Kind is LineKind.Result or LineKind.Log)
        .SelectMany(l => System.Text.RegularExpressions.Regex.Matches(l.Message, @"<kuid2?:[^>]+>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        .Select(m => Kuid.TryParse(m.Value, out var k) ? (Kuid?)k : null)
        .OfType<Kuid>()
        .Distinct()
        .ToList();
}

async Task<int> IndexLibrary()
{
    var categories = (opts.GetValueOrDefault("category") ?? "TV").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var profile = CommandProfile.Load(profilePath);
    var adapter = new TrainzUtilAdapter(new TrainzUtilOptions(trainzUtil!, profile, new HashSet<(int, int)>()), new ProcessRunner());
    var dbPath = Path.Combine(storeRoot, "library.db");
    using var lib = new RailNexus.Core.Library.LibraryIndex(dbPath, profile.Id);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    foreach (var cat in categories)
    {
        var kuids = await SearchCategory(cat);
        Console.WriteLine($"Kategori {cat}: {kuids.Count} KUID ditemukan ({sw.Elapsed.TotalSeconds:0.0} dtk)");
        var stored = 0;
        var first = true;
        foreach (var batch in kuids.Chunk(100))
        {
            var r = await adapter.GetStatusesAsync(batch);
            if (r.Raw is { } raw)
            {
                var parsed = TuResponse.Parse(raw);
                if (first)
                {
                    Corpus.Save(corpusDir, Corpus.FromRaw($"status-batch-{cat}", profile.Build,
                        raw with { StdOut = string.Join("\n", raw.StdOut.Split('\n').Take(30)) }, $"status {batch.Length} KUID; dipotong 30 baris"));
                    Console.WriteLine($"  batch pertama: {batch.Length} KUID → {parsed.Of(LineKind.Result).Count()} baris hasil, " +
                                      $"{parsed.Of(LineKind.Error).Count()} error, terbaca {r.Value?.Count ?? 0}");
                    foreach (var e in parsed.Of(LineKind.Error).Take(3)) Console.WriteLine("    error  : " + e.Raw);
                    var parsedKuids = r.Value?.Select(v => v.Kuid).ToHashSet() ?? [];
                    foreach (var u in parsed.Of(LineKind.Result).Where(x => x.ContextKuid is not { } k || !parsedKuids.Contains(k)).Take(3))
                        Console.WriteLine("    tidak terbaca: " + u.Raw);
                    first = false;
                }
            }
            if (!r.IsSuccess) { Report("status massal", r.Outcome, r.Message, r.Raw); return 1; }
            lib.Upsert(r.Value!);
            stored += r.Value!.Count;
        }
        Console.WriteLine($"  disimpan ke indeks: {stored} dari {kuids.Count} ({sw.Elapsed.TotalSeconds:0.0} dtk)");
    }
    Console.WriteLine($"Total di indeks profil '{profile.Id}': {lib.Count()} — {Corpus.Redact(dbPath)}");
    foreach (var f in new[] { RailNexus.Core.Library.LibraryFilter.Faulty, RailNexus.Core.Library.LibraryFilter.MissingDependencies, RailNexus.Core.Library.LibraryFilter.OpenForEdit })
        Console.WriteLine($"  {f}: {lib.Search(new RailNexus.Core.Library.LibraryQuery(Filter: f, Limit: 100_000)).Count}");
    return 0;
}

/// <summary>
/// Menguji parser terhadap config asli lewat printconfig. Isi config tidak disimpan (karya pihak lain);
/// hanya ringkasan diagnosis dan slot peran yang dicetak.
/// </summary>
async Task<int> ParseCheck()
{
    RequireTrainzUtil();
    var category = opts.GetValueOrDefault("category") ?? "TV";
    var limit = int.TryParse(opts.GetValueOrDefault("limit"), out var l) ? l : 40;
    var kuids = (await SearchCategory(category)).Take(limit).ToList();
    var runner = new ProcessRunner();
    int parsed = 0, withErrors = 0, unreadable = 0;
    var codes = new Dictionary<string, int>();
    var roleHits = RoleCatalog.P0.ToDictionary(r => r.Id, _ => 0);
    var examples = new List<string>();
    var unreadableReasons = new Dictionary<string, int>();
    foreach (var k in kuids)
    {
        var raw = await runner.RunAsync(trainzUtil!, ["printconfig", k.ToString()], TimeSpan.FromSeconds(60));
        if (OutputClassifier.Classify(raw) != OutputKind.Clean)
        {
            unreadable++;
            var reason = TuResponse.Parse(raw).Of(LineKind.Error).Select(e => e.Message).FirstOrDefault() ?? OutputClassifier.Classify(raw).ToString();
            reason = System.Text.RegularExpressions.Regex.Replace(reason, @"<kuid2?:[^>]+>", "<kuid>");
            unreadableReasons[reason] = unreadableReasons.GetValueOrDefault(reason) + 1;
            continue;
        }
        var text = ExtractPrintConfig(raw);
        if (text is null) { unreadable++; unreadableReasons["dump tidak lengkap"] = unreadableReasons.GetValueOrDefault("dump tidak lengkap") + 1; continue; }
        var doc = RailNexus.Core.Config.ConfigDocument.Parse(text);
        parsed++;
        if (doc.HasErrors) withErrors++;
        foreach (var d in doc.Diagnostics) codes[d.Code] = codes.GetValueOrDefault(d.Code) + 1;
        foreach (var role in RoleCatalog.P0)
            if (role.Locate(doc).Any()) roleHits[role.Id]++;
        // Contoh diagnosis dengan potongan baris (dipendekkan) agar penyebab dapat dianalisis; hanya ditampilkan.
        var textLines = text.Split('\n');
        foreach (var d in doc.Diagnostics.Where(d => d.Code != "CFG-DUPLICATE").Take(2))
        {
            if (examples.Count >= 12) break;
            var snippet = d.Line - 1 < textLines.Length ? textLines[d.Line - 1].Trim() : "";
            if (snippet.Length > 70) snippet = snippet[..70] + "…";
            examples.Add($"{d.Code} {k} baris {d.Line}: {snippet}");
        }
        // Uji round-trip: parse tanpa edit harus identik.
        if (RailNexus.Core.Config.ConfigEditor.Apply(text, []) != text) codes["ROUNDTRIP-FAIL"] = codes.GetValueOrDefault("ROUNDTRIP-FAIL") + 1;
    }
    Console.WriteLine($"Kategori {category}: diperiksa {kuids.Count}, ter-parse {parsed}, berisi error {withErrors}, tidak terbaca {unreadable}");
    Console.WriteLine("Diagnosis: " + (codes.Count == 0 ? "tidak ada" : string.Join(", ", codes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}×{kv.Value}"))));
    Console.WriteLine("Slot peran ditemukan pada: " + string.Join(", ", roleHits.Select(kv => $"{kv.Key} {kv.Value}/{parsed}")));
    foreach (var (reason, n) in unreadableReasons.OrderByDescending(kv => kv.Value)) Console.WriteLine($"  tidak terbaca ×{n}: {reason}");
    foreach (var e in examples) Console.WriteLine("  contoh: " + e);
    return 0;
}

static string? ExtractPrintConfig(RawOutput raw)
{
    var lines = TuResponse.Parse(raw).Lines.Where(x => x.Kind == LineKind.Log).Select(x => x.Message).ToList();
    var start = lines.FindIndex(x => x.StartsWith("+ Start of", StringComparison.Ordinal));
    var end = lines.FindIndex(x => x.StartsWith("+ End of", StringComparison.Ordinal));
    if (start < 0 || end <= start) return null;
    var body = lines.Skip(start + 1).Take(end - start - 1).ToList();
    if (body.Count > 0 && body[0].Trim('﻿', '?', ' ').Length == 0) body.RemoveAt(0);
    return string.Join("\n", body) + "\n";
}

async Task<int> Undo()
{
    if (positional.Count < 1) return Usage("undo <txid>");
    var store = new TxStore(storeRoot);
    var m = store.LoadManifest(positional[0]);
    if (m is null) { Console.Error.WriteLine("Transaksi tidak ditemukan."); return 2; }
    var r = await new TransactionRunner(CreateAdapter(m.Kuid), store).UndoAsync(positional[0]);
    PrintTx("undo", r, store);
    return r.Status is TxStatus.Succeeded or TxStatus.SucceededWithWarnings ? 0 : 1;
}

async Task<int> RepairTestAsset()
{
    var kuid = RequireKuid();
    var adapter = CreateAdapter(kuid);
    var store = new TxStore(storeRoot);

    var open = await adapter.BeginEditAsync(kuid);
    Report("baca (edit)", open.Outcome, open.Message, open.Raw);
    if (!open.IsSuccess) return 1;
    var folder = open.Value!;
    var config = await File.ReadAllTextAsync(SafePath.Combine(folder, "config.txt"));
    var current = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(f => Path.GetRelativePath(folder, f), Hashing.HashFileOrNull, StringComparer.OrdinalIgnoreCase);
    var rv = await adapter.RevertAsync(kuid);
    Report("tutup baca (revert)", rv.Outcome, rv.Message, rv.Raw);
    if (!rv.IsSuccess) return 1;
    if (!config.Contains(TestAssetFactory.Marker))
    {
        Console.Error.WriteLine("Config tidak memuat penanda aset uji. Perbaikan dihentikan untuk melindungi aset ini.");
        return 2;
    }

    var target = TestAssetFactory.Files(kuid);
    var changes = target.Select(kv => new FileChange(kv.Key, kv.Value)).ToList();
    foreach (var stale in current.Keys.Where(k => !target.ContainsKey(k))) changes.Add(new FileChange(stale, null));
    var expected = changes.ToDictionary(c => c.RelativePath, c => current.GetValueOrDefault(c.RelativePath), StringComparer.OrdinalIgnoreCase);

    var r = await new TransactionRunner(adapter, store).ApplyAsync(new EditPlan(kuid, "Perbaiki struktur aset uji sesuai validasi TRS19", changes, expected));
    PrintTx("perbaiki aset uji", r, store);
    return r.Status is TxStatus.Succeeded ? 0 : 1;
}

TrainzUtilAdapter CreateAdapter(Kuid writable)
{
    RequireTrainzUtil();
    var profile = CommandProfile.Load(profilePath);
    // Uji timeout nyata: batas waktu commit diperkecil agar TrainzUtil dihentikan saat submit berjalan.
    if (int.TryParse(opts.GetValueOrDefault("commit-timeout-ms"), out var ms))
    {
        var commands = profile.Commands.ToDictionary(kv => kv.Key, kv => kv.Value);
        commands[TrainzOp.Commit] = commands[TrainzOp.Commit] with { TimeoutMilliseconds = ms };
        profile = profile with { Commands = commands };
    }
    var log = Path.Combine(storeRoot, "raw");
    return new TrainzUtilAdapter(
        new TrainzUtilOptions(trainzUtil!, profile, new HashSet<(int, int)> { (writable.UserId, writable.ContentId) }),
        new ProcessRunner())
    {
        // Semua keluaran mentah disimpan untuk diagnosis dan corpus.
        OnRaw = (op, raw) => Corpus.Save(log, Corpus.FromRaw($"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{op}", profile.Build, raw)),
    };
}

void Report(string step, Outcome outcome, string message, RawOutput? raw)
{
    Console.WriteLine($"- {step,-20} {outcome,-16} {message}");
    if (raw is not null && outcome != Outcome.Success)
        foreach (var l in OutputClassifier.Lines(raw).Take(10)) Console.WriteLine("    | " + Corpus.Redact(l));
}

void PrintTx(string label, TxResult r, TxStore store)
{
    Console.WriteLine($"- {label,-20} {r.Status,-16} {r.Message}  [tx {r.TxId}]");
    foreach (var e in store.ReadJournal(r.TxId)) Console.WriteLine($"    {e.At:HH:mm:ss} {e.Phase,-16} {Corpus.Redact(e.Note)}");
    if (r.ConflictingFiles is { Count: > 0 } c) Console.WriteLine("    konflik: " + string.Join(", ", c));
    if (r.Validation is { } v)
    {
        foreach (var x in v.Errors) Console.WriteLine("    error  : " + x);
        foreach (var x in v.Warnings) Console.WriteLine("    warning: " + x);
    }
}

Kuid RequireKuid() =>
    Kuid.TryParse(opts.GetValueOrDefault("kuid"), out var k) ? k
        : throw new ArgumentException("Butuh --kuid \"<kuid:USERID:NOMOR>\" untuk aset uji.");

void RequireTrainzUtil()
{
    if (trainzUtil is null || !File.Exists(trainzUtil))
        throw new IOException("TrainzUtil.exe tidak ditemukan. Tentukan folder instalasi dengan --trainz \"<folder>\".");
}

static string? FindTrainz() =>
    TrainzInstallations.KnownFolders.Select(k => k.Folder).FirstOrDefault(TrainzInstallations.HasTool);

static string? FindRepoRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "RailNexus.slnx"))) return d.FullName;
    return null;
}

static (string Command, List<string> Positional, Dictionary<string, string> Options) ParseArgs(string[] a)
{
    var pos = new List<string>();
    var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 1; i < a.Length; i++)
    {
        if (a[i] == "--") { pos.AddRange(a.Skip(i + 1)); break; }
        if (a[i].StartsWith("--", StringComparison.Ordinal))
        {
            var key = a[i][2..];
            var hasValue = i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal);
            opt[key] = hasValue ? a[++i] : "true";
        }
        else pos.Add(a[i]);
    }
    return (a.Length > 0 ? a[0] : "", pos, opt);
}

static int Usage(string? hint = null)
{
    if (hint is not null) Console.Error.WriteLine("Pemakaian: railnexus " + hint);
    Console.WriteLine("""
        RailNexus — alat uji integrasi Tahap 0

          probe                               Jalankan 'help' dan 'version', simpan ke corpus, cocokkan profil
          capture <nama> -- <argumen...>      Tangkap keluaran perintah baca ke fixtures/
          make-test-asset --kuid <kuid>       Buat folder aset uji yang dapat dibuang
          import <folder> --kuid <kuid>       Impor folder aset uji ke Trainz
          cycle --kuid <kuid> [--no-undo]     Baca → ubah → submit → periksa → undo pada aset uji
          repair-test-asset --kuid <kuid>     Ganti isi aset uji dengan bentuk terbaru lewat transaksi
          list                                Tampilkan riwayat transaksi
          undo <txid>                         Batalkan transaksi yang sudah selesai (transaksi baru)
          index [--category TV,CMP]           Isi indeks pustaka dari Trainz (baca saja)
          parse-check [--category TV] [--limit 40]  Uji parser pada config asli (baca saja, isi tidak disimpan)
          reconcile [txid...]                 Pulihkan transaksi yang belum ditutup (crash/timeout)

        Opsi umum: --trainz <folder>  --profile <json>  --store <folder>  --corpus <folder>
        Operasi tulis hanya berjalan untuk --kuid aset uji dan untuk perintah yang sudah terverifikasi di profil.
        """);
    return hint is null ? 0 : 2;
}
