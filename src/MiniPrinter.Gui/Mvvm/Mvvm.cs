using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MiniPrinter.Gui;

/// <summary>Base of the view models: change notification without a framework.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Sets the field and raises the change only when the value is different.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>Runs work on the UI thread; the tests use <see cref="ImmediateDispatcher"/>.</summary>
public interface IUiDispatcher
{
    /// <summary>Queues <paramref name="action"/> on the UI thread (or runs it now when already there).</summary>
    void Post(Action action);
}

/// <summary>Runs everything where it is called: for tests and for code that is already on the UI thread.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public static readonly ImmediateDispatcher Instance = new();

    public void Post(Action action) => action();
}

/// <summary>A command that runs a synchronous action.</summary>
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A command that runs an asynchronous action. While it runs it cannot be started again, and exceptions are handed to
/// <see cref="Failed"/> instead of crashing the UI thread (the usual fate of an <c>async void</c> handler).
/// </summary>
public sealed class AsyncCommand : ObservableObject, ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isRunning;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public AsyncCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>Raised with the exception when the action fails.</summary>
    public event Action<Exception>? Failed;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
                RaiseCanExecuteChanged();
        }
    }

    public bool CanExecute(object? parameter) => !IsRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    /// <summary>Runs the action and completes when it has finished (tests await this).</summary>
    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
            return;
        IsRunning = true;
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex) when (Failed is not null)
        {
            Failed(ex);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
