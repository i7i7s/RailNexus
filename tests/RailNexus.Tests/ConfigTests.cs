using System.Text;
using RailNexus.Core;
using RailNexus.Core.Config;
using RailNexus.Core.Roles;

namespace RailNexus.Tests;

public class ConfigTests
{
    private const string Small =
        "kuid <kuid:999001:1201>\r\n" +
        "description \"lama\"\r\n" +
        "// komentar { tidak dihitung\r\n" +
        "bogeys\r\n{\r\n  0\r\n  {\r\n    description \"bersarang\"\r\n  }\r\n}\r\n" +
        "tag-tak-dikenal \"tetap\"\r\n";

    internal static DraftBuild SetText(string text, string path, string value)
    {
        var d = new Draft(new Kuid(1, 1, null));
        d.Put(new DraftItem.SetText(path, value, "uji", "ubah"));
        return d.Build(text);
    }

    [Fact]
    public void Parse_without_edit_is_byte_identical_EDIT_AC()
    {
        var doc = ConfigDocument.Parse(Small);
        Assert.False(doc.HasErrors);
        Assert.Equal(Small, ConfigEditor.Apply(doc.Text, []));
        Assert.Equal("\r\n", doc.NewLine);
    }

    [Fact]
    public void Changes_only_target_and_keeps_comments_crlf_unknown_tags_AC05()
    {
        var r = SetText(Small, "description", "baru");
        Assert.True(r.Ok, r.Error);
        Assert.Equal(Small.Replace("description \"lama\"", "description \"baru\""), r.NewText);
    }

    [Fact]
    public void Same_value_produces_identical_text()
    {
        Assert.Equal(Small, SetText(Small, "description", "lama").NewText);
    }

    [Fact]
    public void Nested_tag_with_same_name_is_separate_path()
    {
        var doc = ConfigDocument.Parse(Small);
        Assert.Single(doc.Find("description"));
        Assert.Single(doc.Find("bogeys/0/description"));
    }

    [Fact]
    public void Duplicate_top_level_tag_is_not_guessed_AC06()
    {
        var r = SetText("description \"a\"\ndescription \"b\"\n", "description", "x");
        Assert.False(r.Ok);
        Assert.Contains(ConfigDocument.Parse("description \"a\"\ndescription \"b\"\n").Diagnostics, d => d.Code == "CFG-DUPLICATE");
    }

    [Fact]
    public void Unclosed_container_is_error_and_not_patched_AC06()
    {
        var doc = ConfigDocument.Parse("description \"a\"\nbogeys\n{\n  0\n  {\n");
        Assert.True(doc.HasErrors);
        Assert.False(SetText(doc.Text, "description", "x").Ok);
    }

    [Fact]
    public void Unterminated_string_until_end_of_file_is_error()
    {
        Assert.True(ConfigDocument.Parse("username \"tanpa tutup\nkind x\n").HasErrors);
    }

    [Fact]
    public void Multi_line_string_is_one_value()
    {
        const string text = "description \"Baris satu.\r\n\r\nBaris tiga, dengan \\\"kutip\\\".\"\r\nkind \"traincar\"\r\nbogeys\r\n{\r\n}\r\n";
        var doc = ConfigDocument.Parse(text);
        Assert.Empty(doc.Diagnostics);
        Assert.Equal("Baris satu.\r\n\r\nBaris tiga, dengan \"kutip\".", doc.TopLevelString("description"));
        Assert.Equal("traincar", doc.TopLevelString("kind"));
        Assert.Equal(5, doc.Find("bogeys")[0].Line);
        Assert.Equal(text, ConfigEditor.Apply(text, []));
    }

    [Fact]
    public void Replacing_multi_line_string_keeps_following_lines()
    {
        const string text = "description \"a\nb\"\nkind \"x\"\n";
        var r = SetText(text, "description", "baru");
        Assert.Equal("description \"baru\"\nkind \"x\"\n", r.NewText);
    }

    [Fact]
    public void Quotes_are_escaped_and_unescaped()
    {
        var r = SetText("description \"a\"\n", "description", "kata \"kutip\"");
        Assert.Equal("description \"kata \\\"kutip\\\"\"\n", r.NewText);
        Assert.Equal("kata \"kutip\"", ConfigDocument.Parse(r.NewText).TopLevelString("description"));
    }

