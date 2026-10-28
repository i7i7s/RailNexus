using System.Buffers.Binary;
using System.Text;
using RailNexus.Core;
using RailNexus.Core.Roles;
using RailNexus.Core.Texture;
using RailNexus.Core.Tx;
using static RailNexus.Tests.ImageFixtures;

namespace RailNexus.Tests;

public sealed class ImageCodecTests
{
    private static void AssertPattern(RasterImage img, Func<int, int, (byte, byte, byte, byte)>? expected = null, bool alpha = true)
    {
        expected ??= Pattern;
        Assert.Equal(W, img.Width);
        Assert.Equal(H, img.Height);
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var (r, g, b, a) = expected(x, y);
            Assert.Equal((r, g, b, alpha ? a : (byte)255), img.Pixel(x, y));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Png_rgba_all_filters_decode_exact_pixels(int filter)
    {
        var r = ImageCodec.Decode(PatternPngRgba(filter: filter));
        Assert.True(r.Ok, r.Error);
        Assert.True(r.Image!.HasAlpha);
        AssertPattern(r.Image);
    }

    [Fact]
    public void Png_rgb_has_no_alpha()
    {
        var r = ImageCodec.Decode(PatternPngRgb(filter: 4));
        Assert.True(r.Ok, r.Error);
        Assert.False(r.Image!.HasAlpha);
        AssertPattern(r.Image, alpha: false);
    }

    [Fact]
    public void Png_interlaced_adam7_decodes_same_as_progressive()
    {
        var samples = (Func<int, int, int[]>)((x, y) => [(x * 13 + y * 7) & 0xFF, (x * 3) & 0xFF, (y * 11) & 0xFF, 255 - x]);
        var plain = ImageCodec.Decode(Png(11, 9, 6, samples)).Image!;
        var inter = ImageCodec.Decode(Png(11, 9, 6, samples, interlace: true, filter: 1));
        Assert.True(inter.Ok, inter.Error);
        Assert.Equal(plain.Rgba, inter.Image!.Rgba);
    }

    [Fact]
    public void Png_palette_with_trns_and_low_bit_depth()
    {
        byte[] plte = [255, 0, 0, 0, 255, 0, 0, 0, 255, 9, 8, 7];
        var r = ImageCodec.Decode(Png(5, 2, 3, (x, y) => [(x + y) % 4], bitDepth: 2, plte: plte, trns: [255, 128]));
        Assert.True(r.Ok, r.Error);
        Assert.True(r.Image!.HasAlpha);
        Assert.Equal((255, 0, 0, 255), r.Image.Pixel(0, 0));
        Assert.Equal((0, 255, 0, 128), r.Image.Pixel(1, 0));
        Assert.Equal((9, 8, 7, 255), r.Image.Pixel(3, 0)); // indeks di luar tRNS = opak
        Assert.Equal((0, 0, 255, 255), r.Image.Pixel(1, 1));
    }

    [Fact]
    public void Png_16bit_keeps_high_byte_and_notes_it()
    {
        var r = ImageCodec.Decode(Png(2, 1, 2, (x, _) => [0x1234 + x, 0xABCD, 0x00FF], bitDepth: 16));
        Assert.True(r.Ok, r.Error);
        Assert.Equal((0x12, 0xAB, 0x00, 255), r.Image!.Pixel(0, 0));
        Assert.Contains(r.Notes, n => n.Contains("16-bit"));
    }

    [Fact]
    public void Png_gray_alpha_and_gray_color_key()
    {
        var ga = ImageCodec.Decode(Png(2, 1, 4, (x, _) => [100 + x, 50]));
        Assert.Equal((100, 100, 100, 50), ga.Image!.Pixel(0, 0));
        var key = ImageCodec.Decode(Png(2, 1, 0, (x, _) => [x == 0 ? 7 : 8], trns: [0, 7]));
        Assert.True(key.Image!.HasAlpha);
        Assert.Equal((7, 7, 7, 0), key.Image.Pixel(0, 0));
        Assert.Equal((8, 8, 8, 255), key.Image.Pixel(1, 0));
    }

    [Fact]
    public void Png_gama_chunk_does_not_change_values()
    {
        var r = ImageCodec.Decode(PatternPngRgba(gama: true));
        AssertPattern(r.Image!);
    }

    [Fact]
    public void Png_with_bad_crc_is_rejected()
    {
        var png = PatternPngRgba();
        png[^20] ^= 0xFF; // di dalam chunk IDAT kedua
        var r = ImageCodec.Decode(png);
        Assert.False(r.Ok);
        Assert.Contains("CRC", r.Error);
    }

    [Fact]
    public void Truncated_png_is_rejected_without_exception()
    {
        var png = PatternPngRgba();
        Assert.False(ImageCodec.Decode(png[..(png.Length - 30)]).Ok);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Tga_both_origins_and_rle_decode_to_same_orientation(bool topLeft, bool rle)
    {
        var r = ImageCodec.Decode(PatternTga(topLeft, rle));
        Assert.True(r.Ok, r.Error);
        AssertPattern(r.Image!, rle ? RlePattern : Pattern);
    }

    [Fact]
    public void Tga_24bit_has_opaque_alpha()
    {
        var r = ImageCodec.Decode(PatternTga(topLeft: false, alpha: false));
        Assert.True(r.Ok, r.Error);
        Assert.False(r.Image!.HasAlpha);
        AssertPattern(r.Image, alpha: false);
    }

    [Fact]
    public void Format_is_detected_from_content_not_extension()
    {
        Assert.Equal(ImageFormat.Png, ImageCodec.Detect(PatternPngRgba()));
        Assert.Equal(ImageFormat.Tga, ImageCodec.Detect(PatternTga(false)));
        Assert.Equal(ImageFormat.Jpeg, ImageCodec.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 }));
        Assert.Equal(ImageFormat.Unknown, ImageCodec.Detect(Encoding.ASCII.GetBytes("Primary=badan.tga\r\n")));

        var jpg = ImageCodec.Decode([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0]);
        Assert.False(jpg.Ok);
        Assert.Contains("belum dapat dibaca", jpg.Error);
    }

