using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.Commands;

namespace Adamantium.MVVM;

/// <summary>An async command: <see cref="CanExecute"/> is false while it runs (<see cref="IsRunning"/>), <see cref="Cancel"/>
/// cancels the run, and the method's exceptions propagate while cancellation is swallowed. A command that allows
/// concurrent runs stays executable while it runs and starts each run at once; its method keeps them in order.</summary>
public sealed class AdamantiumAsyncCommand : IAsyncCommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool> _canExecute;
    private readonly AsyncCommandRuns _runs;

    public AdamantiumAsyncCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null,
        bool allowsConcurrentRuns = false)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _runs = new AsyncCommandRuns(allowsConcurrentRuns, OnRunningChanged);
    }

    public bool IsRunning => _runs.IsRunning;

    /// <summary>True when a press while the command runs starts another run instead of being ignored.</summary>
    public bool AllowsConcurrentRuns => _runs.AllowsConcurrentRuns;

    public bool CanExecute(object parameter = null) => !_runs.Blocks && (_canExecute == null || _canExecute());

    // Fire-and-forget for the UI click path; real exceptions surface on the UI thread (not swallowed).
    public async void Execute(object parameter = null) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object parameter = null, CancellationToken cancellationToken = default)
    {
        if (!CanExecute()) return;

        var run = _runs.Start(cancellationToken);
        try
        {
            await _execute(run.Token);
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

    public event EventHandler CanExecuteChanged;

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnRunningChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        RaiseCanExecuteChanged();   // disable-while-running: bound controls re-query CanExecute
    }
}
