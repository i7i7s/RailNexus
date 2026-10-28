using System.Diagnostics;
using System.Text;

namespace RailNexus.Core.Process;

public interface IProcessRunner
{
    Task<RawOutput> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>
/// Menjalankan proses eksternal dengan argumen terpisah (aman untuk path berspasi)
/// dan batas waktu. Proses yang melewati batas waktu dihentikan dan ditandai TimedOut;
/// pemanggil wajib menganggap hasilnya belum pasti, bukan gagal.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<RawOutput> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // TrainzUtil menulis UTF-8 (bukti: printconfig-after-install.json memuat "—" yang rusak
            // saat dibaca dengan codepage konsol).
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var sw = Stopwatch.StartNew();
        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            timedOut = true;
            try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        }

        if (!timedOut) proc.WaitForExit(); // menguras sisa event output
        sw.Stop();

        return new RawOutput(
            executable,
            arguments.ToArray(),
            timedOut ? null : proc.ExitCode,
            stdout.ToString(),
            stderr.ToString(),
            sw.Elapsed,
            timedOut);
    }
}
