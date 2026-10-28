using System.Text;
using RailNexus.Core.Config;
using RailNexus.Core.Tx;

namespace RailNexus.Core.Roles;

/// <summary>Satu perubahan di draft, disimpan sebagai maksud (bukan rentang) agar tetap valid setelah perubahan lain.</summary>
public abstract record DraftItem(string Module, string Summary)
{
    public sealed record ReplaceKuid(string RoleId, string SlotPath, Kuid Old, Kuid New, string Module, string Summary)
        : DraftItem(Module, Summary);

    public sealed record SetText(string Path, string Value, string Module, string Summary)
        : DraftItem(Module, Summary);

    /// <summary>
    /// Menulis satu berkas selain config.txt, mis. hasil Workshop Tekstur. BaseHash adalah hash berkas
    /// di tujuan saat hasil disiapkan (null = belum ada), sehingga perubahan sesudahnya terdeteksi (AC-10).
    /// </summary>
    public sealed record WriteFile(string RelativePath, byte[] Bytes, string? BaseHash, string Module, string Summary)
        : DraftItem(Module, Summary);

    public bool ChangesConfig => this is ReplaceKuid or SetText;
}

public sealed record DraftBuild(
    bool Ok,
    string Error,
    string NewText,
    IReadOnlyList<string> Handled,
    IReadOnlyList<string> Uncertain);

/// <summary>
/// Draft bersama lintas modul (PRD 9.5). Setiap item diterapkan berurutan pada config yang
/// di-parse ulang, sehingga perubahan dari Workshop Suara dan Editor Aset tidak saling
/// menimpa (AC-33). Hasil akhirnya satu <see cref="EditPlan"/> untuk mesin transaksi.
/// </summary>
public sealed class Draft(Kuid asset)
{
    private readonly List<DraftItem> _items = new();

    public Kuid Asset => asset;
    public IReadOnlyList<DraftItem> Items => _items;

    /// <summary>Menambah atau mengganti item pada lokasi yang sama.</summary>
    public void Put(DraftItem item)
    {
        _items.RemoveAll(i => SameTarget(i, item));
        _items.Add(item);
    }

    public bool Remove(DraftItem item) => _items.Remove(item);

    private static bool SameTarget(DraftItem a, DraftItem b) => (a, b) switch
    {
        (DraftItem.ReplaceKuid x, DraftItem.ReplaceKuid y) => x.SlotPath.Equals(y.SlotPath, StringComparison.OrdinalIgnoreCase),
        (DraftItem.SetText x, DraftItem.SetText y) => x.Path.Equals(y.Path, StringComparison.OrdinalIgnoreCase),
        (DraftItem.WriteFile x, DraftItem.WriteFile y) => SamePath(x.RelativePath, y.RelativePath),
        _ => false,
    };

