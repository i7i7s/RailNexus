using System.Globalization;
using RailNexus.Core.Roles;
using RailNexus.Core.Tx;

namespace RailNexus.Core.Texture;

/// <summary>Tujuan gambar (PRD 13.2 langkah 3). Menentukan perlakuan alpha; tidak pernah memicu operasi warna.</summary>
public enum TextureRole
{
    /// <summary>Gambar warna; alpha (bila ada) berarti transparansi.</summary>
    Color,
    /// <summary>Normal map; alpha dapat menyimpan data (mis. height) dan tidak dibuang otomatis.</summary>
    Normal,
    /// <summary>Parameter map (mis. kanal metallic/roughness); alpha adalah data material (AC-17).</summary>
    Parameter,
    /// <summary>Gambar umum tanpa peran khusus.</summary>
    Generic,
}

public enum AlphaChoice
{
    /// <summary>Gambar warna/umum: alpha penuh 255 menjadi 24-bit. Normal/parameter: alpha selalu dipertahankan.</summary>
    Auto,
    Keep,
    /// <summary>Buang alpha secara sengaja; dicatat di hasil.</summary>
    Discard,
}

/// <summary>Pilihan pengguna untuk benturan nama (PRD 13.7). Ganti nama dilakukan lewat <see cref="TextureSource.OutputName"/>.</summary>
public enum CollisionChoice
{
    Unresolved,
    /// <summary>Lewati sumber ini.</summary>
    Skip,
    /// <summary>Ganti berkas yang sudah ada; transaksi menyimpan backup sebelum menulis.</summary>
    Replace,
}

public sealed record TextureSource(
    string SourcePath,
    TextureRole Role,
    AlphaChoice Alpha = AlphaChoice.Auto,
    /// <summary>Nama output tanpa ekstensi; default nama berkas sumber.</summary>
    string? OutputName = null,
    CollisionChoice Collision = CollisionChoice.Unresolved,
    /// <summary>Gambar mask terpisah untuk key Alpha= di metadata.</summary>
    string? AlphaMaskPath = null);

public enum CollisionKind
{
    /// <summary>Dua sumber menghasilkan nama output yang sama (AC-18).</summary>
    DuplicateOutput,
    /// <summary>Berkas dengan nama output sudah ada di tujuan.</summary>
    ExistingFile,
    /// <summary>Nama output tidak dapat dipakai sebagai nama berkas.</summary>
    InvalidName,
}

public sealed record NameCollision(string RelativePath, CollisionKind Kind, string Message);

/// <summary>Rencana nama untuk satu sumber: gambar output, metadata, dan mask opsional.</summary>
public sealed record TextureTarget(
    int Index,
    TextureSource Source,
    string ImagePath,
    string MetadataPath,
    string? MaskPath,
    IReadOnlyList<NameCollision> Collisions)
{
    public bool Skipped => Source.Collision == CollisionChoice.Skip;

    /// <summary>Benturan yang belum diselesaikan oleh pilihan pengguna.</summary>
    public IEnumerable<NameCollision> Unresolved => Skipped ? [] : Collisions.Where(c =>
        c.Kind != CollisionKind.ExistingFile || Source.Collision != CollisionChoice.Replace);
}

public enum TextureItemStatus { Ready, Skipped, Failed }

public sealed record TextureItemResult(
    TextureTarget Target,
    TextureItemStatus Status,
    string SourceDescription,
    int Width,
    int Height,
    /// <summary>24 atau 32; null bila tidak ada output.</summary>
    int? OutputBits,
    IReadOnlyList<string> Notes,
    string Error = "");

/// <summary>Berkas hasil di staging, siap menjadi <see cref="FileChange"/>.</summary>
public sealed record StagedFile(string RelativePath, byte[] Bytes, string? BaseHash, string StagedPath, string Summary);