    [Fact]
    public void Tga_encoder_writes_bottom_left_bgra_layout()
    {
        var img = ImageCodec.Decode(PatternPngRgba()).Image!;
        var tga = TgaCodec.Encode(img, withAlpha: true);
        Assert.Equal(2, tga[2]);
        Assert.Equal(32, tga[16]);
        Assert.Equal(0x08, tga[17]); // 8 bit alpha, asal kiri bawah
        Assert.Equal(W, BinaryPrimitives.ReadUInt16LittleEndian(tga.AsSpan(12)));
        // Piksel data pertama = kiri bawah (0, H-1) dalam urutan B, G, R, A.
        var p = Pattern(0, H - 1);
        Assert.Equal(new[] { p.B, p.G, p.R, p.A }, tga[18..22]);
        // Piksel data terakhir = kanan atas.
        var q = Pattern(W - 1, 0);
        Assert.Equal(new[] { q.B, q.G, q.R, q.A }, tga[^4..]);

        var tga24 = TgaCodec.Encode(img, withAlpha: false);
        Assert.Equal(24, tga24[16]);
        Assert.Equal(18 + W * H * 3, tga24.Length);
    }
}

public sealed class TextureMetadataTests
{
    [Fact]
    public void Update_keeps_unknown_keys_order_and_line_endings()
    {
        var old = Encoding.UTF8.GetBytes("Primary=badan.bmp\r\nMipmapBias=-1\r\nTile=s\r\n");
        var (m, err) = TextureMetadata.Parse(old);
        Assert.NotNull(m);
        m!.Set("Primary", "badan.tga");
        m.Set("Tile", m.Get("Tile") ?? "st");
        Assert.Equal("Primary=badan.tga\r\nMipmapBias=-1\r\nTile=s\r\n", Encoding.UTF8.GetString(m.ToBytes()));
        Assert.Equal("", err);
    }

    [Fact]
    public void Appending_to_file_without_final_newline()
    {
        var (m, _) = TextureMetadata.Parse(Encoding.UTF8.GetBytes("Primary=a.tga\nCustom=1"));
        m!.Set("Tile", "st");
        Assert.Equal("Primary=a.tga\nCustom=1\nTile=st\n", Encoding.UTF8.GetString(m.ToBytes()));
    }

