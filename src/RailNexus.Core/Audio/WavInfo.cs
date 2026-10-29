using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace RailNexus.Core.Audio;

/// <summary>Satu loop dari chunk 'smpl' (posisi dalam frame sampel).</summary>
public sealed record WavLoop(long Start, long End);

/// <summary>
/// Hasil membaca header WAV (RIFF/WAVE). Hanya struktur yang dibaca; isi audio tidak diubah dan tidak
/// dinormalisasi (PRD 14). Preview memakai berkas apa adanya.
/// </summary>
public sealed record WavInfo(
    bool Ok,
    string Error,
    int FormatTag,
    int Channels,
    int SampleRate,
    int BitsPerSample,
    long DataBytes,
    IReadOnlyList<WavLoop> Loops)
{
    public bool IsPcm => FormatTag is 1 or 3 or 0xFFFE;
    public long Frames => Channels > 0 && BitsPerSample > 0 ? DataBytes / (Channels * (BitsPerSample / 8)) : 0;
    public TimeSpan Duration => SampleRate > 0 ? TimeSpan.FromSeconds((double)Frames / SampleRate) : TimeSpan.Zero;

    public string Description => !Ok ? Error
        : string.Create(CultureInfo.GetCultureInfo("id-ID"),
              $"{FormatName} · {ChannelText} · {SampleRate} Hz · {BitsPerSample}-bit · {Duration.TotalSeconds:0.00} dtk")
          + (Loops.Count > 0 ? $" · loop {Loops[0].Start}–{Loops[0].End}" : "");

    private string ChannelText => Channels switch { 1 => "mono", 2 => "stereo", _ => $"{Channels} kanal" };

    public string FormatName => FormatTag switch
    {
        1 => "PCM",
        3 => "PCM float",
        0xFFFE => "PCM (extensible)",
        2 => "MS ADPCM",
        0x11 => "IMA ADPCM",
        0x55 => "MP3 dalam WAV",
        _ => $"format 0x{FormatTag:X4}",
    };

    public static WavInfo Fail(string error) => new(false, error, 0, 0, 0, 0, 0, []);

    /// <summary>Mengenali isi berkas; "OggS" dikenali tetapi belum didukung.</summary>
    public static WavInfo Read(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4 && data[..4].SequenceEqual("OggS"u8)) return Fail("Berkas Ogg Vorbis dikenali tetapi belum didukung untuk preview.");
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
            return Fail("Bukan berkas WAV (RIFF/WAVE) yang dikenali.");

        int format = 0, channels = 0, rate = 0, bits = 0;
        long dataBytes = -1;
        var loops = new List<WavLoop>();
        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            var id = Encoding.ASCII.GetString(data.Slice(pos, 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 4, 4));
            var body = pos + 8;
            var available = (int)Math.Min(size, (uint)Math.Max(0, data.Length - body));
            switch (id)
            {
                case "fmt ":
                    if (available < 16) return Fail("Chunk fmt terlalu pendek.");
                    format = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body, 2));
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 2, 2));
                    rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(body + 4, 4));
                    bits = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(body + 14, 2));
                    break;
                case "data":
                    // Data terpotong tetap dilaporkan sesuai yang tersedia, bukan ukuran yang diklaim header.
                    dataBytes = available;
                    break;
                case "smpl" when available >= 36:
                    var count = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(body + 28, 4));
                    for (var i = 0; i < count && body + 36 + (i + 1) * 24 <= body + available; i++)
                    {
                        var lp = body + 36 + i * 24;
                        loops.Add(new WavLoop(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(lp + 8, 4)),
                                              BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(lp + 12, 4))));
                    }
                    break;
            }
            pos = body + (int)Math.Min(size + (size & 1), (uint)int.MaxValue - body);
        }

        if (format == 0) return Fail("Chunk fmt tidak ditemukan.");
        if (dataBytes < 0) return Fail("Chunk data tidak ditemukan.");
        if (channels == 0 || rate == 0) return Fail("Header WAV tidak valid (kanal atau laju sampel nol).");
        var info = new WavInfo(true, "", format, channels, rate, bits, dataBytes, loops);
        return info.IsPcm ? info : info with { Ok = false, Error = $"{info.FormatName} belum didukung untuk preview; simpan ulang sebagai PCM." };
    }

    /// <summary>Perbedaan yang perlu diketahui saat mengganti WAV; tidak memblokir penggantian.</summary>
    public static IReadOnlyList<string> Differences(WavInfo oldInfo, WavInfo newInfo)
    {
        var d = new List<string>();
        if (!oldInfo.Ok || !newInfo.Ok) return d;
        if (oldInfo.Channels != newInfo.Channels) d.Add($"Jumlah kanal berbeda ({oldInfo.Channels} → {newInfo.Channels}).");
        if (oldInfo.SampleRate != newInfo.SampleRate) d.Add($"Laju sampel berbeda ({oldInfo.SampleRate} → {newInfo.SampleRate} Hz).");
        if (oldInfo.BitsPerSample != newInfo.BitsPerSample) d.Add($"Kedalaman bit berbeda ({oldInfo.BitsPerSample} → {newInfo.BitsPerSample}).");
        if (oldInfo.Loops.Count > 0 && newInfo.Loops.Count == 0) d.Add("Berkas lama memiliki titik loop; berkas baru tidak.");
        return d;
    }
}