public sealed record TexturePrepareResult(
    bool Ok,
    string Message,
    IReadOnlyList<TextureTarget> Targets,
    IReadOnlyList<TextureItemResult> Items,
    IReadOnlyList<StagedFile> Files,
    string? StagingFolder)
{
    public IReadOnlyList<FileChange> ToFileChanges() => Files.Select(f => new FileChange(f.RelativePath, f.Bytes)).ToList();

    /// <summary>Item untuk draft bersama, agar tekstur dan perubahan config diterapkan dalam satu transaksi (PRD 9.5).</summary>
    public IReadOnlyList<DraftItem> ToDraftItems(string module = "Workshop Tekstur") =>
        Files.Select(f => (DraftItem)new DraftItem.WriteFile(f.RelativePath, f.Bytes, f.BaseHash, module, f.Summary)).ToList();

    /// <summary>Rencana transaksi untuk aset terpasang; hash dasar mengikat keadaan tujuan saat persiapan (AC-10).</summary>
    public EditPlan ToEditPlan(Kuid kuid, string purpose)
    {
        if (!Ok) throw new InvalidOperationException("Hasil workshop belum siap diterapkan: " + Message);
        return new EditPlan(kuid, purpose, ToFileChanges(),
            Files.ToDictionary(f => f.RelativePath, f => f.BaseHash, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Workshop tekstur dasar (PRD 13.2–13.4, RN-009). Alurnya: rencana nama → selesaikan benturan →
/// salin sumber ke staging → dekode dari salinan → tulis TGA ke staging → baca ulang dan bandingkan
/// setiap piksel → buat/perbarui <c>.texture.txt</c>. Sumber tidak pernah ditulis; aset tujuan hanya
/// berubah lewat transaksi (<see cref="TexturePrepareResult.ToEditPlan"/>) atau ekspor folder.
/// </summary>
public sealed class TextureWorkshop(string stagingRoot)
{
    public const string ImageExtension = ".tga";
    public const string MetadataExtension = ".texture.txt";
    public const string MaskSuffix = "_alpha";

    private static readonly HashSet<string> KnownKeys = new(["Primary", "Alpha", "Tile"], StringComparer.OrdinalIgnoreCase);

    public static string RoleLabel(TextureRole role) => role switch
    {
        TextureRole.Color => "Gambar warna",
        TextureRole.Normal => "Normal map",
        TextureRole.Parameter => "Parameter map",
        _ => "Gambar umum",
    };

    /// <summary>Menyusun nama output dan menandai benturan tanpa membaca atau menulis gambar.</summary>
    public static IReadOnlyList<TextureTarget> PlanNames(IReadOnlyList<TextureSource> sources, IAssetFileView existing, string targetDirectory = "")
    {
        var dir = NormalizeDirectory(targetDirectory);
        var drafts = sources.Select((s, i) =>
        {
            var name = OutputStem(s);
            var invalid = InvalidNameReason(name);
            var image = dir + name + ImageExtension;
            return (Index: i, Source: s, Name: name, Invalid: invalid, Image: image, Meta: dir + name + MetadataExtension,
                Mask: s.AlphaMaskPath is null ? null : dir + name + MaskSuffix + ImageExtension);
        }).ToList();

        // Semua path yang akan ditulis oleh sumber yang tidak dilewati, untuk mendeteksi nama ganda.
        var writers = drafts.Where(d => d.Source.Collision != CollisionChoice.Skip && d.Invalid is null)
            .SelectMany(d => new[] { d.Image, d.Meta, d.Mask }.OfType<string>().Select(p => (d.Index, Path: p)))
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Index).Distinct().Count() > 1)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Index).Distinct().ToList(), StringComparer.OrdinalIgnoreCase);

        return drafts.Select(d =>
        {
            var collisions = new List<NameCollision>();
            if (d.Invalid is not null)
            {
                collisions.Add(new NameCollision(d.Name, CollisionKind.InvalidName, d.Invalid));
                return new TextureTarget(d.Index, d.Source, d.Image, d.Meta, d.Mask, collisions);
            }
            foreach (var p in new[] { d.Image, d.Meta, d.Mask }.OfType<string>())
            {
                if (writers.TryGetValue(p, out var others))
                {
                    var names = string.Join(", ", others.Where(o => o != d.Index).Select(o => Path.GetFileName(sources[o].SourcePath)));
                    collisions.Add(new NameCollision(p, CollisionKind.DuplicateOutput,
                        $"'{p}' juga dihasilkan dari {names}. Ganti nama output atau lewati salah satunya."));
                }
            }
            // Metadata lama bukan benturan: isinya diperbarui dengan key lain dipertahankan.
            foreach (var p in new[] { d.Image, d.Mask }.OfType<string>())
                if (existing.Hash(p) is not null)
                    collisions.Add(new NameCollision(p, CollisionKind.ExistingFile,
                        $"'{p}' sudah ada di tujuan. Pilih ganti (dengan backup), ganti nama, atau lewati."));
            return new TextureTarget(d.Index, d.Source, d.Image, d.Meta, d.Mask, collisions);
        }).ToList();
    }

    public TexturePrepareResult Prepare(IReadOnlyList<TextureSource> sources, IAssetFileView existing, string targetDirectory = "")
    {
        var targets = PlanNames(sources, existing, targetDirectory);
        var unresolved = targets.SelectMany(t => t.Unresolved).ToList();
        if (unresolved.Count > 0)
            return new TexturePrepareResult(false,
                $"{unresolved.Count} benturan nama belum diselesaikan. Tidak ada berkas yang dibuat.", targets, [], [], null);

        var staging = Path.Combine(stagingRoot, $"{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(Path.Combine(staging, "src"));
        Directory.CreateDirectory(Path.Combine(staging, "out"));

        var items = new List<TextureItemResult>();
        var files = new List<StagedFile>();
        foreach (var t in targets)
        {
            if (t.Skipped)
            {
                items.Add(new TextureItemResult(t, TextureItemStatus.Skipped, "", 0, 0, null, ["Dilewati atas pilihan pengguna."]));
                continue;
            }
            // Satu gambar rusak tidak menggugurkan hasil gambar lain (PRD 13.7).
            var (item, produced) = Convert(t, existing, staging);
            items.Add(item);
            files.AddRange(produced);
        }

        var ready = items.Count(i => i.Status == TextureItemStatus.Ready);
        var failed = items.Count(i => i.Status == TextureItemStatus.Failed);
        var msg = failed == 0
            ? $"{ready} gambar siap; hasil sudah dibaca ulang dan cocok dengan sumber."
            : $"{ready} gambar siap, {failed} gagal. Gambar yang gagal tidak dimasukkan.";
        return new TexturePrepareResult(true, msg, targets, items, files, staging);
    }

    private static (TextureItemResult, IReadOnlyList<StagedFile>) Convert(TextureTarget t, IAssetFileView existing, string staging)
    {
        var notes = new List<string>();
        TextureItemResult Fail(string error, string desc = "", int w = 0, int h = 0) =>
            new(t, TextureItemStatus.Failed, desc, w, h, null, notes, error);

        var (source, sourceErr) = Stage(t.Source.SourcePath, staging, $"{t.Index:D3}");
        if (source is null) return (Fail(sourceErr), []);
        var decoded = ImageCodec.Decode(source.Value.Bytes);
        if (!decoded.Ok) return (Fail(decoded.Error), []);
        var image = decoded.Image!;
        notes.AddRange(decoded.Notes);

        RasterImage? mask = null;
        string? maskHash = null;
        if (t.Source.AlphaMaskPath is { } maskPath)
        {
            var (m, maskErr) = Stage(maskPath, staging, $"{t.Index:D3}-mask");
            maskHash = m?.Hash;
            if (m is null) return (Fail("Mask: " + maskErr, decoded.Description, image.Width, image.Height), []);
            var md = ImageCodec.Decode(m.Value.Bytes);
            if (!md.Ok) return (Fail("Mask: " + md.Error, decoded.Description, image.Width, image.Height), []);
            if (md.Image!.Width != image.Width || md.Image.Height != image.Height)
                return (Fail($"Ukuran mask {md.Image.Width} × {md.Image.Height} berbeda dari gambar {image.Width} × {image.Height}.",
                    decoded.Description, image.Width, image.Height), []);
            mask = md.Image;
        }

        var withAlpha = DecideAlpha(t.Source, image, notes);
        if (!IsPowerOfTwo(image.Width) || !IsPowerOfTwo(image.Height))
            notes.Add($"Ukuran {image.Width} × {image.Height} bukan pangkat dua; ukuran dipertahankan. Periksa apakah build tujuan menerimanya.");

        var produced = new List<StagedFile>();
        var (imageFile, imageErr) = WriteVerified(t.ImagePath, image, withAlpha, existing, staging,
            $"{Path.GetFileName(t.Source.SourcePath)} → {t.ImagePath} ({(withAlpha ? 32 : 24)}-bit)");
        if (imageFile is null) return (Fail(imageErr, decoded.Description, image.Width, image.Height), []);
        produced.Add(imageFile);

        if (mask is not null)
        {
            var (maskFile, maskWriteErr) = WriteVerified(t.MaskPath!, mask, mask.HasAlpha, existing, staging,
                $"{Path.GetFileName(t.Source.AlphaMaskPath)} → {t.MaskPath} (mask)");
            if (maskFile is null) return (Fail(maskWriteErr, decoded.Description, image.Width, image.Height), []);
            produced.Add(maskFile);
        }

        var (metaFile, metaErr) = BuildMetadata(t.MetadataPath, Path.GetFileName(t.ImagePath),
            t.MaskPath is null ? null : Path.GetFileName(t.MaskPath), existing, staging, notes);
        if (metaFile is null) return (Fail(metaErr, decoded.Description, image.Width, image.Height), []);
        produced.Add(metaFile);

        // Bukti bahwa sumber tidak tersentuh: hash dibaca ulang setelah konversi.
        if (!SourceUnchanged(t.Source.SourcePath, source.Value.Hash) ||
            (t.Source.AlphaMaskPath is { } mp && !SourceUnchanged(mp, maskHash!)))
            return (Fail("Sumber berubah selama konversi; hasil dibuang.", decoded.Description, image.Width, image.Height), []);

        return (new TextureItemResult(t, TextureItemStatus.Ready, decoded.Description, image.Width, image.Height,
            withAlpha ? 32 : 24, notes), produced);
    }

    /// <summary>Aturan alpha per peran (PRD 13.3; AC-16, AC-17).</summary>
    private static bool DecideAlpha(TextureSource s, RasterImage image, List<string> notes)
    {
        if (!image.HasAlpha)
        {
            if (s.Alpha == AlphaChoice.Keep) notes.Add("Sumber tidak memiliki alpha; output 24-bit.");
            return false;
        }
        if (s.Alpha == AlphaChoice.Discard)
        {
            notes.Add(s.Role is TextureRole.Normal or TextureRole.Parameter
                ? "Alpha berisi data material dibuang atas pilihan pengguna."
                : "Alpha dibuang atas pilihan pengguna.");
            return false;
        }
        if (s.Alpha == AlphaChoice.Keep || s.Role is TextureRole.Normal or TextureRole.Parameter)
        {
            if (s.Role is TextureRole.Normal or TextureRole.Parameter)
                notes.Add("Alpha diperlakukan sebagai data dan dipertahankan; tidak ada koreksi warna.");
            return true;
        }
        if (image.AlphaIsOpaque())
        {
            notes.Add("Alpha seluruhnya 255 (tanpa transparansi); output 24-bit tanpa kehilangan data.");
            return false;
        }
        return true;
    }

    private static (StagedFile? File, string Error) WriteVerified(string rel, RasterImage image, bool withAlpha,
        IAssetFileView existing, string staging, string summary)
    {
        var bytes = TgaCodec.Encode(image, withAlpha);
        var path = SafePath.Combine(Path.Combine(staging, "out"), rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);

        // Baca ulang dari disk, bukan dari memori, lalu bandingkan setiap kanal dan orientasi (AC-16).
        var back = ImageCodec.Decode(File.ReadAllBytes(path));
        if (!back.Ok) return (null, $"Verifikasi baca ulang gagal: {back.Error}");
        var err = Compare(image, back.Image!, withAlpha);
        if (err is not null) return (null, $"Verifikasi baca ulang gagal: {err}");
        return (new StagedFile(rel, File.ReadAllBytes(path), existing.Hash(rel), path, summary), "");
    }

    internal static string? Compare(RasterImage expected, RasterImage actual, bool alphaKept)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return $"ukuran {actual.Width} × {actual.Height}, seharusnya {expected.Width} × {expected.Height}.";
        if (actual.HasAlpha != alphaKept) return alphaKept ? "kanal alpha hilang." : "kanal alpha tidak diharapkan.";
        for (var i = 0; i < expected.Rgba.Length; i += 4)
        {
            var mismatch = expected.Rgba[i] != actual.Rgba[i] || expected.Rgba[i + 1] != actual.Rgba[i + 1] || expected.Rgba[i + 2] != actual.Rgba[i + 2]
                           || (alphaKept ? expected.Rgba[i + 3] != actual.Rgba[i + 3] : actual.Rgba[i + 3] != 255);
            if (!mismatch) continue;
            var p = i / 4;
            return string.Create(CultureInfo.InvariantCulture, $"piksel ({p % expected.Width}, {p / expected.Width}) berbeda.");
        }
        return null;
    }

    private static (StagedFile? File, string Error) BuildMetadata(string metadataPath, string imageName, string? maskName,
        IAssetFileView existing, string staging, List<string> notes)
    {
        var old = existing.Read(metadataPath);
        TextureMetadata meta;
        if (old is null)
        {
            meta = TextureMetadata.CreateNew();
        }
        else
        {
            var (parsed, err) = TextureMetadata.Parse(old);
            if (parsed is null) return (null, err);
            meta = parsed;
        }

        var oldPrimary = meta.Get("Primary");
        meta.Set("Primary", imageName);
        if (old is not null && oldPrimary is not null && !oldPrimary.Equals(imageName, StringComparison.OrdinalIgnoreCase))
            notes.Add($"Metadata: Primary {oldPrimary} → {imageName}.");

        if (maskName is not null)
        {
            var oldAlpha = meta.Get("Alpha");
            meta.Set("Alpha", maskName);
            if (oldAlpha is not null && !oldAlpha.Equals(maskName, StringComparison.OrdinalIgnoreCase))
                notes.Add($"Metadata: Alpha {oldAlpha} → {maskName}.");
        }
        else if (meta.Get("Alpha") is { Length: > 0 } keptAlpha)
        {
            // Alpha lama dipertahankan sampai pengguna memilih penggantian yang jelas (PRD 13.4 langkah 5).
            var dir = Path.GetDirectoryName(metadataPath.Replace('\\', '/'))?.Replace('\\', '/');
            var rel = string.IsNullOrEmpty(dir) ? keptAlpha : dir + "/" + keptAlpha;
            notes.Add(existing.Hash(rel) is null
                ? $"Metadata lama memuat Alpha={keptAlpha}, tetapi berkas itu tidak ditemukan. Periksa sebelum menerapkan."
                : $"Alpha={keptAlpha} dari metadata lama dipertahankan.");
        }

        if (meta.Get("Tile") is null) meta.Set("Tile", "st");
        var unknown = meta.Keys().Where(k => !KnownKeys.Contains(k)).ToList();
        if (old is not null && unknown.Count > 0)
            notes.Add($"Key lain dipertahankan: {string.Join(", ", unknown)}.");

        var bytes = meta.ToBytes();
        if (old is not null && bytes.AsSpan().SequenceEqual(old)) notes.Add("Metadata lama sudah sesuai; tidak berubah.");
        var path = SafePath.Combine(Path.Combine(staging, "out"), metadataPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return (new StagedFile(metadataPath, bytes, old is null ? null : Hashing.Sha256(old), path,
            old is null ? $"{metadataPath} dibuat" : $"{metadataPath} diperbarui"), "");
    }

    /// <summary>Menyalin sumber ke staging dan memastikan salinan identik.</summary>
    private static ((byte[] Bytes, string Hash)? Copy, string Error) Stage(string sourcePath, string staging, string prefix)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(sourcePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, $"Sumber tidak dapat dibaca: {ex.Message}");
        }
        var hash = Hashing.Sha256(bytes);
        var copy = Path.Combine(staging, "src", $"{prefix}-{Path.GetFileName(sourcePath)}");
        File.WriteAllBytes(copy, bytes);
        var staged = File.ReadAllBytes(copy);
        if (Hashing.Sha256(staged) != hash) return (null, "Salinan staging tidak identik dengan sumber.");
        return ((staged, hash), "");
    }

    private static bool SourceUnchanged(string path, string hash)
    {
        try { return Hashing.Sha256(File.ReadAllBytes(path)) == hash; }
        catch (IOException) { return false; }
    }

    /// <summary>
    /// Menulis gambar hasil rakitan (mis. perakit kanal PBR) melalui jalur yang sama: benturan nama,
    /// staging, baca ulang per kanal, dan metadata. Output selalu 32-bit karena kanal alpha berisi data.
    /// </summary>
    public TexturePrepareResult PrepareImage(RasterImage image, string outputName, string description, IAssetFileView existing,
        string targetDirectory = "", CollisionChoice collision = CollisionChoice.Unresolved)
    {
        var source = new TextureSource(outputName + ".rakitan", TextureRole.Parameter, AlphaChoice.Keep, outputName, collision);
        var targets = PlanNames([source], existing, targetDirectory);
        var unresolved = targets.SelectMany(t => t.Unresolved).ToList();
        if (unresolved.Count > 0)
            return new TexturePrepareResult(false, $"{unresolved.Count} benturan nama belum diselesaikan. Tidak ada berkas yang dibuat.", targets, [], [], null);
        var t = targets[0];
        if (t.Skipped)
            return new TexturePrepareResult(true, "Dilewati.", targets, [new TextureItemResult(t, TextureItemStatus.Skipped, description, image.Width, image.Height, null, [])], [], null);

        var staging = Path.Combine(stagingRoot, $"{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(Path.Combine(staging, "out"));
        var notes = new List<string>();
        var (imageFile, err) = WriteVerified(t.ImagePath, image, withAlpha: true, existing, staging, $"{description} → {t.ImagePath} (32-bit)");
        if (imageFile is null)
            return new TexturePrepareResult(true, err, targets, [new TextureItemResult(t, TextureItemStatus.Failed, description, image.Width, image.Height, null, notes, err)], [], staging);
        var (meta, metaErr) = BuildMetadata(t.MetadataPath, Path.GetFileName(t.ImagePath), null, existing, staging, notes);
        if (meta is null)
            return new TexturePrepareResult(true, metaErr, targets, [new TextureItemResult(t, TextureItemStatus.Failed, description, image.Width, image.Height, null, notes, metaErr)], [], staging);
        return new TexturePrepareResult(true, "1 gambar rakitan siap; setiap kanal dibaca ulang dan cocok.", targets,
            [new TextureItemResult(t, TextureItemStatus.Ready, description, image.Width, image.Height, 32, notes)], [imageFile, meta], staging);
    }

    /// <summary>
    /// Alur B (PRD 13.4): membuat atau memperbarui <c>.texture.txt</c> untuk gambar yang sudah ada di aset,
    /// tanpa mengonversi gambarnya. Gambar harus dapat dibaca agar metadata tidak menunjuk berkas rusak.
    /// </summary>
    public TexturePrepareResult PrepareMetadata(IReadOnlyList<string> imagePaths, IAssetFileView existing)
    {
        var staging = Path.Combine(stagingRoot, $"{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(Path.Combine(staging, "out"));
        var targets = new List<TextureTarget>();
        var items = new List<TextureItemResult>();
        var files = new List<StagedFile>();
        foreach (var (rel, index) in imagePaths.Select((p, i) => (p.Replace('\\', '/'), i)))
        {
            var dir = Path.GetDirectoryName(rel)?.Replace('\\', '/') is { Length: > 0 } folder ? folder + "/" : "";
            var stem = Path.GetFileNameWithoutExtension(rel);
            var t = new TextureTarget(index, new TextureSource(rel, TextureRole.Generic), rel, dir + stem + MetadataExtension, null, []);
            targets.Add(t);
            var notes = new List<string>();
            if (existing.Hash(rel) is null)
            {
                items.Add(new TextureItemResult(t, TextureItemStatus.Failed, "", 0, 0, null, notes, $"'{rel}' tidak ada di aset."));
                continue;
            }
            byte[]? bytes;
            try { bytes = existing.Read(rel); }
            catch (NotSupportedException) { bytes = null; }
            var desc = "";
            int w = 0, h = 0;
            if (bytes is null) notes.Add("Isi gambar tidak tersedia untuk diperiksa (terlalu besar); hanya keberadaannya yang dipastikan.");
            else
            {
                var d = ImageCodec.Decode(bytes);
                if (!d.Ok && d.Format is ImageFormat.Unknown)
                {
                    items.Add(new TextureItemResult(t, TextureItemStatus.Failed, "", 0, 0, null, notes, $"'{rel}' bukan gambar yang dikenali: {d.Error}"));
                    continue;
                }
                desc = d.Ok ? d.Description : d.Format.ToString().ToUpperInvariant();
                (w, h) = d.Ok ? (d.Image!.Width, d.Image.Height) : (0, 0);
                if (!d.Ok) notes.Add($"Format {desc} dikenali tetapi isinya belum dapat diperiksa RailNexus.");
            }
            notes.Add("Penerimaan format gambar ini oleh build tujuan belum diuji; validasi Trainz setelah menerapkan.");
            var (meta, err) = BuildMetadata(t.MetadataPath, Path.GetFileName(rel), null, existing, staging, notes);
            if (meta is null)
            {
                items.Add(new TextureItemResult(t, TextureItemStatus.Failed, desc, w, h, null, notes, err));
                continue;
            }
            files.Add(meta);
            items.Add(new TextureItemResult(t, TextureItemStatus.Ready, desc, w, h, null, notes));
        }
        var ready = items.Count(i => i.Status == TextureItemStatus.Ready);
        return new TexturePrepareResult(true, $"{ready} metadata siap, {items.Count - ready} gagal.", targets, items, files, staging);
    }

    /// <summary>
    /// Menyimpan hasil ke folder bebas (bukan aset terpasang). Berhenti tanpa menulis bila isi folder
    /// berbeda dari saat persiapan. Hasilnya tersimpan, bukan terpasang di Trainz (AC-25).
    /// </summary>
    public static (bool Ok, string Message, IReadOnlyList<string> Conflicts) ExportToFolder(TexturePrepareResult result, string folder)
    {
        if (!result.Ok) return (false, result.Message, []);
        var conflicts = result.Files
            .Where(f => !string.Equals(Hashing.HashFileOrNull(SafePath.Combine(folder, f.RelativePath)), f.BaseHash, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.RelativePath).ToList();
        if (conflicts.Count > 0)
            return (false, "Isi folder berubah sejak hasil disiapkan. Tidak ada berkas yang ditulis.", conflicts);
        foreach (var f in result.Files)
        {
            var path = SafePath.Combine(folder, f.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, f.Bytes);
        }
        return (true, $"{result.Files.Count} berkas tersimpan di folder. Belum terpasang di Trainz.", []);
    }

    private static string OutputStem(TextureSource s)
    {
        var name = (s.OutputName ?? Path.GetFileNameWithoutExtension(s.SourcePath)).Trim();
        if (name.EndsWith(ImageExtension, StringComparison.OrdinalIgnoreCase)) name = name[..^ImageExtension.Length];
        return name;
    }

    private static string? InvalidNameReason(string name)
    {
        if (name.Length == 0) return "Nama output kosong.";
        if (name is "." or ".." || name.EndsWith('.') || name.EndsWith(' '))
            return $"Nama output '{name}' tidak dapat dipakai sebagai nama berkas.";
        // Karakter yang tidak sah di Windows diperiksa di semua OS karena aset dipakai Trainz di Windows.
        if (name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 || name.Any(char.IsControl))
            return $"Nama output '{name}' memuat karakter yang tidak boleh ada di nama berkas.";
        return null;
    }

    private static string NormalizeDirectory(string dir)
    {
        dir = dir.Replace('\\', '/').Trim('/');
        if (dir.Length == 0) return "";
        // Validasi: folder tujuan tidak boleh keluar dari folder aset.
        SafePath.Combine(Path.GetTempPath(), dir);
        return dir + "/";
    }

    private static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;
}
