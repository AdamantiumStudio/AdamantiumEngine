using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.Commands;

namespace Adamantium.MVVM;

/// <summary>An <see cref="AdamantiumAsyncCommand"/> with a typed parameter: the UI binds it untyped through
/// <see cref="IAsyncCommand"/>, code calls <see cref="ExecuteAsync(T, CancellationToken)"/>.</summary>
public sealed class AdamantiumAsyncCommand<T> : IAsyncCommand
{
    private readonly Func<T, CancellationToken, Task> _execute;
    private readonly Func<T, bool> _canExecute;
    private readonly AsyncCommandRuns _runs;

    public AdamantiumAsyncCommand(Func<T, CancellationToken, Task> execute, Func<T, bool> canExecute = null,
        bool allowsConcurrentRuns = false)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _runs = new AsyncCommandRuns(allowsConcurrentRuns, OnRunningChanged);
    }

    public bool IsRunning => _runs.IsRunning;

    /// <summary>True when a press while the command runs starts another run instead of being ignored.</summary>
    public bool AllowsConcurrentRuns => _runs.AllowsConcurrentRuns;

    public bool CanExecute(T parameter) => !_runs.Blocks && (_canExecute == null || _canExecute(parameter));

    // Fire-and-forget for the UI click path; real exceptions surface on the UI thread (not swallowed).
    public async void Execute(T parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(T parameter, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameter)) return;

        var run = _runs.Start(cancellationToken);
        try
        {
            await _execute(parameter, run.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when Cancel() (or the caller's token) fires — cancellation is not an error.
        }
        finally
        {
            _runs.End(run);
        }
    }

    /// <summary>Cancels every run under way.</summary>
    public void Cancel() => _runs.CancelAll();

    bool ICommand.CanExecute(object parameter) => CanExecute(Coerce(parameter));

    void ICommand.Execute(object parameter) => Execute(Coerce(parameter));

    Task IAsyncCommand.ExecuteAsync(object parameter, CancellationToken cancellationToken) =>
        ExecuteAsync(Coerce(parameter), cancellationToken);

    public event EventHandler CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnRunningChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        RaiseCanExecuteChanged();   // disable-while-running: bound controls re-query CanExecute
    }

    private static T Coerce(object parameter) => parameter is T value ? value : default;
}