    private static bool SamePath(string a, string b) =>
        a.Replace('\\', '/').Equals(b.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

    public bool HasConfigChanges => _items.Any(i => i.ChangesConfig);

    public void Clear() => _items.Clear();

    public DraftBuild Build(string originalText)
    {
        var text = originalText;
        var handled = new List<string>();
        var uncertain = new List<string>();
        foreach (var item in _items)
        {
            var doc = ConfigDocument.Parse(text);
            switch (item)
            {
                case DraftItem.ReplaceKuid r:
                {
                    var role = RoleCatalog.ById(r.RoleId);
                    if (role is null) return Fail($"Peran '{r.RoleId}' tidak dikenal.");
                    var slot = role.Locate(doc).FirstOrDefault(s => s.Path.Equals(r.SlotPath, StringComparison.OrdinalIgnoreCase));
                    if (slot is null) return Fail($"{r.Summary}: lokasi tidak ditemukan lagi.");
                    if (!References.SameAsset(slot.Current, r.Old))
                        return Fail($"{r.Summary}: nilai sekarang {slot.Current}, bukan {r.Old} seperti saat draft dibuat.");
                    var plan = ModificationEngine.PlanReplace(doc, role, r.SlotPath, r.New);
                    if (!plan.Ok) return Fail($"{r.Summary}: {plan.Error}");
                    text = ConfigEditor.Apply(text, plan.Edits);
                    handled.AddRange(plan.Handled);
                    uncertain.AddRange(plan.Uncertain);
                    break;
                }
                case DraftItem.SetText s:
                {
                    var (tag, err) = ConfigEditor.Single(doc, s.Path);
                    if (tag is null) return Fail($"{s.Summary}: {err}");
                    var e = ConfigEditor.SetString(doc, tag, s.Value, s.Summary);
                    if (!e.Ok) return Fail($"{s.Summary}: {e.Message}");
                    text = ConfigEditor.Apply(text, [e.Edit!]);
                    handled.Add($"{s.Summary} di '{s.Path}'");
                    break;
                }
                case DraftItem.WriteFile f:
                    // Berkas lain tidak menyentuh config; isinya ikut rencana di ToEditPlan.
                    handled.Add(f.Summary);
                    break;
            }
        }

        var check = ConfigDocument.Parse(text);
        if (check.HasErrors) return Fail("Hasil perubahan tidak dapat dibaca ulang dengan benar; draft tidak diterapkan.");
        return new DraftBuild(true, "", text, handled, uncertain);

        DraftBuild Fail(string msg) => new(false, msg, originalText, handled, uncertain);
    }

    /// <summary>Membentuk rencana transaksi untuk config.txt, mengikat hash dasar saat draft dibangun (AC-10).</summary>
    public (EditPlan? Plan, DraftBuild Build) ToEditPlan(byte[] originalBytes, string purpose)
    {
        var hasBom = originalBytes.Length >= 3 && originalBytes[0] == 0xEF && originalBytes[1] == 0xBB && originalBytes[2] == 0xBF;
        var encoding = new UTF8Encoding(hasBom);
        var text = encoding.GetString(originalBytes, hasBom ? 3 : 0, originalBytes.Length - (hasBom ? 3 : 0));
        // Config bukan UTF-8 (mis. ANSI dengan huruf non-ASCII) tidak boleh ditulis ulang lewat
        // decoding yang merusak byte lain; berhenti daripada menebak encoding.
        var roundTrip = (hasBom ? encoding.GetPreamble() : []).Concat(encoding.GetBytes(text));
        if (!roundTrip.SequenceEqual(originalBytes))
            return (null, new DraftBuild(false, "Encoding config tidak dikenali sebagai UTF-8; perubahan otomatis tidak dilakukan.", text, [], []));
        var build = Build(text);
        if (!build.Ok) return (null, build);

        var files = _items.OfType<DraftItem.WriteFile>().ToList();
        if (files.FirstOrDefault(f => SamePath(f.RelativePath, "config.txt")) is { } bad)
            return (null, build with { Ok = false, Error = $"{bad.Summary}: config.txt hanya diubah lewat perubahan config, bukan sebagai berkas utuh." });

        var changes = new List<FileChange>();
        var expected = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        // config.txt hanya ikut ditulis bila ada perubahan config; draft berisi tekstur saja tidak menyentuhnya.
        if (HasConfigChanges)
        {
            var bytes = (hasBom ? encoding.GetPreamble() : []).Concat(encoding.GetBytes(build.NewText)).ToArray();
            changes.Add(new FileChange("config.txt", bytes));
            expected["config.txt"] = Hashing.Sha256(originalBytes);
        }
        foreach (var f in files)
        {
            changes.Add(new FileChange(f.RelativePath, f.Bytes));
            expected[f.RelativePath] = f.BaseHash;
        }
        if (changes.Count == 0) return (null, build with { Ok = false, Error = "Draft kosong; tidak ada yang diterapkan." });
        return (new EditPlan(asset, purpose, changes, expected), build);
    }
}
