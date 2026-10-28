using System.Text;
using RailNexus.App.Core;
using RailNexus.App.Core.ViewModels;
using RailNexus.Core;
using RailNexus.Core.Config;
using RailNexus.Core.Library;
using RailNexus.Core.Roles;
using RailNexus.Core.Texture;
using RailNexus.Core.Tx;

namespace RailNexus.Tests;

/// <summary>Layanan koneksi palsu: memakai simulator, bukan Trainz (PRD 27).</summary>
internal sealed class FakeConnections(SimulatedTrainz sim, string dbPath, string storeRoot, ConnectionCheck check) : IConnectionService
{
    public int Opened { get; private set; }

    public IReadOnlyList<InstallationCandidate> FindInstallations() =>
        [new InstallationCandidate("TRS19 simulasi", @"C:\Simulasi\TRS19", true)];

    public InstallationCandidate Inspect(string folder) => new(Path.GetFileName(folder), folder, false);

    public string DataRoot => storeRoot;

    public IReadOnlyList<Capability> Capabilities() =>
        [new Capability("Submit", new StatusLabel("Terverifikasi", Tone.Ok), "simulasi")];

    public Task<ConnectionCheck> CheckAsync(InstallationCandidate installation, CancellationToken ct = default) =>
        Task.FromResult(installation.ToolFound ? check
            : new ConnectionCheck(ConnectionState.ToolNotFound, "", "TrainzUtil.exe tidak ditemukan di folder ini.", []));

    public AppSession Open(InstallationCandidate installation, ConnectionCheck c)
    {
        Opened++;
        var store = new TxStore(storeRoot);
        var runner = c.State == ConnectionState.Connected ? new TransactionRunner(sim, store) : null;
        return new AppSession("TRS19 simulasi — trs19", new LibraryIndex(dbPath, "trs19"), new EditFolderAssetReader(sim), store, runner,
            runner is null ? "Build ini belum diuji; perubahan dinonaktifkan sampai profilnya lolos uji." : null, installation.Folder);
    }
}

public sealed class AppViewModelTests : IDisposable
{
    private static readonly Kuid Loco = Kuid.Parse("<kuid2:999001:1201:2>");
    private static readonly Kuid Wagon = Kuid.Parse("<kuid:999001:1300>");
    private static readonly Kuid Broken = Kuid.Parse("<kuid:999001:1400>");
    private static readonly Kuid Horn2 = Kuid.Parse("<kuid:999002:3101>");
    private static readonly Kuid Horn3 = Kuid.Parse("<kuid:999002:3103>");
    private static readonly Kuid HornFaulty = Kuid.Parse("<kuid:999002:3104>");
    private static readonly Kuid Bogey = Kuid.Parse("<kuid:999005:6006>");

    private static readonly ConnectionCheck Connected = new(ConnectionState.Connected, "Build 117026", "Terhubung · dapat mengedit aset uji",
        [new CheckStep(true, "Identitas build terbaca", "Build 117026")]);

