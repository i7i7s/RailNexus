using RailNexus.Core.Config;

namespace RailNexus.Tests;

public sealed class LineDiffTests
{
    [Fact]
    public void Identical_text_has_no_changes()
    {
        var d = LineDiff.Compare("a\r\nb\r\nc\r\n", "a\nb\nc\n");
        Assert.All(d, l => Assert.Equal(DiffKind.Same, l.Kind));
        Assert.Empty(LineDiff.WithContext(d));
    }

    [Fact]
    public void Changed_line_is_removed_then_added_with_line_numbers()
    {
        var d = LineDiff.Compare("a\nb\nc\nd\n", "a\nB\nc\nd\ne\n");
        Assert.Equal(
        [
            (DiffKind.Same, 1, 1, "a"),
            (DiffKind.Removed, 2, (int?)null, "b"),
            (DiffKind.Added, (int?)null, 2, "B"),
            (DiffKind.Same, 3, 3, "c"),
            (DiffKind.Same, 4, 4, "d"),
            (DiffKind.Added, (int?)null, 5, "e"),
        ], d.Select(l => (l.Kind, l.OldLine, l.NewLine, l.Text)));
    }

    [Fact]
    public void Insertion_in_middle_keeps_alignment()
    {
        var d = LineDiff.Compare("x\ny\nz", "x\nnew1\nnew2\ny\nz");
        Assert.Equal(2, d.Count(l => l.Kind == DiffKind.Added));
        Assert.DoesNotContain(d, l => l.Kind == DiffKind.Removed);
        Assert.Equal(4, d.Single(l => l.Text == "y").NewLine);
    }

    [Fact]
    public void Context_keeps_only_nearby_lines()
    {
        var old = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"l{i}"));
        var neu = old.Replace("l25", "L25");
        var ctx = LineDiff.WithContext(LineDiff.Compare(old, neu), 2);
        Assert.Equal(["l23", "l24", "l25", "L25", "l26", "l27"], ctx.Select(l => l.Text));
    }
}

public sealed class DraftTextTests
{
    [Fact]
    public void Replace_text_applies_first_and_rejects_changed_base()
    {
        const string original = "kuid <kuid:1:2>\r\ndescription \"a\"\r\n";
        const string edited = "kuid <kuid:1:2>\r\ndescription \"b\"\r\n// catatan\r\n";
        var draft = new RailNexus.Core.Roles.Draft(new RailNexus.Core.Kuid(1, 2, null));
        draft.Put(new RailNexus.Core.Roles.DraftItem.SetText("description", "c", "m", "deskripsi"));
        draft.Put(new RailNexus.Core.Roles.DraftItem.ReplaceConfigText(edited, RailNexus.Core.Roles.Draft.TextHash(original), "m", "teks"));
        Assert.IsType<RailNexus.Core.Roles.DraftItem.ReplaceConfigText>(draft.Items[0]);
        var build = draft.Build(original);
        Assert.True(build.Ok, build.Error);
        Assert.Equal("kuid <kuid:1:2>\r\ndescription \"c\"\r\n// catatan\r\n", build.NewText);

        var stale = draft.Build("kuid <kuid:1:2>\r\ndescription \"x\"\r\n");
        Assert.False(stale.Ok);
        Assert.Contains("config berubah sejak teks disunting", stale.Error);

        var bad = new RailNexus.Core.Roles.Draft(new RailNexus.Core.Kuid(1, 2, null));
        bad.Put(new RailNexus.Core.Roles.DraftItem.ReplaceConfigText("a\r\n{\r\n", RailNexus.Core.Roles.Draft.TextHash(original), "m", "teks"));
        Assert.Contains("error struktur", bad.Build(original).Error);
    }
}
