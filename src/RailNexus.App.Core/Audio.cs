namespace RailNexus.App.Core;

/// <summary>
/// Pemutar preview. Volume preview tidak mengubah aset (PRD 14). Implementasi WPF ada di aplikasi;
/// di test dan platform lain dipakai pemutar palsu atau <see cref="NullAudioPlayer"/>.
/// </summary>
public interface IAudioPlayer
{
    /// <returns>null bila mulai diputar; selain itu alasan tidak dapat diputar.</returns>
    string? Play(string label, byte[] wav, double volume);
    void Stop();
    string? NowPlaying { get; }
}

public sealed class NullAudioPlayer : IAudioPlayer
{
    public string? Play(string label, byte[] wav, double volume) => "Pemutar audio tidak tersedia di lingkungan ini.";
    public void Stop() { }
    public string? NowPlaying => null;
}
