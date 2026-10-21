using System;

namespace Adamantium.MVVM;

/// <summary>The MVVM generator describes the public properties of this class and of every class derived from it for
/// <see cref="PropertyAccessors"/>, as it does for every class with <see cref="System.ComponentModel.INotifyPropertyChanged"/>:
/// bindings then reach them without reflection. For a base whose objects are bound to but do not notify through
/// INotifyPropertyChanged - the UI's components.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class DescribePropertiesAttribute : Attribute
{
}
