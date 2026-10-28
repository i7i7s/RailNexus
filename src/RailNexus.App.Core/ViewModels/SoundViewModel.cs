using System.Collections.ObjectModel;
using RailNexus.Core;
using RailNexus.Core.Audio;
using RailNexus.Core.Config;
using RailNexus.Core.Roles;

namespace RailNexus.App.Core.ViewModels;

/// <summary>Satu berkas suara lokal aset beserta calon penggantinya.</summary>
public sealed class SoundFileItem : ObservableObject
{
    private readonly SoundViewModel _owner;
    private string? _replacementPath;
    private byte[]? _replacementBytes;
    private WavInfo? _replacementInfo;

    internal SoundFileItem(SoundViewModel owner, string path, byte[]? bytes, string? notRetainedReason, IReadOnlyList<string> usedIn)
    {
        _owner = owner;
        Path = path;
        Bytes = bytes;
        Info = bytes is null ? WavInfo.Fail(notRetainedReason ?? "Isi berkas tidak tersedia.") : WavInfo.Read(bytes);
        UsedIn = usedIn.Count == 0 ? "Tidak disebut langsung di config (mungkin dipakai soundscript atau berkas lain)." : "Disebut di: " + string.Join(", ", usedIn);
        PlayCommand = new RelayCommand(() => _owner.PlayOld(this), () => Info.Ok);
        PlayNewCommand = new RelayCommand(() => _owner.PlayNew(this), () => _replacementInfo is { Ok: true });
        AddToDraftCommand = new RelayCommand(() => _owner.AddToDraft(this), () => ReplacementProblem is null);
    }

    public string Path { get; }
    internal byte[]? Bytes { get; }
    public WavInfo Info { get; }
    public string Description => Info.Description;
    public string UsedIn { get; }
    public RelayCommand PlayCommand { get; }
    public RelayCommand PlayNewCommand { get; }
    public RelayCommand AddToDraftCommand { get; }

    public string? ReplacementPath => _replacementPath;
    internal byte[]? ReplacementBytes => _replacementBytes;
    public string? ReplacementDescription => _replacementInfo?.Description;
    public string? Differences => _replacementInfo is null ? null
        : string.Join(" ", WavInfo.Differences(Info, _replacementInfo)) is { Length: > 0 } d ? d : null;

    /// <summary>Alasan pengganti belum dapat masuk draft; null bila siap.</summary>
    public string? ReplacementProblem =>
        _replacementPath is null ? "Pilih berkas WAV pengganti."
        : _replacementInfo is not { Ok: true } i ? $"Pengganti tidak dapat dipakai: {_replacementInfo?.Error}"
        : null;

    public void ChooseReplacement(string path)
    {
        _replacementPath = path;
        try
        {
            _replacementBytes = File.ReadAllBytes(path);
            _replacementInfo = WavInfo.Read(_replacementBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _replacementBytes = null;
            _replacementInfo = WavInfo.Fail($"Berkas tidak dapat dibaca: {ex.Message}");
        }
        foreach (var n in new[] { nameof(ReplacementPath), nameof(ReplacementDescription), nameof(Differences), nameof(ReplacementProblem) })
            OnPropertyChanged(n);
        PlayNewCommand.Refresh();
        AddToDraftCommand.Refresh();
    }
}

/// <summary>
/// Workshop Suara untuk berkas lokal (UI-17, RN-013/022): dengarkan WAV yang dapat dibaca, bandingkan
/// dengan pengganti, lalu ganti berkas lewat transaksi berkas. Tidak menambah dependency KUID (AC-29).
/// Playback berhenti saat halaman ditinggalkan (AUDIO-AC).
/// </summary>
public sealed class SoundViewModel : ObservableObject, IPage, IDisposable
{
    public static readonly string[] AudioExtensions = [".wav", ".ogg"];

    private readonly AssetWorkspace _workspace;
    private readonly IAudioPlayer _player;
    private readonly Action _back;
    private double _volume = 0.8;
    private string? _status;

