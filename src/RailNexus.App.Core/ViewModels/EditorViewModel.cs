using System.Collections.ObjectModel;
using RailNexus.Core.Config;
using RailNexus.Core.Roles;

namespace RailNexus.App.Core.ViewModels;

public enum EditorTab { Form, Structure, Text, Changes, Problems }

/// <summary>Field formulir. Hanya field teks tunggal yang dapat diubah; sisanya ditampilkan dengan alasan.</summary>
public sealed class FormField : ObservableObject
{
    private string _value;

    public FormField(string path, string label, string help, string? original, string? draftValue, string? lockedReason)
    {
        Path = path;
        Label = label;
        Help = help;
        Original = original;
        _value = draftValue ?? original ?? "";
        LockedReason = lockedReason;
    }

    public string Path { get; }
    public string Label { get; }
    public string Help { get; }
    public string? Original { get; }
    public string? LockedReason { get; }
    public bool IsEditable => LockedReason is null;

    public string Value
    {
        get => _value;
        set { if (Set(ref _value, value ?? "")) OnPropertyChanged(nameof(IsChanged)); }
    }

    /// <summary>Nilai berbeda dari config asli (termasuk yang sudah ada di draft).</summary>
    public bool IsChanged => IsEditable && Normalize(Value) != Normalize(Original ?? "");

    internal static string Normalize(string s) => s.Replace("\r\n", "\n");
}

/// <summary>Node pohon Struktur.</summary>
public sealed record ConfigNode(string Name, string Value, string Kind, string Path, int Line, IReadOnlyList<ConfigNode> Children);

public sealed record TextLine(int Number, string Text, string? Issue);

public sealed record ProblemItem(int Line, string Code, StatusLabel Severity, string Message);

public sealed record ChangeLine(DiffKind Kind, string Marker, string OldNumber, string NewNumber, string Text);

/// <summary>
/// UI-12 Editor aset (terbatas, P0): Formulir untuk field teks yang aman, Struktur, Teks bernomor
/// baris, Masalah, dan Perubahan (diff draft terhadap isi asli). Perubahan masuk draft bersama
/// lewat "Simpan rencana"; config tidak ditulis ulang dari hasil parse (PRD 11.3).
/// </summary>
public sealed class EditorViewModel : ObservableObject, IPage, IDisposable
{
    private static readonly (string Path, string Label, string Help)[] EditableFields =
    [
        ("username", "Nama aset", "Nama yang tampil di Trainz dan pustaka."),
        ("description", "Deskripsi", "Teks keterangan aset. Baris baru disimpan sebagai \\n."),
    ];

    private static readonly (string Path, string Label)[] InfoFields =
    [
        ("kuid", "KUID"),
        ("kind", "Jenis (KIND)"),
        ("trainz-build", "Versi Trainz"),
        ("category-class", "Kelas kategori"),
    ];

    private readonly AssetWorkspace _workspace;
    private readonly Action _back;
    private readonly FooterAction _applyFooter;
    private readonly FooterAction _saveFooter;
    private EditorTab _tab = EditorTab.Form;
    private string _search = "";
    private readonly FooterAction _saveTextFooter;
    private string? _editableText;
    private string? _editBase;

    public EditorViewModel(AssetWorkspace workspace, FooterAction applyFooter, Action back)
    {
        _workspace = workspace;
        _back = back;
        _applyFooter = applyFooter;
        SaveCommand = new RelayCommand(SavePlan, HasUnsaved);
        _saveFooter = new FooterAction("Simpan rencana", SaveCommand, () => "Perubahan formulir belum masuk draft.");
        // Isian formulir yang belum disimpan harus masuk draft dulu agar tidak hilang saat teks disunting.
        EditTextCommand = new RelayCommand(BeginTextEdit, () => !IsTextEditing && _workspace.ConfigText is not null && !HasUnsaved());
        SaveTextCommand = new RelayCommand(SaveText, () => TextSaveProblem is null);
        CancelTextCommand = new RelayCommand(CancelText, () => IsTextEditing);
        _saveTextFooter = new FooterAction("Simpan teks ke draft", SaveTextCommand, () => TextSaveProblem ?? "Teks yang disunting belum masuk draft.");
        SelectTabCommand = new ParamCommand<string>(t => { if (Enum.TryParse<EditorTab>(t, out var tab)) Tab = tab; });
        Build();
        _workspace.Changed += OnWorkspaceChanged;
    }

