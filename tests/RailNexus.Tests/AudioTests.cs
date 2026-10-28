using System.Buffers.Binary;
using System.Text;
using RailNexus.Core.Audio;

namespace RailNexus.Tests;

public static class WavFixtures
{
    /// <summary>WAV sintetis: header RIFF/fmt/data (+ smpl bila ada loop), isi sampel nol.</summary>
    public static byte[] Wav(int channels = 1, int rate = 22050, int bits = 16, int frames = 2205, int format = 1, (uint Start, uint End)? loop = null)
    {
        var data = new byte[frames * channels * (bits / 8)];
        var ms = new MemoryStream();
        void W(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        void U32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); ms.Write(b); }
        void U16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); ms.Write(b); }
        W("RIFF"); U32(0); W("WAVE");
        W("fmt "); U32(16); U16((ushort)format); U16((ushort)channels); U32((uint)rate);
        U32((uint)(rate * channels * bits / 8)); U16((ushort)(channels * bits / 8)); U16((ushort)bits);
        if (loop is { } l)
        {
            W("smpl"); U32(36 + 24);
            for (var i = 0; i < 7; i++) U32(0);
            U32(1); U32(0);
            U32(0); U32(0); U32(l.Start); U32(l.End); U32(0); U32(0);
        }
        W("data"); U32((uint)data.Length); ms.Write(data);
        var bytes = ms.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        return bytes;
    }
}

public sealed class WavInfoTests
{
    [Fact]
    public void Pcm_header_duration_and_loop_are_read()
    {
        var info = WavInfo.Read(WavFixtures.Wav(channels: 2, rate: 44100, frames: 22050, loop: (100, 2000)));
        Assert.True(info.Ok, info.Error);
        Assert.Equal(2, info.Channels);
        Assert.Equal(44100, info.SampleRate);
        Assert.Equal(0.5, info.Duration.TotalSeconds, 3);
        Assert.Equal(new WavLoop(100, 2000), Assert.Single(info.Loops));
        Assert.Contains("stereo", info.Description);
        Assert.Contains("loop 100–2000", info.Description);
    }

    [Fact]
    public void Unsupported_and_foreign_formats_get_clear_messages()
    {
        var adpcm = WavInfo.Read(WavFixtures.Wav(format: 2, bits: 4));
        Assert.False(adpcm.Ok);
        Assert.Contains("MS ADPCM belum didukung", adpcm.Error);
        Assert.Contains("Ogg Vorbis", WavInfo.Read("OggS\0\0\0\0"u8).Error);
        Assert.Contains("Bukan berkas WAV", WavInfo.Read("ID3\u0004"u8).Error);
        var truncated = WavFixtures.Wav()[..30];
        Assert.False(WavInfo.Read(truncated).Ok);
    }

    [Fact]
    public void Differences_are_reported_without_blocking()
    {
        var old = WavInfo.Read(WavFixtures.Wav(loop: (0, 10)));
        var neu = WavInfo.Read(WavFixtures.Wav(channels: 2, rate: 44100));
        var d = WavInfo.Differences(old, neu);
        Assert.Contains(d, x => x.Contains("kanal"));
        Assert.Contains(d, x => x.Contains("Laju sampel"));
        Assert.Contains(d, x => x.Contains("loop"));
    }
}
