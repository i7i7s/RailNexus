namespace RailNexus.Core;

/// <summary>
/// Hasil operasi terhadap Trainz. "Unknown" berbeda dari gagal: hasil yang tidak
/// dikenali atau timeout tidak boleh dianggap sukses (PRD 23.2, 21.3).
/// </summary>
public enum Outcome
{
    Success,
    Failed,
    Unknown,
    NotSupported,
    TrainzNotRunning,
}

public sealed record OpResult<T>(Outcome Outcome, T? Value, string Message, RawOutput? Raw = null)
{
    public bool IsSuccess => Outcome == Outcome.Success;

    public static OpResult<T> Ok(T value, RawOutput? raw = null, string message = "") => new(Outcome.Success, value, message, raw);
    public static OpResult<T> Fail(string message, RawOutput? raw = null) => new(Outcome.Failed, default, message, raw);
    public static OpResult<T> Unknown(string message, RawOutput? raw = null) => new(Outcome.Unknown, default, message, raw);
    public static OpResult<T> Unsupported(string message) => new(Outcome.NotSupported, default, message);
    public static OpResult<T> NotRunning(RawOutput? raw = null) =>
        new(Outcome.TrainzNotRunning, default, "Trainz belum berjalan. Buka Trainz lalu coba lagi.", raw);

    public OpResult<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? OpResult<TOut>.Ok(map(Value!), Raw, Message) : new(Outcome, default, Message, Raw);
}

/// <summary>Keluaran mentah sebuah proses, disimpan untuk corpus dan log.</summary>
public sealed record RawOutput(
    string Command,
    IReadOnlyList<string> Arguments,
    int? ExitCode,
    string StdOut,
    string StdErr,
    TimeSpan Duration,
    bool TimedOut);
