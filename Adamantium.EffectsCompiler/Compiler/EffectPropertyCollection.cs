using System;
using System.Collections.Generic;
using Adamantium.Core;

namespace Adamantium.EffectsCompiler;

public class EffectPropertyCollection : Dictionary<string, object>
{
    public void SetProperty<T>(PropertyKey<T> key, T value)
    {
        if (value is Enum)
        {
            Add(key.Name, Convert.ChangeType(value, Enum.GetUnderlyingType(typeof(T))));
        }
        else
        {
            Add(key.Name, value);
        }
    }

    public EffectPropertyCollection Clone()
    {
        return (EffectPropertyCollection) MemberwiseClone();
    }
}