    [Fact]
    public void Container_brace_on_same_line_is_supported()
    {
        var doc = ConfigDocument.Parse("kuid-table {\n  0 <kuid:1:2>\n}\n");
        Assert.False(doc.HasErrors);
        Assert.Equal(new Kuid(1, 2, null), doc.Find("kuid-table/0")[0].Value!.Kuid);
    }

    [Fact]
    public void Lists_of_numbers_and_kuids_are_values()
    {
        var doc = ConfigDocument.Parse("pos 1.0,2,3\nlist <kuid:1:2>,<kuid:1:3>\n");
        Assert.False(doc.HasErrors);
        Assert.Equal(ValueKind.List, doc.Find("pos")[0].Value!.Kind);
        Assert.Equal(2, doc.Find("list")[0].Value!.ListKuids.Count);
    }

    [Fact]
    public void Append_into_empty_container_uses_document_newline()
    {
        var doc = ConfigDocument.Parse("kuid-table\n{\n}\n");
        var c = doc.Find("kuid-table")[0].Container!;
        var e = ConfigEditor.AppendTag(doc, c, "0", "<kuid:1:2>", "uji");
        var text = ConfigEditor.Apply(doc.Text, [e.Edit!]);
        Assert.Equal("kuid-table\n{\n  0 <kuid:1:2>\n}\n", text);
    }

    [Fact]
    public void Overlapping_edits_are_rejected()
    {
        var edits = new[] { new TextEdit(new Span(0, 4), "a", "x"), new TextEdit(new Span(2, 4), "b", "y") };
        Assert.Throws<InvalidOperationException>(() => ConfigEditor.Apply("0123456789", edits));
    }
}