    [Fact]
    public void Duplicate_keys_are_ambiguous()
    {
        var (m, err) = TextureMetadata.Parse(Encoding.UTF8.GetBytes("Primary=a.tga\r\nprimary=b.tga\r\n"));
        Assert.Null(m);
        Assert.Contains("lebih dari sekali", err);
    }

    [Fact]
    public void New_metadata_uses_crlf()
    {
        var m = TextureMetadata.CreateNew();
        m.Set("Primary", "a.tga");
        m.Set("Tile", "st");
        Assert.Equal("Primary=a.tga\r\nTile=st\r\n", Encoding.UTF8.GetString(m.ToBytes()));
    }
}

public sealed class TextureWorkshopTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rn tex ä ").FullName;
    private readonly string _src;
    private readonly TextureWorkshop _ws;

    public TextureWorkshopTests()
    {
        _src = Path.Combine(_root, "sumber gambar");
        Directory.CreateDirectory(_src);
        _ws = new TextureWorkshop(Path.Combine(_root, "staging"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Source(string name, byte[] bytes, string? sub = null)
    {
        var dir = sub is null ? _src : Path.Combine(_src, sub);
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static RasterImage ReadBack(TexturePrepareResult r, string rel)
    {
        var f = r.Files.Single(x => x.RelativePath == rel);
        var d = ImageCodec.Decode(f.Bytes);
        Assert.True(d.Ok, d.Error);
        return d.Image!;
    }

    [Fact]
    public void Png_with_alpha_becomes_32bit_tga_with_exact_channels_and_orientation_AC16()
    {
        var src = Source("badan.png", PatternPngRgba());
        var before = File.ReadAllBytes(src);

        var r = _ws.Prepare([new TextureSource(src, TextureRole.Color)], DictionaryFileView.Empty);

        Assert.True(r.Ok, r.Message);
        var item = Assert.Single(r.Items);
        Assert.Equal(TextureItemStatus.Ready, item.Status);
        Assert.Equal(32, item.OutputBits);
        var back = ReadBack(r, "badan.tga");
        Assert.True(back.HasAlpha);
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            Assert.Equal(Pattern(x, y), back.Pixel(x, y));

        // Sumber tidak berubah; salinan ada di staging.
        Assert.Equal(before, File.ReadAllBytes(src));
        Assert.Single(Directory.GetFiles(Path.Combine(r.StagingFolder!, "src")));
        Assert.True(File.Exists(Path.Combine(r.StagingFolder!, "out", "badan.tga")));

        var meta = Encoding.UTF8.GetString(r.Files.Single(f => f.RelativePath == "badan.texture.txt").Bytes);
        Assert.Equal("Primary=badan.tga\r\nTile=st\r\n", meta);
    }

    [Fact]
    public void Rgb_source_becomes_24bit()
    {
        var r = _ws.Prepare([new TextureSource(Source("atap.png", PatternPngRgb()), TextureRole.Color)], DictionaryFileView.Empty);
        Assert.Equal(24, r.Items[0].OutputBits);
        Assert.False(ReadBack(r, "atap.tga").HasAlpha);
        Assert.Equal(24, r.Files.Single(f => f.RelativePath == "atap.tga").Bytes[16]);
    }

    [Fact]
    public void Color_image_with_fully_opaque_alpha_becomes_24bit_with_note()
    {
        var png = Png(W, H, 6, (x, y) => { var p = Pattern(x, y); return [p.R, p.G, p.B, 255]; });
        var r = _ws.Prepare([new TextureSource(Source("opak.png", png), TextureRole.Color)], DictionaryFileView.Empty);
        Assert.Equal(24, r.Items[0].OutputBits);
        Assert.Contains(r.Items[0].Notes, n => n.Contains("seluruhnya 255"));
    }

    [Fact]
    public void Parameter_map_alpha_is_kept_even_when_opaque_AC17()
    {
        // Alpha bervariasi = data metallic; alpha penuh pun tetap data, bukan transparansi.
        var varying = _ws.Prepare([new TextureSource(Source("param.png", PatternPngRgba()), TextureRole.Parameter)], DictionaryFileView.Empty);
        Assert.Equal(32, varying.Items[0].OutputBits);
        var back = ReadBack(varying, "param.tga");
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            Assert.Equal(Pattern(x, y), back.Pixel(x, y));

        var opaque = Png(W, H, 6, (x, y) => [x * 40, y * 40, 7, 255]);
        var r = _ws.Prepare([new TextureSource(Source("param2.png", opaque), TextureRole.Parameter)], DictionaryFileView.Empty);
        Assert.Equal(32, r.Items[0].OutputBits);
        Assert.Contains(r.Items[0].Notes, n => n.Contains("dipertahankan"));
    }

    [Fact]
    public void Parameter_alpha_is_only_dropped_by_explicit_choice()
    {
        var r = _ws.Prepare([new TextureSource(Source("param.png", PatternPngRgba()), TextureRole.Parameter, AlphaChoice.Discard)], DictionaryFileView.Empty);
        Assert.Equal(24, r.Items[0].OutputBits);
        Assert.Contains(r.Items[0].Notes, n => n.Contains("atas pilihan pengguna"));
    }

    [Fact]
    public void Normal_map_values_are_not_color_corrected()
    {
        // Nilai vektor normal tipikal + chunk gAMA: harus tersalin apa adanya.
        var png = Png(W, H, 2, (x, y) => [128 + x, 128 - y, 255 - x * y], gama: true);
        var r = _ws.Prepare([new TextureSource(Source("normal.png", png), TextureRole.Normal)], DictionaryFileView.Empty);
        var back = ReadBack(r, "normal.tga");
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            Assert.Equal(((byte)(128 + x), (byte)(128 - y), (byte)(255 - x * y), (byte)255), back.Pixel(x, y));
    }

    [Fact]
    public void Extension_is_not_trusted()
    {
        var r = _ws.Prepare([new TextureSource(Source("ternyata-png.tga", PatternPngRgba()), TextureRole.Color)], DictionaryFileView.Empty);
        Assert.Equal(TextureItemStatus.Ready, r.Items[0].Status);
        Assert.StartsWith("PNG", r.Items[0].SourceDescription);

        var fake = _ws.Prepare([new TextureSource(Source("bukan.png", Encoding.ASCII.GetBytes("bukan gambar")), TextureRole.Color)], DictionaryFileView.Empty);
        Assert.Equal(TextureItemStatus.Failed, fake.Items[0].Status);
        Assert.Contains("tidak dikenali", fake.Items[0].Error);
    }

    [Fact]
    public void Unknown_texture_format_is_not_faked_and_source_is_untouched_AC19()
    {
        // Isi tidak dikenal (mis. .texture terkompilasi): tidak ditebak dari ekstensi atau tanda tangan.
        var bytes = new byte[] { 0x13, 0x37, 0x00, 0x42, 1, 0, 0, 0, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9, 9 }; // isi sembarang
        var src = Source("badan.texture", bytes);
        var r = _ws.Prepare([new TextureSource(src, TextureRole.Color)], DictionaryFileView.Empty);
        Assert.Equal(TextureItemStatus.Failed, r.Items[0].Status);
        Assert.Contains("tidak dikenali", r.Items[0].Error);
        Assert.Empty(r.Files);
        Assert.Equal(bytes, File.ReadAllBytes(src));
    }

    [Fact]
    public void Two_sources_with_same_output_name_block_until_resolved_AC18()
    {
        var a = Source("badan.png", PatternPngRgba(), "a");
        var b = Source("badan.png", PatternPngRgb(), "b");

        var r = _ws.Prepare([new TextureSource(a, TextureRole.Color), new TextureSource(b, TextureRole.Color)], DictionaryFileView.Empty);
        Assert.False(r.Ok);
        Assert.Null(r.StagingFolder);
        Assert.Empty(r.Files);
        Assert.All(r.Targets, t => Assert.Contains(t.Collisions, c => c.Kind == CollisionKind.DuplicateOutput));

        // Replace tidak menyelesaikan nama ganda; ganti nama menyelesaikannya.
        var stillBlocked = _ws.Prepare([new TextureSource(a, TextureRole.Color, Collision: CollisionChoice.Replace), new TextureSource(b, TextureRole.Color)], DictionaryFileView.Empty);
        Assert.False(stillBlocked.Ok);

        var renamed = _ws.Prepare([new TextureSource(a, TextureRole.Color), new TextureSource(b, TextureRole.Color, OutputName: "badan_b")], DictionaryFileView.Empty);
        Assert.True(renamed.Ok, renamed.Message);
        Assert.Equal(["badan.tga", "badan.texture.txt", "badan_b.tga", "badan_b.texture.txt"], renamed.Files.Select(f => f.RelativePath));

        var skipped = _ws.Prepare([new TextureSource(a, TextureRole.Color), new TextureSource(b, TextureRole.Color, Collision: CollisionChoice.Skip)], DictionaryFileView.Empty);
        Assert.True(skipped.Ok);
        Assert.Equal(TextureItemStatus.Skipped, skipped.Items[1].Status);
    }

    [Fact]
    public void Existing_file_needs_choice_and_replace_binds_base_hash()
    {
        var existingTga = TgaCodec.Encode(new RasterImage(1, 1, [1, 2, 3, 255], false), false);
        var view = new DictionaryFileView(new Dictionary<string, byte[]> { ["textures\\badan.tga"] = existingTga });
        var src = Source("badan.png", PatternPngRgba());

        var blocked = _ws.Prepare([new TextureSource(src, TextureRole.Color)], view, "textures");
        Assert.False(blocked.Ok);
        Assert.Equal(CollisionKind.ExistingFile, blocked.Targets[0].Collisions.Single().Kind);

        var replaced = _ws.Prepare([new TextureSource(src, TextureRole.Color, Collision: CollisionChoice.Replace)], view, "textures");
        Assert.True(replaced.Ok);
        var plan = replaced.ToEditPlan(new Kuid(1, 2, null), "tekstur");
        Assert.Equal(Hashing.Sha256(existingTga), plan.ExpectedBaseHashes!["textures/badan.tga"]);
        Assert.Null(plan.ExpectedBaseHashes["textures/badan.texture.txt"]);
    }

    [Fact]
    public void Existing_metadata_is_updated_and_unknown_keys_kept()
    {
        var oldMeta = Encoding.UTF8.GetBytes("Primary=badan.jpg\r\nAlpha=badan_mask.bmp\r\nMipmapBias=-1\r\n");
        var view = new DictionaryFileView(new Dictionary<string, byte[]> { ["badan.texture.txt"] = oldMeta });
        var r = _ws.Prepare([new TextureSource(Source("badan.png", PatternPngRgba()), TextureRole.Color)], view);

        var meta = Encoding.UTF8.GetString(r.Files.Single(f => f.RelativePath == "badan.texture.txt").Bytes);
        Assert.Equal("Primary=badan.tga\r\nAlpha=badan_mask.bmp\r\nMipmapBias=-1\r\nTile=st\r\n", meta);
        var notes = r.Items[0].Notes;
        Assert.Contains(notes, n => n.Contains("Primary badan.jpg → badan.tga"));
        Assert.Contains(notes, n => n.Contains("Alpha=badan_mask.bmp") && n.Contains("tidak ditemukan"));
        Assert.Contains(notes, n => n.Contains("MipmapBias"));
        Assert.Equal(Hashing.Sha256(oldMeta), r.Files.Single(f => f.RelativePath == "badan.texture.txt").BaseHash);
    }

    [Fact]
    public void Ambiguous_metadata_fails_only_that_item()
    {
        var view = new DictionaryFileView(new Dictionary<string, byte[]> { ["a.texture.txt"] = Encoding.UTF8.GetBytes("Primary=x\nPrimary=y\n") });
        var r = _ws.Prepare([
            new TextureSource(Source("a.png", PatternPngRgba()), TextureRole.Color),
            new TextureSource(Source("b.png", PatternPngRgb()), TextureRole.Color),
        ], view);
        Assert.True(r.Ok);
        Assert.Equal(TextureItemStatus.Failed, r.Items[0].Status);
        Assert.Equal(TextureItemStatus.Ready, r.Items[1].Status);
        Assert.DoesNotContain(r.Files, f => f.RelativePath.StartsWith("a.", StringComparison.Ordinal));
    }

    [Fact]
    public void Separate_mask_sets_alpha_key_and_checks_size()
    {
        var mask = Png(W, H, 0, (x, y) => [x * 50 + y]);
        var r = _ws.Prepare([new TextureSource(Source("kaca.png", PatternPngRgb()), TextureRole.Color, AlphaMaskPath: Source("kaca-mask.png", mask))], DictionaryFileView.Empty);
        Assert.True(r.Ok);
        Assert.Equal("Primary=kaca.tga\r\nAlpha=kaca_alpha.tga\r\nTile=st\r\n",
            Encoding.UTF8.GetString(r.Files.Single(f => f.RelativePath == "kaca.texture.txt").Bytes));
        Assert.Equal((51, 51, 51, 255), ReadBack(r, "kaca_alpha.tga").Pixel(1, 1));

        var small = Png(2, 2, 0, (_, _) => [9]);
        var bad = _ws.Prepare([new TextureSource(Source("kaca2.png", PatternPngRgb()), TextureRole.Color, AlphaMaskPath: Source("m2.png", small))], DictionaryFileView.Empty);
        Assert.Equal(TextureItemStatus.Failed, bad.Items[0].Status);
        Assert.Contains("berbeda", bad.Items[0].Error);
    }

    [Theory]
    [InlineData("../luar")]
    [InlineData("a/b")]
    [InlineData("nama?")]
    [InlineData("  ")]
    public void Invalid_output_names_are_blocked(string name)
    {
        var r = _ws.Prepare([new TextureSource(Source("x.png", PatternPngRgb()), TextureRole.Color, OutputName: name)], DictionaryFileView.Empty);
        Assert.False(r.Ok);
        Assert.Equal(CollisionKind.InvalidName, r.Targets[0].Collisions.Single().Kind);
    }

    [Fact]
    public void Target_directory_cannot_escape_asset()
    {
        Assert.Throws<ArgumentException>(() =>
            _ws.Prepare([new TextureSource(Source("x.png", PatternPngRgb()), TextureRole.Color)], DictionaryFileView.Empty, "../luar"));
    }

    [Fact]
    public void Export_to_folder_reports_saved_not_installed_AC25()
    {
        var outDir = Path.Combine(_root, "hasil");
        Directory.CreateDirectory(outDir);
        var r = _ws.Prepare([new TextureSource(Source("badan.png", PatternPngRgba()), TextureRole.Color)], new FolderFileView(outDir));
        var (ok, msg, _) = TextureWorkshop.ExportToFolder(r, outDir);
        Assert.True(ok);
        Assert.Contains("Belum terpasang di Trainz", msg);
        Assert.True(File.Exists(Path.Combine(outDir, "badan.tga")));

        // Folder berubah setelah persiapan: berhenti tanpa menulis.
        var again = _ws.Prepare([new TextureSource(Source("badan.png", PatternPngRgba()), TextureRole.Color, Collision: CollisionChoice.Replace)], new FolderFileView(outDir));
        File.WriteAllText(Path.Combine(outDir, "badan.texture.txt"), "diubah");
        var (ok2, _, conflicts) = TextureWorkshop.ExportToFolder(again, outDir);
        Assert.False(ok2);
        Assert.Contains("badan.texture.txt", conflicts);
    }

    [Fact]
    public async Task Texture_and_config_change_apply_in_one_transaction_and_undo()
    {
        var asset = new Kuid(999001, 1201, null);
        const string config = "kuid <kuid:999001:1201>\r\ndescription \"lama\"\r\n";
        using var sim = new SimulatedTrainz();
        sim.Install(asset, new() { ["config.txt"] = Encoding.UTF8.GetBytes(config) });
        var store = new TxStore(Path.Combine(_root, "store"));

        var tex = _ws.Prepare([new TextureSource(Source("badan.png", PatternPngRgba()), TextureRole.Color)], DictionaryFileView.Empty, "textures");
        var draft = new Draft(asset);
        draft.Put(new DraftItem.SetText("description", "baru", "Editor aset", "Ubah deskripsi"));
        foreach (var item in tex.ToDraftItems()) draft.Put(item);

        var (plan, build) = draft.ToEditPlan(Encoding.UTF8.GetBytes(config), "tekstur + deskripsi");
        Assert.True(build.Ok, build.Error);
        Assert.Equal(3, plan!.Changes.Count);

        var runner = new TransactionRunner(sim, store);
        var r = await runner.ApplyAsync(plan);
        Assert.Equal(TxStatus.Succeeded, r.Status);
        var installed = sim.InstalledFile(asset, Path.Combine("textures", "badan.tga"));
        Assert.Equal(tex.Files[0].Bytes, installed);
        Assert.Contains("baru", Encoding.UTF8.GetString(sim.InstalledFile(asset, "config.txt")!));

        var u = await runner.UndoAsync(r.TxId);
        Assert.Equal(TxStatus.Succeeded, u.Status);
        Assert.Null(sim.InstalledFile(asset, Path.Combine("textures", "badan.tga")));
        Assert.Equal(config, Encoding.UTF8.GetString(sim.InstalledFile(asset, "config.txt")!));
    }

    [Fact]
    public void Texture_only_draft_does_not_touch_config()
    {
        var tex = _ws.Prepare([new TextureSource(Source("badan.png", PatternPngRgb()), TextureRole.Color)], DictionaryFileView.Empty);
        var draft = new Draft(new Kuid(1, 2, null));
        foreach (var item in tex.ToDraftItems()) draft.Put(item);
        var (plan, build) = draft.ToEditPlan(Encoding.UTF8.GetBytes("kuid <kuid:1:2>\r\n"), "tekstur");
        Assert.True(build.Ok, build.Error);
        Assert.DoesNotContain(plan!.Changes, c => c.RelativePath == "config.txt");

        draft.Put(new DraftItem.WriteFile("config.txt", [1], null, "x", "salah"));
        Assert.Null(draft.ToEditPlan(Encoding.UTF8.GetBytes("kuid <kuid:1:2>\r\n"), "x").Plan);
    }
}

public sealed class ChannelPackerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rn pbr ").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Img(string name, Func<int, int, int[]> px)
    {
        var p = Path.Combine(_root, name);
        File.WriteAllBytes(p, Png(W, H, 6, px));
        return p;
    }

    [Fact]
    public void Every_output_channel_comes_from_its_declared_source_AC17()
    {
        var a = Img("a.png", (x, y) => [x * 10, 1, 2, 3]);
        var b = Img("b.png", (x, y) => [9, 9, y * 20, 200 - x]);
        var c = Img("c.png", (x, y) => [5, 6, 7, x * 30 + y]);
        var r = ChannelPacker.Pack(new(a, SourceChannel.R), new(b, SourceChannel.B), ChannelInput.Const(77), new(c, SourceChannel.A));
        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.ChannelReport, s => s.StartsWith("B: nilai konstan pilihan pengguna (77)", StringComparison.Ordinal));

        var ws = new TextureWorkshop(Path.Combine(_root, "staging"));
        var prepared = ws.PrepareImage(r.Image!, "param", "uji", DictionaryFileView.Empty);
        Assert.True(prepared.Ok);
        var back = ImageCodec.Decode(prepared.Files.Single(f => f.RelativePath == "param.tga").Bytes).Image!;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            Assert.Equal(((byte)(x * 10), (byte)(y * 20), (byte)77, (byte)(x * 30 + y)), back.Pixel(x, y));
        Assert.Equal(32, prepared.Items[0].OutputBits);
    }

    [Fact]
    public void Size_mismatch_and_constant_only_are_refused()
    {
        var a = Img("a.png", (x, y) => [1, 2, 3, 4]);
        var small = Path.Combine(_root, "small.png");
        File.WriteAllBytes(small, Png(2, 2, 2, (_, _) => [1, 1, 1]));
        Assert.Contains("Ukuran gambar sumber berbeda", ChannelPacker.Pack(new(a), new(small), ChannelInput.Const(0), ChannelInput.Const(0)).Error);
        Assert.Contains("Minimal satu kanal", ChannelPacker.Pack(ChannelInput.Const(1), ChannelInput.Const(1), ChannelInput.Const(1), ChannelInput.Const(1)).Error);
    }

    [Fact]
    public void Luminance_and_missing_alpha_are_noted()
    {
        var rgb = Path.Combine(_root, "rgb.png");
        File.WriteAllBytes(rgb, PatternPngRgb());
        var r = ChannelPacker.Pack(new(rgb, SourceChannel.Luminance), new(rgb, SourceChannel.A), ChannelInput.Const(0), ChannelInput.Const(255));
        Assert.True(r.Ok);
        Assert.Contains(r.Notes, n => n.Contains("kecerahan"));
        Assert.Contains(r.Notes, n => n.Contains("tidak memiliki alpha"));
        Assert.Equal(255, r.Image!.Pixel(0, 0).G);
    }
}
