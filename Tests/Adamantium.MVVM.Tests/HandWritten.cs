using System.ComponentModel;

namespace Adamantium.MVVM.Tests;

/// <summary>A notifying class written by hand, with no MVVM attributes: the generator still describes its
/// properties.</summary>
public sealed class HandWritten : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public int Count { get; set; }

    public string Created { get; init; } = "now";

    public int @class { get; set; }

    public string Written { get; private set; }

    public string WriteOnly
    {
        set => Written = value;
    }

    public void Touch() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
}