public class RoleTests
{
    private static readonly string Loco =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "configs", "loco-dx200.txt"));

    private static readonly Kuid Horn2 = Kuid.Parse("<kuid:999002:3101>");
    private static readonly Kuid Horn3 = Kuid.Parse("<kuid:999002:3103>");

    private static string Replace(string text, string roleId, string path, Kuid replacement, out SlotChangePlan plan)
    {
        var doc = ConfigDocument.Parse(text);
        plan = ModificationEngine.PlanReplace(doc, RoleCatalog.ById(roleId)!, path, replacement);
        Assert.True(plan.Ok, plan.Error);
        return ConfigEditor.Apply(text, plan.Edits);
    }

    /// <summary>Baris yang berbeda antara dua teks — memastikan tidak ada perubahan di luar cakupan.</summary>
    private static List<(string Before, string After)> ChangedLines(string a, string b)
    {
        var x = a.Split('\n');
        var y = b.Split('\n');
        Assert.True(y.Length >= x.Length);
        var diff = new List<(string, string)>();
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (x[i] == y[j]) { i++; j++; continue; }
            if (j + 1 < y.Length && x[i] == y[j + 1]) { diff.Add(("", y[j].Trim())); j++; continue; }
            diff.Add((x[i].Trim(), y[j].Trim())); i++; j++;
        }
        while (j < y.Length) diff.Add(("", y[j++].Trim()));
        return diff;
    }

    [Fact]
    public void Fixture_parses_cleanly()
    {
        var doc = ConfigDocument.Parse(Loco);
        Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void Locates_p0_slots()
    {
        var doc = ConfigDocument.Parse(Loco);
        Assert.Equal(Horn2, Assert.Single(RoleCatalog.ById("hornsound")!.Locate(doc)).Current);
        Assert.Single(RoleCatalog.ById("enginesound")!.Locate(doc));

        var bogies = RoleCatalog.ById("bogie")!.Locate(doc).ToList();
        Assert.Equal(["bogeys/0/bogey", "bogeys/1/bogey"], bogies.Select(b => b.Path));
        Assert.Contains(new KeyValuePair<string, string>("reversed", "1"), bogies[1].Context);

        var ts = RoleCatalog.ById("tracksound")!.Locate(doc).ToList();
        Assert.Equal(["track-sound/0/track", "track-sound/0/sound", "track-sound/1/sound"], ts.Select(s => s.Path));
        Assert.Equal("Aturan 0 · sound", ts[1].Label);
        Assert.Contains(new KeyValuePair<string, string>("priority", "10"), ts[1].Context);
    }

    [Fact]
    public void Hornsound_changes_only_horn_field_and_its_table_entry_AC01()
    {
        var after = Replace(Loco, "hornsound", "hornsound", Horn3, out var plan);
        var diff = ChangedLines(Loco, after);
        Assert.Equal(2, diff.Count);
        Assert.Contains(diff, d => d.After.StartsWith("hornsound") && d.After.EndsWith("<kuid:999002:3103>"));
        Assert.Contains(diff, d => d.After.StartsWith("horn ") && d.After.EndsWith("<kuid:999002:3103>"));
        Assert.Empty(plan.Uncertain);
        // Kolom nilai tetap rata dengan aslinya.
        Assert.Contains("hornsound                               <kuid:999002:3103>", after);
    }

    [Fact]
    public void Script_asset_keeps_symbolic_entry_and_reports_uncertainty_AC02_AC28()
    {
        var scripted = Loco.Replace("engine                                  1\n",
            "engine                                  1\nscript                                  \"dx200.gs\"\nclass                                   \"DX200\"\n");
        var after = Replace(scripted, "hornsound", "hornsound", Horn3, out var plan);
        var table = ConfigDocument.Parse(after).Find("kuid-table")[0].Container!;
        Assert.Equal(Horn2, table.Named("horn").Single().Value!.Kuid);
        Assert.Contains(table.Children, c => c.Name == "5" && c.Value!.Kuid == Horn3);
        Assert.NotEmpty(plan.Uncertain);
    }

    [Fact]
    public void One_bogie_slot_changes_and_other_slot_and_orientation_stay_AC26()
    {
        var k7 = Kuid.Parse("<kuid:999005:6007>");
        var after = Replace(Loco, "bogie", "bogeys/0/bogey", k7, out _);
        var doc = ConfigDocument.Parse(after);
        Assert.Equal(k7, doc.Find("bogeys/0/bogey")[0].Value!.Kuid);
        Assert.Equal(Kuid.Parse("<kuid:999005:6005>"), doc.Find("bogeys/1/bogey")[0].Value!.Kuid);
        Assert.Equal("0", doc.Find("bogeys/0/reversed")[0].Value!.Text);
        Assert.Equal("1", doc.Find("bogeys/1/reversed")[0].Value!.Text);
        // K-5 masih dipakai slot 1 → entri lama tetap, entri baru ditambahkan.
        var table = doc.Find("kuid-table")[0].Container!;
        Assert.Contains(table.Children, c => c.Name == "1" && c.Value!.Kuid == Kuid.Parse("<kuid:999005:6005>"));
        Assert.Contains(table.Children, c => c.Name == "5" && c.Value!.Kuid == k7);
        Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void Tracksound_rule_keeps_filter_and_priority_AC03()
    {
        var newSound = Kuid.Parse("<kuid:999008:203>");
        var after = Replace(Loco, "tracksound", "track-sound/0/sound", newSound, out _);
        var doc = ConfigDocument.Parse(after);
        Assert.Equal(newSound, doc.Find("track-sound/0/sound")[0].Value!.Kuid);
        Assert.Equal("10", doc.Find("track-sound/0/priority")[0].Value!.Text);
        Assert.Equal(Kuid.Parse("<kuid:999007:100>"), doc.Find("track-sound/0/track")[0].Value!.Kuid);
        Assert.Equal(Kuid.Parse("<kuid:999008:200>"), doc.Find("track-sound/1/sound")[0].Value!.Kuid);
        Assert.Equal(2, ChangedLines(Loco, after).Count);
    }

    [Fact]
    public void Duplicate_hornsound_offers_no_slot_AC06()
    {
        var dup = Loco.Replace("enginesound", "hornsound");
        Assert.Empty(RoleCatalog.ById("hornsound")!.Locate(ConfigDocument.Parse(dup)));
    }

    [Fact]
    public void Draft_keeps_changes_from_different_modules_AC33()
    {
        var d = new Draft(new Kuid(999001, 1201, 2));
        d.Put(new DraftItem.ReplaceKuid("hornsound", "hornsound", Horn2, Horn3, "Workshop Suara", "Ganti klakson"));
        d.Put(new DraftItem.SetText("description", "Deskripsi baru", "Editor aset", "Ubah deskripsi"));
        var b = d.Build(Loco);
        Assert.True(b.Ok, b.Error);
        var doc = ConfigDocument.Parse(b.NewText);
        Assert.Equal(Horn3, doc.Find("hornsound")[0].Value!.Kuid);
        Assert.Equal("Deskripsi baru", doc.TopLevelString("description"));
    }

    [Fact]
    public void Draft_put_on_same_slot_replaces_previous_choice()
    {
        var d = new Draft(new Kuid(999001, 1201, 2));
        d.Put(new DraftItem.ReplaceKuid("hornsound", "hornsound", Horn2, Horn3, "m", "a"));
        d.Put(new DraftItem.ReplaceKuid("hornsound", "hornsound", Horn2, Kuid.Parse("<kuid:999002:3104>"), "m", "b"));
        Assert.Single(d.Items);
    }

    [Fact]
    public void Stale_draft_is_rejected_when_config_changed()
    {
        var d = new Draft(new Kuid(999001, 1201, 2));
        d.Put(new DraftItem.ReplaceKuid("hornsound", "hornsound", Horn2, Horn3, "m", "Ganti klakson"));
        var changedOutside = Loco.Replace("hornsound                               <kuid:999002:3101>", "hornsound                               <kuid:999002:3199>");
        Assert.False(d.Build(changedOutside).Ok);
    }

    [Fact]
    public void Edit_plan_preserves_bom_and_binds_base_hash()
    {
        var d = new Draft(new Kuid(999001, 1201, 2));
        d.Put(new DraftItem.SetText("description", "x", "m", "ubah"));
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Loco)).ToArray();
        var (plan, build) = d.ToEditPlan(bytes, "uji");
        Assert.True(build.Ok, build.Error);
        var output = plan!.Changes[0].NewBytes!;
        Assert.Equal([0xEF, 0xBB, 0xBF], output[..3]);
        Assert.NotNull(plan.ExpectedBaseHashes!["config.txt"]);
    }

    [Fact]
    public void Non_utf8_config_is_not_rewritten()
    {
        var d = new Draft(new Kuid(1, 1, null));
        d.Put(new DraftItem.SetText("description", "x", "m", "ubah"));
        var ansi = Encoding.ASCII.GetBytes("description \"caf").Concat(new byte[] { 0xE9 }).Concat(Encoding.ASCII.GetBytes("\"\nusername \"a\"\n")).ToArray();
        var (plan, build) = d.ToEditPlan(ansi, "uji");
        Assert.Null(plan);
        Assert.False(build.Ok);
    }

    [Theory]
    [InlineData("bogey", AssetState.Installed, CandidateVerdict.Suitable)]
    [InlineData("scenery", AssetState.Installed, CandidateVerdict.Rejected)]
    [InlineData("bogey", AssetState.Faulty, CandidateVerdict.Rejected)]
    [InlineData("bogey", AssetState.Missing, CandidateVerdict.Rejected)]
    public void Bogie_candidates_AC04(string kind, AssetState state, CandidateVerdict expected)
    {
        var role = RoleCatalog.ById("bogie")!;
        var slot = role.Locate(ConfigDocument.Parse(Loco)).First();
        var c = ModificationEngine.Evaluate(role, slot, new AssetRecord(Kuid.Parse("<kuid:999005:6007>"), "K-7", kind, state));
        Assert.Equal(expected, c.Verdict);
    }

    [Fact]
    public void Horn_kind_is_shown_as_unverified_not_assumed()
    {
        var role = RoleCatalog.ById("hornsound")!;
        var slot = role.Locate(ConfigDocument.Parse(Loco)).First();
        var c = ModificationEngine.Evaluate(role, slot, new AssetRecord(Horn3, "KL-3", "sound", AssetState.Installed));
        Assert.Equal(CandidateVerdict.SuitableKindUnverified, c.Verdict);
    }
}