    private readonly string _root = Directory.CreateTempSubdirectory("rn app ").FullName;
    private readonly SimulatedTrainz _sim = new();
    private readonly byte[] _locoConfig =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "configs", "loco-dx200.txt"));

    public AppViewModelTests()
    {
        _sim.Install(Loco, new() { ["config.txt"] = _locoConfig });
        _sim.Install(Wagon, new() { ["config.txt"] = Encoding.UTF8.GetBytes("kuid <kuid:999001:1300>\r\nkind \"traincar\"\r\n") });
        _sim.Install(Broken, new() { ["config.txt"] = Encoding.UTF8.GetBytes("kuid <kuid:999001:1400>\r\nbogeys\r\n{\r\n  0\r\n  {\r\n") });

        using var lib = new LibraryIndex(DbPath, "trs19");
        var t = DateTimeOffset.Parse("2026-10-28T09:12:00Z");
        lib.Upsert(
        [
            new LibraryEntry(Loco, "Lokomotif Diesel DX-200", "TV", "abcdefgILmopuXY", AssetState.Installed, false, false, false, false, t),
            new LibraryEntry(Wagon, "Gerbong Barang GB-40", "TV", "abcdefgILmopuXY", AssetState.Installed, false, false, false, false, t),
            new LibraryEntry(Broken, "Gerbong Rusak", "TV", "abcdeFgILmopuXY", AssetState.Faulty, true, false, false, false, t),
            new LibraryEntry(Horn2, "Klakson Dua Nada KL-2", "SND", "abcdefgIlmopuXY", AssetState.Installed, false, false, false, false, t),
            new LibraryEntry(Horn3, "Klakson Tiga Nada KL-3", "SND", "abcdefgIlmopuXY", AssetState.Installed, false, false, false, false, t),
            new LibraryEntry(HornFaulty, "Klakson Rusak", "SND", "abcdeFgIlmopuXY", AssetState.Faulty, false, false, false, false, t),
            new LibraryEntry(Bogey, "Bogie Tipe K-6", "CMP", "abcdefgIlmopuXY", AssetState.Installed, false, false, false, false, t),
        ]);
    }

    private string DbPath => Path.Combine(_root, "library.db");

    public void Dispose()
    {
        _sim.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<MainViewModel> ConnectedShell(ConnectionCheck? check = null)
    {
        var main = new MainViewModel(new FakeConnections(_sim, DbPath, Path.Combine(_root, "tx"), check ?? Connected));
        var connect = Assert.IsType<ConnectViewModel>(main.CurrentPage);
        await connect.CheckCommand.ExecuteAsync();
        connect.ConnectCommand.Execute(null);
        return main;
    }

    private static LibraryEntry Entry(MainViewModel main, Kuid k) => main.Session!.Library.Get(k)!;

    // ------------------------------------------------------------- UI-01

    [Fact]
    public async Task Connect_requires_check_then_opens_library()
    {
        var main = new MainViewModel(new FakeConnections(_sim, DbPath, Path.Combine(_root, "tx"), Connected));
        var connect = Assert.IsType<ConnectViewModel>(main.CurrentPage);
        Assert.Equal("Hubungkan", connect.Footer.Label);
        Assert.False(connect.Footer.IsEnabled);
        Assert.Equal("Periksa koneksi terlebih dahulu.", connect.Footer.Hint);
        Assert.False(main.Nav.Single(n => n.Key == "pustaka").IsEnabled);
        Assert.Equal("Belum terhubung", main.Connection.Text);

        await connect.CheckCommand.ExecuteAsync();
        Assert.Equal(Tone.Ok, connect.Status.Tone);
        Assert.True(connect.Footer.IsEnabled);
        connect.ConnectCommand.Execute(null);

        Assert.IsType<LibraryViewModel>(main.CurrentPage);
        Assert.True(main.Nav.Single(n => n.Key == "pustaka").IsActive);
        Assert.Equal("Terhubung", main.Connection.Text);
        // Modul yang belum ada tetap tampil dengan alasan.
        Assert.True(main.Nav.Single(n => n.Key == "riwayat").IsEnabled);
        Assert.All(main.Nav.Where(n => !n.IsEnabled), n => Assert.False(string.IsNullOrEmpty(n.DisabledReason)));
    }

    [Fact]
    public async Task Trainz_not_running_keeps_connect_disabled_with_steps()
    {
        var notRunning = new ConnectionCheck(ConnectionState.TrainzNotRunning, "", "Trainz belum tersambung.",
            [new CheckStep(false, "Aktifkan Advanced Debug Tools", "Launcher → Trainz Settings")]);
        var main = new MainViewModel(new FakeConnections(_sim, DbPath, Path.Combine(_root, "tx"), notRunning));
        var connect = (ConnectViewModel)main.CurrentPage;
        await connect.CheckCommand.ExecuteAsync();
        Assert.Equal("Trainz belum tersambung", connect.Status.Text);
        Assert.False(connect.ConnectCommand.CanExecute(null));
        Assert.Contains(connect.Steps, s => s.Title.Contains("Advanced Debug Tools"));
    }

    [Fact]
    public async Task Folder_without_tool_is_reported()
    {
        var main = new MainViewModel(new FakeConnections(_sim, DbPath, Path.Combine(_root, "tx"), Connected));
        var connect = (ConnectViewModel)main.CurrentPage;
        connect.AddFolder(@"D:\Bukan Trainz");
        Assert.Equal(2, connect.Installations.Count);
        await connect.CheckCommand.ExecuteAsync();
        Assert.Equal(Tone.Err, connect.Status.Tone);
        Assert.False(connect.Footer.IsEnabled);
    }

    // ------------------------------------------------------------- UI-02

    [Fact]
    public async Task Library_search_by_name_kuid_and_status_filter_AC35()
    {
        var main = await ConnectedShell();
        var lib = (LibraryViewModel)main.CurrentPage;
        Assert.Equal(7, lib.Results.Count);
        Assert.Equal("Bermasalah · 2", lib.Filters.Single(f => f.Filter == LibraryFilter.Faulty).Label);

        lib.SearchText = "klakson";
        Assert.Equal(3, lib.Results.Count);
        Assert.Equal("Bermasalah · 1", lib.Filters.Single(f => f.Filter == LibraryFilter.Faulty).Label);

        lib.SearchText = "<kuid:999001:1201>"; // tanpa revisi tetap menemukan kuid2
        var row = Assert.Single(lib.Results);
        Assert.Equal("Lokomotif Diesel DX-200", row.Name);
        Assert.Equal("Siap digunakan", row.Status.Text);

        lib.SearchText = "";
        lib.SelectFilterCommand.Execute(lib.Filters.Single(f => f.Filter == LibraryFilter.Faulty));
        Assert.Equal(2, lib.Results.Count);
        Assert.All(lib.Results, r => Assert.Equal("Aset bermasalah", r.Status.Text));
        Assert.Contains(lib.Results.Single(r => r.Name == "Gerbong Rusak").Extra, e => e.Text == "Komponen belum tersedia");
    }

    [Fact]
    public async Task Library_empty_states_explain_next_step()
    {
        var main = await ConnectedShell();
        var lib = (LibraryViewModel)main.CurrentPage;
        Assert.False(lib.OpenCommand.CanExecute(null));
        Assert.Equal("Pilih aset dari daftar.", lib.Footer.Hint);

        lib.SearchText = "tidak ada yang bernama begini";
        Assert.Empty(lib.Results);
        Assert.Equal("Tidak ada aset yang cocok.", lib.EmptyTitle);

        lib.SearchText = "<kuid:1:2>";
        Assert.Contains("KUID ini tidak ada", lib.EmptyHint);
    }

    [Fact]
    public async Task Empty_index_says_how_to_fill_it()
    {
        File.Delete(DbPath);
        var main = await ConnectedShell();
        var lib = (LibraryViewModel)main.CurrentPage;
        Assert.Equal("Belum ada data pustaka untuk profil ini.", lib.EmptyTitle);
        Assert.Contains("railnexus index", lib.EmptyHint);
    }

    // ------------------------------------------------------------- UI-03

    [Fact]
    public async Task Hub_offers_tasks_from_config_and_closes_edit_after_reading()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        var hub = Assert.IsType<EditHubViewModel>(main.CurrentPage);

        Assert.Null(hub.Error);
        Assert.False(_sim.IsOpen(Loco)); // dibaca lewat edit lalu revert
        Assert.Equal("Lokomotif Diesel DX-200", main.ActiveAsset!.Entry.Name);
        Assert.Equal("Siap digunakan", main.ActiveStatus!.Text);
        Assert.True(main.ShowDraftPanel);

        var cards = hub.Groups.SelectMany(g => g.Cards).ToDictionary(c => c.Title);
        Assert.True(cards["Ganti klakson"].IsAvailable);
        Assert.Contains("Klakson Dua Nada KL-2", cards["Ganti klakson"].Caption);
        Assert.Equal("Terpandu", cards["Ganti klakson"].Status.Text);
        Assert.Equal("track-sound · 3 target di aturan", cards["Suara roda-rel"].Caption);
        Assert.Equal("bogeys · 2 slot", cards["Ganti bogie"].Caption);
        Assert.True(cards["Tekstur & material"].IsAvailable);
        // Tugas yang belum ada: nonaktif dengan alasan, bukan disembunyikan.
        Assert.False(cards["Lihat model"].IsAvailable);
        Assert.All(cards.Values.Where(c => !c.IsAvailable), c => Assert.False(string.IsNullOrWhiteSpace(c.Reason)));
    }

    [Fact]
    public async Task Hub_explains_missing_roles_and_broken_config()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Wagon));
        var horn = ((EditHubViewModel)main.CurrentPage).Groups.SelectMany(g => g.Cards).Single(c => c.Title == "Ganti klakson");
        Assert.False(horn.IsAvailable);
        Assert.StartsWith("Tidak tersedia: aset ini tidak punya referensi hornsound", horn.Reason);

        await main.OpenAssetAsync(Entry(main, Broken));
        var bogie = ((EditHubViewModel)main.CurrentPage).Groups.SelectMany(g => g.Cards).Single(c => c.Title == "Ganti bogie");
        Assert.False(bogie.IsAvailable);
        Assert.Contains("error struktur", bogie.Reason);
        Assert.Equal("Aset bermasalah", main.ActiveStatus!.Text);
    }

    [Fact]
    public async Task Asset_open_elsewhere_is_not_taken_over_AC09()
    {
        var main = await ConnectedShell();
        _sim.OpenExternally(Loco);
        var revertsBefore = _sim.RevertCalls;
        await main.OpenAssetAsync(Entry(main, Loco));
        var hub = (EditHubViewModel)main.CurrentPage;
        Assert.Contains("masih terbuka untuk diedit", hub.Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(_sim.IsOpen(Loco));
        Assert.Equal(revertsBefore, _sim.RevertCalls);
        var roleTitles = new[] { "Ganti klakson", "Ganti suara mesin", "Suara roda-rel", "Ganti bogie" };
        Assert.All(hub.Groups.SelectMany(g => g.Cards).Where(c => roleTitles.Contains(c.Title)), c => Assert.False(c.IsAvailable));
    }

    // ------------------------------------------------------- draft + terapkan

    private static async Task<ReplaceViewModel> OpenRole(MainViewModel main, string title)
    {
        var hub = (EditHubViewModel)main.CurrentPage;
        hub.Groups.SelectMany(g => g.Cards).Single(c => c.Title == title).Open!.Execute(null);
        await Task.Yield();
        return Assert.IsType<ReplaceViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Replace_horn_shows_candidate_reasons_and_adds_to_draft()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        var replace = await OpenRole(main, "Ganti klakson");

        var slot = Assert.Single(replace.Slots);
        Assert.Equal("Klakson Dua Nada KL-2", slot.Current);
        Assert.Contains("Ketik nama", replace.EmptyHint);

        replace.SearchText = "klakson";
        var byName = replace.Candidates.ToDictionary(c => c.Name);
        Assert.False(byName["Klakson Dua Nada KL-2"].IsSelectable);
        Assert.Equal("Sama dengan yang dipakai sekarang.", byName["Klakson Dua Nada KL-2"].Reason);
        Assert.False(byName["Klakson Rusak"].IsSelectable);
        Assert.Equal("Aset bermasalah di profil ini.", byName["Klakson Rusak"].Reason);
        Assert.True(byName["Klakson Tiga Nada KL-3"].IsSelectable);
        Assert.Equal("Jenis belum dapat dipastikan", byName["Klakson Tiga Nada KL-3"].Verdict.Text);

        replace.SelectedCandidate = byName["Klakson Rusak"];
        Assert.False(replace.AddCommand.CanExecute(null));
        Assert.Equal("Aset bermasalah di profil ini.", replace.AddDisabledReason);

        replace.SelectedCandidate = byName["Klakson Tiga Nada KL-3"];
        replace.AddCommand.Execute(null);
        await main.Loading;
        Assert.IsType<EditHubViewModel>(main.CurrentPage);

        var draft = main.Draft!;
        Assert.Equal("1 perubahan siap", draft.CountLabel);
        Assert.Contains("Klakson Tiga Nada KL-3", draft.Items.Single().Summary);
        Assert.Contains(draft.Handled, h => h.Contains("kuid-table"));
        Assert.True(draft.Footer.IsEnabled);
        Assert.Equal("Terapkan & Selesai", main.CurrentPage.Footer.Label);
    }

    [Fact]
    public async Task Bogie_candidates_with_unknown_kind_are_not_offered()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        var replace = await OpenRole(main, "Ganti bogie");
        Assert.Equal(2, replace.Slots.Count);
        Assert.Contains("reversed 1", replace.Slots[1].Context);

        replace.SearchText = "bogie";
        var c = Assert.Single(replace.Candidates);
        Assert.False(c.IsSelectable);
        Assert.Contains("belum terbaca di indeks", c.Reason);
    }

    private static async Task AddHorn3(MainViewModel main)
    {
        var replace = await OpenRole(main, "Ganti klakson");
        replace.SearchText = "KL-3";
        replace.SelectedCandidate = replace.Candidates.Single();
        replace.AddCommand.Execute(null);
        await main.Loading;
    }

    [Fact]
    public async Task Apply_runs_one_transaction_and_reloads_hub()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        await AddHorn3(main);
        // Perubahan dari modul lain masuk draft yang sama (AC-33).
        main.ActiveAsset!.Draft.Put(new DraftItem.SetText("description", "Deskripsi baru", "Editor aset", "Ubah deskripsi"));
        main.ActiveAsset.NotifyDraftChanged();
        Assert.Equal("2 perubahan siap", main.Draft!.CountLabel);

        await main.Draft.ApplyCommand.ExecuteAsync();
        await main.Loading;

        Assert.Equal("Berhasil", main.Draft.Outcome!.Status.Text);
        Assert.NotNull(main.Draft.Outcome.TxId);
        var installed = Encoding.UTF8.GetString(_sim.InstalledFile(Loco, "config.txt")!);
        Assert.Contains("<kuid:999002:3103>", installed);
        Assert.Contains("\"Deskripsi baru\"", installed);
        Assert.Contains("custom-data-x                           \"dipertahankan\"", installed);
        Assert.False(_sim.IsOpen(Loco));

        Assert.True(main.Draft.IsEmpty);
        Assert.False(main.Draft.Footer.IsEnabled);
        Assert.Equal("Belum ada perubahan di draft.", main.Draft.Footer.Hint);
        // Hub membaca ulang isi terbaru: klakson sekarang KL-3.
        Assert.NotNull(main.ActiveAsset.Document);
        var horn = ((EditHubViewModel)main.CurrentPage).Groups.SelectMany(g => g.Cards).Single(c => c.Title == "Ganti klakson");
        Assert.Contains("Klakson Tiga Nada KL-3", horn.Caption);
    }

    [Fact]
    public async Task Draft_survives_navigation_AC37()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        await AddHorn3(main);

        main.NavigateCommand.Execute(main.Nav.Single(n => n.Key == "pustaka"));
        Assert.IsType<LibraryViewModel>(main.CurrentPage);
        Assert.Null(main.ActiveAsset);
        Assert.False(main.ShowDraftPanel);

        await main.OpenAssetAsync(Entry(main, Loco));
        Assert.Equal("1 perubahan siap", main.Draft!.CountLabel);
    }

    [Fact]
    public async Task External_change_after_draft_stops_with_conflict_AC10()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        await AddHorn3(main);
        _sim.ExternalEdit(Loco, "config.txt", Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(_locoConfig) + "// diubah di luar\r\n"));

        await main.Draft!.ApplyCommand.ExecuteAsync();
        await main.Loading;
        Assert.Equal("Konflik", main.Draft.Outcome!.Status.Text);
        Assert.Contains(main.Draft.Outcome.Details, d => d.Contains("config.txt"));
        Assert.EndsWith("// diubah di luar\r\n", Encoding.UTF8.GetString(_sim.InstalledFile(Loco, "config.txt")!));
        // Draft tetap ada; isi aset dibaca ulang sebelum mencoba lagi.
        Assert.Equal("1 perubahan siap", main.Draft.CountLabel);
    }

    [Fact]
    public async Task Read_only_build_disables_apply_with_reason()
    {
        var readOnly = new ConnectionCheck(ConnectionState.ReadOnly, "Build 999999", "Terhubung · build belum diuji, hanya baca", []);
        var main = await ConnectedShell(readOnly);
        Assert.Equal("Terhubung · hanya baca", main.Connection.Text);
        await main.OpenAssetAsync(Entry(main, Loco));
        await AddHorn3(main);
        Assert.False(main.Draft!.Footer.IsEnabled);
        Assert.Contains("belum diuji", main.Draft.Footer.Hint);
    }

    [Fact]
    public async Task Removing_draft_item_updates_panel()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        await AddHorn3(main);
        main.Draft!.Items.Single().RemoveCommand.Execute(null);
        Assert.True(main.Draft.IsEmpty);
        Assert.Empty(main.ActiveAsset!.Draft.Items);
        Assert.Equal("Belum ada perubahan di draft.", main.Draft.ApplyDisabledReason);
    }

    // ------------------------------------------------------------- UI-24

    private async Task<MainViewModel> AppliedHorn3()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        await AddHorn3(main);
        await main.Draft!.ApplyCommand.ExecuteAsync();
        await main.Loading;
        Assert.Equal("Berhasil", main.Draft.Outcome!.Status.Text);
        return main;
    }

    private static HistoryViewModel OpenHistory(MainViewModel main)
    {
        main.NavigateCommand.Execute(main.Nav.Single(n => n.Key == "riwayat"));
        return Assert.IsType<HistoryViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task History_lists_work_and_undo_restores_exact_bytes_AC13()
    {
        var main = await AppliedHorn3();
        var history = OpenHistory(main);
        var row = Assert.Single(history.Rows);
        Assert.Equal("Lokomotif Diesel DX-200", row.AssetName);
        Assert.Equal("Berhasil", row.Status.Text);
        Assert.Equal("Perubahan", row.KindLabel);
        Assert.Equal("config.txt", Assert.Single(row.Files).Path);
        Assert.Equal("Diubah", row.Files[0].Change);
        Assert.StartsWith("Backup ", row.BackupLabel);
        Assert.Contains(row.Steps, st => st.Phase == "Submitted");
        Assert.True(history.Footer.IsEnabled);

        await history.UndoCommand.ExecuteAsync();
        Assert.Equal("Berhasil", history.Outcome!.Status.Text);
        Assert.Equal(_locoConfig, _sim.InstalledFile(Loco, "config.txt"));

        Assert.Equal(2, history.Rows.Count);
        Assert.Equal("Undo", history.Rows[0].KindLabel);
        var original = history.Rows.Single(r => r.KindLabel == "Perubahan");
        history.Selected = original;
        Assert.StartsWith("Perubahan ini sudah dibatalkan", history.UndoDisabledReason);
        // Isi aset kedaluwarsa setelah undo; dibaca ulang saat aset dibuka lagi.
        Assert.Null(main.Session!.Workspaces[Loco].Document);
    }

    [Fact]
    public async Task Undo_after_external_edit_shows_conflict_AC14()
    {
        var main = await AppliedHorn3();
        var external = Encoding.UTF8.GetBytes("kuid <kuid2:999001:1201:2>\r\n// diubah editor lain\r\n");
        _sim.ExternalEdit(Loco, "config.txt", external);

        var history = OpenHistory(main);
        await history.UndoCommand.ExecuteAsync();
        Assert.Equal("Konflik", history.Outcome!.Status.Text);
        Assert.Contains(history.Outcome.Details, d => d.Contains("config.txt"));
        Assert.Equal(external, _sim.InstalledFile(Loco, "config.txt"));
    }

    [Fact]
    public async Task Uncertain_submit_is_reconciled_from_history_AC11()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        await AddHorn3(main);
        _sim.Commit = CommitBehavior.TimeoutNotApplied;
        await main.Draft!.ApplyCommand.ExecuteAsync();
        await main.Loading;
        Assert.Equal("Hasil submit belum pasti", main.Draft.Outcome!.Status.Text);
        Assert.Equal(1, _sim.CommitCalls);

        var history = OpenHistory(main);
        var row = history.Rows.Single(r => !r.IsClosed);
        history.Selected = row;
        Assert.Equal("Belum selesai · perlu rekonsiliasi", row.Status.Text);
        Assert.False(history.Footer.IsEnabled);
        Assert.Contains("rekonsiliasi", history.Footer.Hint);

        // Tanpa pilihan eksplisit, edit tertunda tidak dibuang.
        await history.ReconcileCommand.ExecuteAsync();
        Assert.Equal("Perlu tindakan", history.Outcome!.Status.Text);
        Assert.True(_sim.IsOpen(Loco));

        history.Selected = history.Rows.Single(r => !r.IsClosed);
        await history.DiscardPendingCommand.ExecuteAsync();
        Assert.Equal("Dibatalkan dan dipulihkan", history.Outcome!.Status.Text);
        Assert.False(_sim.IsOpen(Loco));
        Assert.Equal(_locoConfig, _sim.InstalledFile(Loco, "config.txt"));
        Assert.Equal(1, _sim.CommitCalls); // submit tidak diulang
    }

    [Fact]
    public async Task Asset_history_card_opens_filtered_history()
    {
        var main = await AppliedHorn3();
        var card = ((EditHubViewModel)main.CurrentPage).Groups.SelectMany(g => g.Cards).Single(c => c.Title == "Riwayat aset ini");
        Assert.True(card.IsAvailable);
        Assert.StartsWith("1 pekerjaan", card.Caption);
        card.Open!.Execute(null);
        var history = Assert.IsType<HistoryViewModel>(main.CurrentPage);
        Assert.Equal("Riwayat aset ini", history.Title);
        Assert.Single(history.Rows);
        history.Back();
        await main.Loading;
        Assert.IsType<EditHubViewModel>(main.CurrentPage);
    }

    // ------------------------------------------------------------- UI-25

    [Fact]
    public async Task Settings_edits_test_kuids_and_keeps_draft()
    {
        var store = new SettingsStore(Path.Combine(_root, "app-settings.json"));
        var main = new MainViewModel(new FakeConnections(_sim, DbPath, Path.Combine(_root, "tx"), Connected), store);
        var connect = (ConnectViewModel)main.CurrentPage;
        await connect.CheckCommand.ExecuteAsync();
        connect.ConnectCommand.Execute(null);
        await main.OpenAssetAsync(Entry(main, Loco));
        await AddHorn3(main);

        main.NavigateCommand.Execute(main.Nav.Single(n => n.Key == "pengaturan"));
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentPage);
        Assert.False(settings.Footer.IsEnabled);
        Assert.Contains(settings.Capabilities, c => c.Status.Text == "Terverifikasi");

        settings.NewKuid = "bukan kuid";
        settings.AddKuidCommand.Execute(null);
        Assert.NotNull(settings.KuidError);

        settings.NewKuid = "<kuid:-2:117104311>";
        settings.AddKuidCommand.Execute(null);
        Assert.Null(settings.KuidError);
        settings.NewKuid = "<kuid:-2:117104311>";
        settings.AddKuidCommand.Execute(null);
        Assert.Equal("KUID ini sudah ada di daftar.", settings.KuidError);

        Assert.True(settings.Footer.IsEnabled);
        settings.SaveCommand.Execute(null);
        Assert.Equal(["<kuid:-2:117104311>"], AppSettings.Load(store.Path).WritableTestKuids);
        Assert.Contains("Sambungkan ulang", settings.Footer.Hint);

        settings.TestKuids.Single().RemoveCommand.Execute(null);
        Assert.Empty(settings.TestKuids);
        Assert.True(settings.Footer.IsEnabled);

        // Menyambung ulang ke profil yang sama mempertahankan draft (AC-37, AC-21).
        settings.ChangeInstallationCommand.Execute(null);
        var reconnect = Assert.IsType<ConnectViewModel>(main.CurrentPage);
        await reconnect.CheckCommand.ExecuteAsync();
        reconnect.ConnectCommand.Execute(null);
        await main.OpenAssetAsync(Entry(main, Loco));
        Assert.Equal("1 perubahan siap", main.Draft!.CountLabel);
        Assert.Equal(@"C:\Simulasi\TRS19", AppSettings.Load(store.Path).TrainzFolder);
    }

    // ------------------------------------------------------------- UI-12

    private static EditorViewModel OpenEditor(MainViewModel main)
    {
        var hub = (EditHubViewModel)main.CurrentPage;
        hub.Groups.SelectMany(g => g.Cards).Single(c => c.Title == "Buka editor lanjutan").Open!.Execute(null);
        return Assert.IsType<EditorViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Editor_form_saves_plan_and_shows_diff_EDITAC()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        var editor = OpenEditor(main);
        Assert.True(main.ShowDraftPanel);

        var desc = editor.Fields.Single(f => f.Path == "description");
        Assert.True(desc.IsEditable);
        Assert.Contains(editor.Info, kv => kv.Key == "KUID" && kv.Value == "<kuid2:999001:1201:2>");
        Assert.Contains(editor.Nodes, n => n.Name == "bogeys" && n.Kind == "Container" && n.Children.Count == 2);
        Assert.Contains(editor.Lines, l => l.Text.StartsWith("hornsound", StringComparison.Ordinal));
        // Tanpa perubahan: tidak ada diff dan tindakan utama tetap Terapkan & Selesai (EDIT-AC).
        Assert.Empty(editor.Changes);
        Assert.Equal("Terapkan & Selesai", editor.Footer.Label);

        desc.Value = "Deskripsi baru\r\nbaris dua";
        Assert.Equal("Simpan rencana", editor.Footer.Label);
        editor.SaveCommand.Execute(null);
        Assert.Equal(EditorTab.Changes, editor.Tab);
        Assert.Equal("Terapkan & Selesai", editor.Footer.Label);
        Assert.Equal("1 perubahan siap", main.Draft!.CountLabel);
        Assert.Contains(editor.Changes, c => c.Kind == DiffKind.Removed && c.Text.Contains("Lokomotif diesel untuk lintas datar"));
        Assert.Contains(editor.Changes, c => c.Kind == DiffKind.Added && c.Text.Contains("\"Deskripsi baru\\nbaris dua\""));

        // Perubahan dari modul lain ikut terlihat di tab Perubahan.
        await AddHornFromEditorBack(main);
        editor = OpenEditor(main);
        Assert.Contains(editor.Changes, c => c.Kind == DiffKind.Added && c.Text.Contains("<kuid:999002:3103>"));

        // Mengembalikan nilai ke aslinya lalu menyimpan menghapus item dari draft.
        var d2 = editor.Fields.Single(f => f.Path == "description");
        d2.Value = d2.Original!;
        Assert.Equal("Simpan rencana", editor.Footer.Label);
        editor.SaveCommand.Execute(null);
        Assert.Equal("1 perubahan siap", main.Draft.CountLabel);
        Assert.DoesNotContain(main.ActiveAsset!.Draft.Items, i => i is DraftItem.SetText);
    }

    private static async Task AddHornFromEditorBack(MainViewModel main)
    {
        main.CurrentPage.Back();
        await main.Loading;
        await AddHorn3(main);
    }

    [Fact]
    public async Task Editor_on_broken_config_locks_form_and_lists_problems()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Broken));
        var editor = OpenEditor(main);
        Assert.All(editor.Fields, f => Assert.False(f.IsEditable));
        Assert.Contains("menunggu perbaikan", editor.Fields[1].LockedReason);
        Assert.NotEmpty(editor.Problems);
        Assert.StartsWith("Masalah · ", editor.ProblemsLabel);
        Assert.Contains(editor.Lines, l => l.Issue is not null);
    }

    // ------------------------------------------------------------- UI-14/15

    private string ImageFile(string name, byte[] bytes)
    {
        var dir = Path.Combine(_root, "gambar");
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static TextureViewModel OpenTexture(MainViewModel main)
    {
        var hub = (EditHubViewModel)main.CurrentPage;
        hub.Groups.SelectMany(g => g.Cards).Single(c => c.Title == "Tekstur & material").Open!.Execute(null);
        return Assert.IsType<TextureViewModel>(main.CurrentPage);
    }

    [Fact]
    public async Task Texture_panel_adds_converted_files_to_draft_and_applies()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        var tex = OpenTexture(main);
        Assert.True(tex.IsAssetMode);
        Assert.True(main.ShowDraftPanel);
        Assert.Equal("Konversi ke draft", tex.Footer.Label);
        Assert.Equal("Tambahkan gambar terlebih dahulu.", tex.PrimaryDisabledReason);

        tex.AddFiles([ImageFile("badan.png", ImageFixtures.PatternPngRgba()), ImageFile("badan_normal.png", ImageFixtures.PatternPngRgb())]);
        var body = tex.Items[0];
        Assert.StartsWith("PNG RGBA", body.Format);
        Assert.Equal("4 × 3", body.Size);
        Assert.Equal("Memiliki alpha", body.AlphaLabel.Text);
        Assert.Equal(TextureRole.Color, body.Role);
        Assert.Equal(TextureRole.Normal, tex.Items[1].Role); // tebakan nama hanya saran
        tex.TargetDirectory = "textures";
        Assert.True(tex.Footer.IsEnabled);

        tex.PrimaryCommand.Execute(null);
        Assert.Equal("Siap · 32-bit, dibaca ulang cocok", body.ResultStatus!.Text);
        Assert.Equal("4 perubahan siap", main.Draft!.CountLabel);

        main.CurrentPage.Back();
        await main.Loading;
        await main.Draft.ApplyCommand.ExecuteAsync();
        await main.Loading;
        Assert.Equal("Berhasil", main.Draft.Outcome!.Status.Text);
        Assert.NotNull(_sim.InstalledFile(Loco, Path.Combine("textures", "badan.tga")));
        Assert.Equal("Primary=badan.tga\r\nTile=st\r\n",
            Encoding.UTF8.GetString(_sim.InstalledFile(Loco, Path.Combine("textures", "badan.texture.txt"))!));
        Assert.Equal(_locoConfig, _sim.InstalledFile(Loco, "config.txt")); // tekstur saja tidak menyentuh config
    }

    [Fact]
    public async Task Texture_panel_blocks_on_name_collision_until_resolved_AC18()
    {
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Loco));
        var tex = OpenTexture(main);
        tex.AddFiles([ImageFile("a.png", ImageFixtures.PatternPngRgb())]);
        var other = Path.Combine(_root, "lain");
        Directory.CreateDirectory(other);
        File.WriteAllBytes(Path.Combine(other, "a.png"), ImageFixtures.PatternPngRgba());
        tex.AddFiles([Path.Combine(other, "a.png")]);

        Assert.All(tex.Items, i => Assert.NotNull(i.CollisionText));
        Assert.Equal("Selesaikan benturan nama sebelum melanjutkan.", tex.PrimaryDisabledReason);
        Assert.False(tex.Footer.IsEnabled);

        tex.Items[1].OutputName = "a_kedua";
        Assert.All(tex.Items, i => Assert.Null(i.CollisionText));
        Assert.True(tex.Footer.IsEnabled);
    }

    [Fact]
    public async Task Texture_inventory_finds_images_without_metadata_and_creates_it()
    {
        var thumb = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 };
        _sim.Install(Wagon, new()
        {
            ["config.txt"] = Encoding.UTF8.GetBytes("kuid <kuid:999001:1300>\r\nthumbnails\r\n{\r\n  0\r\n  {\r\n    image \"thumb.jpg\"\r\n  }\r\n}\r\n"),
            ["thumb.jpg"] = thumb,
            ["body.tga"] = TgaCodec.Encode(new RasterImage(1, 1, [1, 2, 3, 255], false), false),
            ["roof.texture.txt"] = Encoding.UTF8.GetBytes("Primary=roof.tga\r\n"),
        });
        var main = await ConnectedShell();
        await main.OpenAssetAsync(Entry(main, Wagon));
        var card = ((EditHubViewModel)main.CurrentPage).Groups.SelectMany(g => g.Cards).Single(c => c.Title == "Tekstur & material");
        Assert.Equal("2 gambar · 1 tanpa metadata", card.Caption);

        var tex = OpenTexture(main);
        var missingMeta = Assert.Single(tex.Inventory, i => i.Finding.Kind == InventoryKind.ImageWithoutMetadata);
        Assert.Equal("body.tga", missingMeta.Path); // thumbnail dari config tidak dihitung
        Assert.Contains(tex.Inventory, i => i.Finding.Kind == InventoryKind.MissingTarget && i.Path == "roof.texture.txt");
        Assert.True(missingMeta.IsSelected);

        tex.MetadataCommand.Execute(null);
        Assert.Equal("1 perubahan siap", main.Draft!.CountLabel);
        main.CurrentPage.Back();
        await main.Loading;
        await main.Draft.ApplyCommand.ExecuteAsync();
        await main.Loading;
        Assert.Equal("Primary=body.tga\r\nTile=st\r\n", Encoding.UTF8.GetString(_sim.InstalledFile(Wagon, "body.texture.txt")!));
    }

    [Fact]
    public void Texture_folder_mode_works_without_trainz_AC25()
    {
        var main = new MainViewModel(new FakeConnections(_sim, DbPath, Path.Combine(_root, "tx"), Connected));
        Assert.True(main.Nav.Single(n => n.Key == "workshop").IsEnabled); // sebelum tersambung
        main.NavigateCommand.Execute(main.Nav.Single(n => n.Key == "workshop"));
        var workshop = Assert.IsType<WorkshopViewModel>(main.CurrentPage);
        Assert.Equal(9, workshop.Modules.Count);
        Assert.Contains(workshop.Modules, m => m.Utility == "Images2TGA" && m.Status.Tone == Tone.Ok);

        workshop.OpenTextureFolderCommand.Execute(null);
        var tex = Assert.IsType<TextureViewModel>(main.CurrentPage);
        Assert.False(tex.IsAssetMode);
        Assert.False(main.ShowDraftPanel);
        tex.AddFiles([ImageFile("x.png", ImageFixtures.PatternPngRgb())]);
        Assert.Equal("Pilih folder tujuan.", tex.PrimaryDisabledReason);
        var outDir = Path.Combine(_root, "keluaran");
        Directory.CreateDirectory(outDir);
        tex.ExportFolder = outDir;
        tex.PrimaryCommand.Execute(null);
        Assert.Contains("Belum terpasang di Trainz", tex.Message);
        Assert.True(File.Exists(Path.Combine(outDir, "x.tga")));

        // Menjalankan lagi: berkas kini ada → benturan sampai pengguna memilih.
        Assert.NotNull(tex.Items[0].CollisionText);
        tex.Items[0].Collision = CollisionChoice.Replace;
        Assert.Null(tex.PrimaryDisabledReason);
    }
}
