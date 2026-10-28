using System.IO;
using System.Windows.Media;
using RailNexus.App.Core;

namespace RailNexus.App;

/// <summary>
/// Pemutar preview memakai MediaPlayer WPF. Isi WAV ditulis ke folder sementara milik RailNexus karena
/// MediaPlayer membutuhkan URI berkas; berkas aset tidak pernah diputar langsung dari folder edit.
/// </summary>
public sealed class WpfAudioPlayer : IAudioPlayer
{
    private readonly MediaPlayer _player = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RailNexus", "preview");
    private string? _current;

    public WpfAudioPlayer() => _player.MediaEnded += (_, _) => Stop();

    public string? NowPlaying { get; private set; }

    public string? Play(string label, byte[] wav, double volume)
    {
        Stop();
        try
        {
            Directory.CreateDirectory(_folder);
            _current = Path.Combine(_folder, Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(_current, wav);
            _player.Open(new Uri(_current));
            _player.Volume = volume;
            _player.Play();
            NowPlaying = label;
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"Preview tidak dapat diputar: {ex.Message}";
        }
    }

    public void Stop()
    {
        _player.Stop();
        _player.Close();
        NowPlaying = null;
        if (_current is not null)
        {
            try { File.Delete(_current); } catch (IOException) { } // dapat masih terkunci sesaat; dibersihkan di sesi berikutnya
            _current = null;
        }
    }
}
