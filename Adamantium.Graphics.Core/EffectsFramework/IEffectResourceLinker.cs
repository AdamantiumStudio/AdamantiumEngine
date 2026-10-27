using System.Collections.Generic;
using Adamantium.EffectsCompiler;

namespace Adamantium.Graphics.Core.EffectsFramework;

public interface IEffectResourceLinker
{
    /// <summary>
    /// Initializes this instance.
    /// </summary>
    void Initialize();

    T GetResource<T>(EffectData.Parameter resourceName) where T : class;
    T[] GetResources<T>(EffectData.Parameter resourceName) where T : class;
    void SetResource<T>(EffectData.ResourceParameter paramDescription, T value);
    void SetResource<T>(EffectData.ResourceParameter resourceName, params T[] valueArray) where T : class;

    /// <summary>Binds <paramref name="values"/> to the elements of <paramref name="parameter"/> from
    /// <paramref name="index"/> on.</summary>
    void SetResource<T>(EffectData.ResourceParameter parameter, int index, params T[] values) where T : class;

    Dictionary<EffectData.Parameter, object> GetBoundResources();

    /// <summary>The resources bound to <paramref name="resource"/>, one per element (null where none is); null when
    /// nothing was ever bound to it.</summary>
    object[] GetBoundValues(EffectData.Parameter resource);

    void AddBoundResource(EffectData.Parameter resourceName, object value);
    int Count { get; set; }
}