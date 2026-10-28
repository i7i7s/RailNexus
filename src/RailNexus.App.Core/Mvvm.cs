using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace RailNexus.App.Core;

/// <summary>Dasar ViewModel tanpa ketergantungan WPF, agar dapat diuji di semua platform.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Perintah asinkron yang tidak dapat dijalankan ganda. Pengecualian tidak ditelan: ViewModel
/// pemanggil bertanggung jawab mengubahnya menjadi pesan untuk pengguna.
/// </summary>
public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;

    public bool IsRunning => _running;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        _running = true;
        Refresh();
        try { await execute(); }
        finally
        {
            _running = false;
            Refresh();
        }
    }

    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Perintah dengan parameter bertipe, mis. butir navigasi atau chip filter.</summary>
public sealed class ParamCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : System.Windows.Input.ICommand where T : class
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => parameter is T t && (canExecute?.Invoke(t) ?? true);
    public void Execute(object? parameter) { if (parameter is T t && CanExecute(t)) execute(t); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