    public void Dispose() => _workspace.Changed -= OnWorkspaceChanged;

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        FillChanges();
        SaveCommand.Refresh();
        OnPropertyChanged(nameof(Footer));
    }

    /// <summary>Isi formulir berbeda dari isi draft (perubahan baru, atau nilai yang dikembalikan ke aslinya).</summary>
    private bool HasUnsaved() =>
        Fields.Any(f => f.IsEditable && (f.IsChanged ? FormField.Normalize(f.Value) : null) != DraftValue(f.Path));

    public string Title => "Editor aset";
    /// <summary>Selama formulir punya perubahan yang belum disimpan, tindakan utama adalah Simpan rencana.</summary>
    public FooterAction Footer => IsTextDirty ? _saveTextFooter : SaveCommand.CanExecute(null) ? _saveFooter : _applyFooter;
    public string? BackLabel => "Kembali ke Mau edit apa?";
    public void Back() => _back();

    public EditorTab Tab { get => _tab; set => Set(ref _tab, value); }
    public ParamCommand<string> SelectTabCommand { get; }
    public RelayCommand SaveCommand { get; }

    public ObservableCollection<FormField> Fields { get; } = new();
    public ObservableCollection<KeyValuePair<string, string>> Info { get; } = new();
    public ObservableCollection<ConfigNode> Nodes { get; } = new();
    public ObservableCollection<TextLine> Lines { get; } = new();
    public ObservableCollection<ProblemItem> Problems { get; } = new();
    public ObservableCollection<ChangeLine> Changes { get; } = new();
    public ObservableCollection<string> OtherChanges { get; } = new();
    public string? ChangesError { get; private set; }
    public string? Unavailable { get; private set; }

    public string SearchText
    {
        get => _search;
        set { if (Set(ref _search, value ?? "")) FillLines(); }
    }

    public string ProblemsLabel => Problems.Count == 0 ? "Masalah" : $"Masalah · {Problems.Count}";

    // ------------------------------------------------------------------ editor teks penuh (P1)

    public RelayCommand EditTextCommand { get; }
    public RelayCommand SaveTextCommand { get; }
    public RelayCommand CancelTextCommand { get; }
    public ObservableCollection<ProblemItem> TextProblems { get; } = new();

    public bool IsTextEditing => _editableText is not null;

    /// <summary>Teks yang sedang disunting: isi config setelah seluruh draft, disunting langsung.</summary>
    public string? EditableText
    {
        get => _editableText;
        set
        {
            if (_editableText is null || !Set(ref _editableText, value ?? "")) return;
            ValidateText();
        }
    }

    /// <summary>Teks berbeda dari dasar suntingan dan belum masuk draft.</summary>
    public bool IsTextDirty => _editableText is not null && Normalize(_editableText) != _editBase;

    /// <summary>Alasan teks belum dapat masuk draft; null bila siap (PRD 11.3: teks tidak valid tidak ditulis).</summary>
    public string? TextSaveProblem =>
        !IsTextEditing ? "Mulai sunting teks dulu."
        : !IsTextDirty ? "Belum ada perubahan teks."
        : TextProblems.FirstOrDefault(p => p.Severity.Tone == Tone.Err) is { } e ? $"Baris {e.Line}: {e.Message}. Perbaiki sebelum menyimpan."
        : null;

    private string Normalize(string text)
    {
        // Akhir baris mengikuti gaya config asli agar baris yang tidak disentuh tetap identik.
        var nl = _workspace.Document?.NewLine ?? "\r\n";
        return text.Replace("\r\n", "\n").Replace("\n", nl);
    }

    private void BeginTextEdit()
    {
        if (_workspace.ConfigText is not { } original) return;
        var build = _workspace.Draft.Build(original);
        _editBase = build.Ok ? build.NewText : original;
        _editableText = _editBase;
        OnPropertyChanged(nameof(EditableText));
        ValidateText();
    }

    private void ValidateText()
    {
        TextProblems.Clear();
        if (_editableText is { } t)
            foreach (var d in ConfigDocument.Parse(Normalize(t)).Diagnostics.OrderBy(d => d.Line))
                TextProblems.Add(new ProblemItem(d.Line, d.Code, d.Severity == Severity.Error ? new StatusLabel("Error", Tone.Err)
                    : d.Severity == Severity.Warning ? new StatusLabel("Peringatan", Tone.Warn) : new StatusLabel("Info", Tone.Info), d.Message));
        RefreshTextState();
    }

    private void RefreshTextState()
    {
        foreach (var n in new[] { nameof(IsTextEditing), nameof(IsTextDirty), nameof(TextSaveProblem), nameof(Footer) }) OnPropertyChanged(n);
        EditTextCommand.Refresh();
        SaveTextCommand.Refresh();
        CancelTextCommand.Refresh();
        _saveTextFooter.Refresh();
        Build(keepText: true);
    }

    /// <summary>
    /// Seluruh perubahan config di draft digantikan satu teks utuh (teks itu sudah memuat efeknya);
    /// berkas non-config di draft tetap.
    /// </summary>
    private void SaveText()
    {
        if (TextSaveProblem is not null || _workspace.ConfigText is not { } original) return;
        foreach (var item in _workspace.Draft.Items.Where(i => i.ChangesConfig).ToList()) _workspace.Draft.Remove(item);
        _workspace.Draft.Put(new DraftItem.ReplaceConfigText(Normalize(_editableText!), Draft.TextHash(original), "Editor aset", "Sunting teks config"));
        _editableText = null;
        _editBase = null;
        OnPropertyChanged(nameof(EditableText));
        _workspace.NotifyDraftChanged();
        RefreshTextState();
        Tab = EditorTab.Changes;
    }

    private void CancelText()
    {
        _editableText = null;
        _editBase = null;
        TextProblems.Clear();
        OnPropertyChanged(nameof(EditableText));
        RefreshTextState();
    }

    private string? DraftValue(string path) => _workspace.Draft.Items.OfType<DraftItem.SetText>()
        .FirstOrDefault(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) is { } d ? FormField.Normalize(d.Value) : null;

    private void Build(bool keepText = false)
    {
        Fields.Clear(); Info.Clear(); Nodes.Clear(); Problems.Clear();
        if (_workspace.Document is not { } doc)
        {
            Unavailable = _workspace.ReadError ?? "Isi aset belum terbaca. Kembali dan buka ulang aset.";
            OnPropertyChanged(nameof(Unavailable));
            return;
        }

        foreach (var (path, label, help) in EditableFields)
        {
            var hits = doc.Find(path);
            string? locked = IsTextDirty ? "Teks sedang disunting; simpan atau batalkan suntingan teks dulu agar keduanya tidak saling menimpa."
                : doc.HasErrors ? "Config memiliki error struktur; formulir menunggu perbaikan agar tidak menulis berdasarkan hasil baca yang salah."
                : hits.Count == 0 ? "Field ini tidak ada di config; menambah field baru belum didukung."
                : hits.Count > 1 ? $"Field ini muncul {hits.Count} kali; struktur ambigu tidak diubah otomatis."
                : hits[0].Value is not { Kind: ValueKind.String or ValueKind.Bare } ? "Nilai field ini bukan teks."
                : null;
            var original = hits.Count == 1 ? hits[0].Value?.Text : null;
            var field = new FormField(path, label, help, original, DraftValue(path), locked);
            field.PropertyChanged += (_, _) => { SaveCommand.Refresh(); EditTextCommand?.Refresh(); OnPropertyChanged(nameof(Footer)); };
            Fields.Add(field);
        }
        foreach (var (path, label) in InfoFields)
            if (doc.Find(path) is { Count: 1 } h && h[0].Value is { } v) Info.Add(new(label, v.Text));

        foreach (var t in doc.Root.Children) Nodes.Add(ToNode(t));
        foreach (var d in doc.Diagnostics.OrderBy(d => d.Line))
            Problems.Add(new ProblemItem(d.Line, d.Code, d.Severity switch
            {
                Severity.Error => new StatusLabel("Error", Tone.Err),
                Severity.Warning => new StatusLabel("Peringatan", Tone.Warn),
                _ => new StatusLabel("Info", Tone.Info),
            }, d.Message));
        FillLines();
        FillChanges();
        OnPropertyChanged(nameof(ProblemsLabel));
        if (!keepText) OnPropertyChanged(nameof(Footer));
    }

    private static ConfigNode ToNode(ConfigTag t) => new(
        t.Name,
        t.Value?.Text ?? (t.Container is null ? "" : $"{t.Container.Children.Count} isi"),
        t.Container is not null ? "Container" : t.Value?.Kind switch
        {
            ValueKind.Kuid => "KUID",
            ValueKind.String => "Teks",
            ValueKind.Number => "Angka",
            ValueKind.List => "Daftar",
            _ => "Nilai",
        },
        t.Path, t.Line,
        t.Container?.Children.Select(ToNode).ToList() ?? []);

    private void FillLines()
    {
        Lines.Clear();
        if (_workspace.ConfigText is not { } text) return;
        var lines = LineDiff.SplitLines(text);
        // Diagnosis di akhir berkas (mis. container tidak ditutup) ditampilkan pada baris terakhir.
        var issues = _workspace.Document?.Diagnostics
            .GroupBy(d => Math.Clamp(d.Line, 1, Math.Max(1, lines.Count)))
            .ToDictionary(g => g.Key, g => string.Join("; ", g.Select(d => d.Message))) ?? [];
        for (var i = 0; i < lines.Count; i++)
        {
            if (SearchText.Length > 0 && !lines[i].Contains(SearchText, StringComparison.OrdinalIgnoreCase)) continue;
            Lines.Add(new TextLine(i + 1, lines[i], issues.GetValueOrDefault(i + 1)));
        }
    }

    /// <summary>Diff seluruh draft terhadap isi asli, termasuk perubahan dari modul lain (PRD 19.2 langkah 5).</summary>
    public void FillChanges()
    {
        Changes.Clear();
        OtherChanges.Clear();
        ChangesError = null;
        if (_workspace.ConfigText is { } original)
        {
            var build = _workspace.Draft.Build(original);
            if (!build.Ok) ChangesError = build.Error;
            else
                foreach (var d in LineDiff.WithContext(LineDiff.Compare(original, build.NewText)))
                    Changes.Add(new ChangeLine(d.Kind, d.Kind switch { DiffKind.Added => "+", DiffKind.Removed => "−", _ => " " },
                        d.OldLine?.ToString() ?? "", d.NewLine?.ToString() ?? "", d.Text));
        }
        foreach (var f in _workspace.Draft.Items.OfType<DraftItem.WriteFile>()) OtherChanges.Add($"{f.Module}: {f.Summary}");
        OnPropertyChanged(nameof(ChangesError));
    }

    private void SavePlan()
    {
        foreach (var f in Fields.Where(f => f.IsEditable))
        {
            var existing = _workspace.Draft.Items.OfType<DraftItem.SetText>()
                .FirstOrDefault(i => i.Path.Equals(f.Path, StringComparison.OrdinalIgnoreCase));
            if (f.IsChanged)
                _workspace.Draft.Put(new DraftItem.SetText(f.Path, FormField.Normalize(f.Value), "Editor aset", $"Ubah {f.Label.ToLowerInvariant()}"));
            else if (existing is not null)
                _workspace.Draft.Remove(existing); // nilai dikembalikan ke aslinya
        }
        _workspace.NotifyDraftChanged(); // memicu FillChanges lewat OnWorkspaceChanged
        Tab = EditorTab.Changes;
    }
}
