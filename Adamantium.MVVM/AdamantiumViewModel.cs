using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Adamantium.MVVM;

/// <summary>
/// Base class for view-models. Provides <see cref="INotifyPropertyChanged"/> + <c>SetProperty</c>/<c>RaisePropertyChanged</c>,
/// which the MVVM source generator's <c>[Bindable]</c> properties build on. Derive from this; use <c>[ViewModel]</c>
/// only when you must keep a different base class.
/// </summary>
public abstract class AdamantiumViewModel : INotifyPropertyChanged
{
    /// <summary>Raised when a property of this view-model changes.</summary>
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>Sets <paramref name="field"/> and raises the change if the value differs; true if it did.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for <paramref name="propertyName"/>.</summary>
    protected virtual void RaisePropertyChanged([CallerMemberName] string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
