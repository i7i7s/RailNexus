using RailNexus.Core;
using RailNexus.Core.Config;
using RailNexus.Core.Dependencies;
using RailNexus.Core.Roles;
using RailNexus.Core.Texture;

namespace RailNexus.Tests;

public sealed class DependencyTests
{
    private static readonly string Loco =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "configs", "loco-dx200.txt"));
    private static readonly Kuid Self = Kuid.Parse("<kuid2:999001:1201:2>");

    private static Kuid K(string s) => Kuid.Parse(s);

    [Fact]
    public void Inventory_labels_roles_and_keeps_unknown_references_unclassified()
    {
        var refs = DependencyInventory.Collect(ConfigDocument.Parse(Loco), Self);
        Assert.DoesNotContain(refs, r => References.SameAsset(r.Target, Self));
        Assert.Equal("Klakson · Klakson", refs.Single(r => r.Location == "hornsound").RoleLabel);
        Assert.Equal(2, refs.Count(r => r.RoleId == "bogie"));
        Assert.Equal("horn", refs.Single(r => r.InKuidTable && r.Target == K("<kuid:999002:3101>")).SymbolicName);
        // track filter di aturan 0 adalah target KUID; tetap dikenali lewat peran tracksound.
        Assert.All(refs.Where(r => r.Location.StartsWith("track-sound", StringComparison.Ordinal)), r => Assert.Equal("tracksound", r.RoleId));
    }

    [Fact]
    public void Faulty_dependency_is_not_reported_as_missing_AC08()
    {
        var doc = ConfigDocument.Parse(Loco);
        var refs = DependencyInventory.Collect(doc, Self);
        var problems = Diagnosis.Diagnose(Self, false, doc, refs,
            k => k.ContentId switch { 3101 => TargetState.Faulty, 6005 => TargetState.Missing, _ => TargetState.Installed }, null, []);

        var faulty = problems.Single(p => p.Target is { ContentId: 3101 });
        Assert.Equal(ProblemClass.InstalledFaulty, faulty.Class);
        Assert.DoesNotContain("belum tersedia", faulty.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(faulty.Actions, a => a.Kind == RepairActionKind.OpenTarget);
        Assert.Contains(faulty.Actions, a => a.Kind == RepairActionKind.ChooseReplacement && a.RoleId == "hornsound");

        var missing = problems.Single(p => p.Target is { ContentId: 6005 });
        Assert.Equal(ProblemClass.NotInstalled, missing.Class);
        Assert.Contains("bogeys/0/bogey", missing.Evidence);
        Assert.Contains("bogeys/1/bogey", missing.Evidence);
    }

    [Fact]
    public void Not_indexed_is_only_suspected_when_trainz_flags_missing_dependencies()
    {
        var doc = ConfigDocument.Parse(Loco);
        var refs = DependencyInventory.Collect(doc, Self);
        Assert.Empty(Diagnosis.Diagnose(Self, false, doc, refs, _ => TargetState.NotIndexed, null, []));
        var suspected = Diagnosis.Diagnose(Self, true, doc, refs, _ => TargetState.NotIndexed, null, []);
        Assert.All(suspected, p => Assert.StartsWith("Kemungkinan belum tersedia", p.Title));
    }

    [Fact]
    public void Reference_missing_from_kuid_table_offers_rule_R001_and_draft_adds_entry_AC28()
    {
        var text = Loco.Replace("  0                                     <kuid:999003:4100>\n", "")
                       .Replace("  0                                     <kuid:999003:4100>\r\n", "");
        var doc = ConfigDocument.Parse(text);
        var refs = DependencyInventory.Collect(doc, Self);
        var p = Assert.Single(Diagnosis.Diagnose(Self, false, doc, refs, _ => TargetState.Installed, null, []));
        Assert.Equal(ProblemClass.Inconsistent, p.Class);
        var action = Assert.Single(p.Actions);
        Assert.Equal("R001", action.RuleId);

        var draft = new Draft(Self);
        draft.Put(new DraftItem.AddKuidTableEntry(action.Target!.Value, "Perbaikan", "Tambahkan enginesound ke kuid-table"));
        var build = draft.Build(text);
        Assert.True(build.Ok, build.Error);
        var table = ConfigDocument.Parse(build.NewText).Root.Named("kuid-table").Single().Container!;
        Assert.Equal("horn", table.Children[0].Name); // nama simbolik tetap
        Assert.Contains(table.Children, c => c.Name == "5" && c.Value!.Kuid == K("<kuid:999003:4100>"));

        // Menambah lagi tidak menggandakan entri.
        var again = new Draft(Self);
        again.Put(new DraftItem.AddKuidTableEntry(K("<kuid:999003:4100>"), "Perbaikan", "lagi"));
        Assert.Equal(build.NewText, again.Build(build.NewText).NewText);
    }

    [Fact]
    public void Validator_errors_are_shown_as_is_and_texture_metadata_problems_are_local()
    {
        var doc = ConfigDocument.Parse(Loco);
        var refs = DependencyInventory.Collect(doc, Self);
        var report = new ValidationReport(Self, ["VE99: something about <kuid:999008:200>"], []);
        var files = new[] { new InventoryFinding(InventoryKind.MissingTarget, "a.texture.txt", "Primary=a.tga menunjuk berkas yang tidak ada.") };
        var problems = Diagnosis.Diagnose(Self, false, doc, refs, _ => TargetState.Installed, report, files);
        var v = problems.Single(p => p.Class == ProblemClass.NotUnderstood);
        Assert.Equal("VE99: something about <kuid:999008:200>", v.Cause);
        Assert.Equal("track-sound/1/sound", v.Location);
        Assert.Contains(problems, p => p.Class == ProblemClass.Inconsistent && p.Actions.Single().Kind == RepairActionKind.OpenTextureWorkshop);
    }

    [Fact]
    public void Graph_limits_depth_detects_cycles_and_builds_chain()
    {
        Kuid a = K("<kuid:1:1>"), b = K("<kuid:1:2>"), c = K("<kuid:1:3>"), d = K("<kuid:1:4>"), e = K("<kuid:1:5>");
        var deps = new Dictionary<Kuid, Kuid[]> { [a] = [b, c], [b] = [d], [d] = [e, a], [c] = [] };
        var g = DependencyGraph.Build(a, k => deps.TryGetValue(new Kuid(k.UserId, k.ContentId, null), out var v) ? v : null, maxDepth: 3);

        Assert.Equal([b, d, e, c], g.Nodes.Where(n => !n.IsCycle).Select(n => n.Kuid));
        Assert.Contains(g.Nodes, n => n.Kuid == e && n.Depth == 3 && !n.Expanded);
        Assert.Contains(g.Nodes, n => n.Kuid == a && n.IsCycle);
        Assert.Equal([a, b, d, e], g.Chain(e));
        Assert.True(g.Nodes.Single(n => n.Kuid == b).Expanded);

        var shallow = DependencyGraph.Build(a, k => deps.TryGetValue(k, out var v) ? v : null, maxDepth: 1);
        Assert.Equal([b, c], shallow.Nodes.Select(n => n.Kuid));
    }
}