    public SoundViewModel(AssetWorkspace workspace, IAudioPlayer player, FooterAction footer, Action back)
    {
        _workspace = workspace;
        _player = player;
        _back = back;
        Footer = footer;
        StopCommand = new RelayCommand(Stop);
        Build();
    }

    public string Title => "Workshop Suara";
    public FooterAction Footer { get; }
    public string? BackLabel => "Kembali ke Mau edit apa?";
    public void Back() => _back();
    public void Dispose() => _player.Stop();

    public ObservableCollection<SoundFileItem> Files { get; } = new();
    public RelayCommand StopCommand { get; }
    public string? Status { get => _status; private set => Set(ref _status, value); }
    public string Note => "Preview memutar berkas apa adanya; bukan simulasi suara di dalam Trainz. Volume preview tidak mengubah aset.";
    public string? EmptyHint => Files.Count > 0 ? null : _workspace.Snapshot is null ? "Isi aset belum terbaca." : "Aset ini tidak memiliki berkas suara lokal.";

    /// <summary>Volume preview 0–1.</summary>
    public double Volume { get => _volume; set => Set(ref _volume, Math.Clamp(value, 0, 1)); }

    public static int CountAudio(AssetSnapshot snapshot) =>
        snapshot.Files.Count(f => AudioExtensions.Any(e => f.RelativePath.EndsWith(e, StringComparison.OrdinalIgnoreCase)));

    private void Build()
    {
        Files.Clear();
        if (_workspace.Snapshot is not { } snap) return;
        var strings = _workspace.Document?.AllTags()
            .Where(t => t.Value is { Kind: ValueKind.String or ValueKind.Bare })
            .Select(t => (t.Path, Value: t.Value!.Text.Replace('\\', '/'))).ToList() ?? [];
        foreach (var f in snap.Files.Where(f => AudioExtensions.Any(e => f.RelativePath.EndsWith(e, StringComparison.OrdinalIgnoreCase))))
        {
            byte[]? bytes = null;
            string? reason = null;
            try { bytes = snap.Read(f.RelativePath); }
            catch (NotSupportedException ex) { reason = ex.Message; }
            var name = System.IO.Path.GetFileName(f.RelativePath);
            var used = strings.Where(s => s.Value.Equals(f.RelativePath, StringComparison.OrdinalIgnoreCase)
                                          || s.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(s => s.Path).ToList();
            Files.Add(new SoundFileItem(this, f.RelativePath, bytes, reason, used));
        }
        OnPropertyChanged(nameof(EmptyHint));
    }

    internal void PlayOld(SoundFileItem item) => Play($"{item.Path} (sekarang)", item.Bytes!);

    internal void PlayNew(SoundFileItem item) => Play($"{System.IO.Path.GetFileName(item.ReplacementPath)} (pengganti)", item.ReplacementBytes!);

    private void Play(string label, byte[] wav)
    {
        _player.Stop();
        var error = _player.Play(label, wav, Volume);
        Status = error ?? $"Memutar: {label}";
    }

    private void Stop()
    {
        _player.Stop();
        Status = "Dihentikan.";
    }

    /// <summary>Berkas diganti dengan nama yang sama: referensi config tetap, tidak ada KUID baru (AC-29).</summary>
    internal void AddToDraft(SoundFileItem item)
    {
        if (item.ReplacementProblem is not null || _workspace.Snapshot is not { } snap) return;
        _workspace.Draft.Put(new DraftItem.WriteFile(item.Path, item.ReplacementBytes!, snap.Hash(item.Path), "Workshop Suara",
            $"Ganti berkas suara {item.Path} dengan {System.IO.Path.GetFileName(item.ReplacementPath)}"));
        _workspace.NotifyDraftChanged();
        Status = $"{item.Path} masuk draft." + (item.Differences is { } d ? $" Perhatikan: {d}" : "");
    }
}
