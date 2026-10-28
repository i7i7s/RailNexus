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