public sealed class P1RoleTests
{
    private const string Emu = """
        // Config sintetis RailNexus untuk peran P1; bukan config pembuat mana pun.
        kuid                                    <kuid:999001:2001>
        kind                                    "traincar"
        interior                                <kuid:999010:1>
        pantograph                              <kuid:999011:1>
        enginespec                              <kuid:999012:1>
        kuid-table
        {
          0                                     <kuid:999010:1>
          1                                     <kuid:999011:1>
          2                                     <kuid:999012:1>
        }

        """;

    [Theory]
    [InlineData("interior", "<kuid:999010:1>", "interior")]
    [InlineData("pantograph", "<kuid:999011:1>", "pantograph")]
    [InlineData("enginespec", "<kuid:999012:1>", "enginespec")]
    public void P1_roles_use_the_same_engine_and_require_kind(string roleId, string current, string kind)
    {
        var doc = ConfigDocument.Parse(Emu);
        var role = RoleCatalog.ById(roleId)!;
        var slot = Assert.Single(role.Locate(doc));
        Assert.Equal(Kuid.Parse(current), slot.Current);
        Assert.Equal(SupportLevel.Guided, slot.Support);

        var replacement = new Kuid(999099, 5, null);
        Assert.Equal(CandidateVerdict.Suitable, ModificationEngine.Evaluate(role, slot, new AssetRecord(replacement, "x", kind, AssetState.Installed)).Verdict);
        Assert.Equal(CandidateVerdict.Rejected, ModificationEngine.Evaluate(role, slot, new AssetRecord(replacement, "x", "scenery", AssetState.Installed)).Verdict);

        var plan = ModificationEngine.PlanReplace(doc, role, slot.Path, replacement);
        Assert.True(plan.Ok, plan.Error);
        var after = ConfigDocument.Parse(ConfigEditor.Apply(Emu, plan.Edits));
        Assert.Equal(replacement, after.Find(roleId)[0].Value!.Kuid);
        // Peran lain tidak tersentuh.
        foreach (var other in RoleCatalog.P1.Where(r => r.Id != roleId))
            Assert.Equal(other.Locate(doc).Single().Current, other.Locate(after).Single().Current);
    }

    [Fact]
    public void Inventory_labels_p1_roles()
    {
        var refs = DependencyInventory.Collect(ConfigDocument.Parse(Emu), Kuid.Parse("<kuid:999001:2001>"));
        Assert.Equal("Pantograf · Pantograf", refs.Single(r => r.Location == "pantograph").RoleLabel);
    }
}
