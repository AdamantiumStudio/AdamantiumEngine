using System;
using System.Collections.Generic;
using Adamantium.ECS.Components;

namespace Adamantium.ECS;

/// <summary>The components of a scene by type: what a service asks for instead of walking every tree of the scene. Each
/// type's components are packed in an array of their own, so a selection costs as many steps as there are components of
/// the type, and adding or taking one out costs the same however many there are. Kept as trees come into the scene and
/// leave it, and as components are added to its entities and taken off; on the world's thread. The scene's cameras
/// (<see cref="Scene.Cameras"/>) are kept from it.</summary>
public sealed class SceneComponents
{
    private readonly Scene _scene;
    private readonly Dictionary<Type, ComponentPool> _pools = new();
    private readonly Dictionary<Type, ComponentPool[]> _poolsOf = new();
    private readonly List<ComponentPool> _gapped = [];
    private readonly EventHandler<EntityComponentEventArgs> _componentsChanged;
    private int _walks;

    internal SceneComponents(Scene scene)
    {
        _scene = scene;
        _componentsChanged = OnComponentsChanged;
    }

    /// <summary>The components of type <typeparamref name="T"/>, or derived from it, on the scene's entities that are
    /// enabled in their tree.</summary>
    public ComponentSelection<T> Of<T>() where T : class, IComponent => new(this, GetPoolsOf(typeof(T)), [], -1);

    /// <summary>The components of <paramref name="query"/>'s type on the scene's entities enabled in their tree that meet
    /// its conditions; walks the type of the fewest components and asks each entity for the others.</summary>
    public ComponentSelection<T> Select<T>(ComponentQuery<T> query) where T : class, IComponent
    {
        ArgumentNullException.ThrowIfNull(query);
        var requirements = query.Requirements;
        var pools = GetPoolsOf(typeof(T));
        var fewest = Count(pools);
        var walked = -1;
        for (var i = 0; i < requirements.Length; i++)
        {
            var required = GetPoolsOf(requirements[i].Type);
            var count = Count(required);
            if (count < fewest)
            {
                pools = required;
                fewest = count;
                walked = i;
            }
        }

        return new ComponentSelection<T>(this, pools, requirements, walked);
    }

    /// <summary>How many components of type <typeparamref name="T"/>, or derived from it, the scene holds - its disabled
    /// entities' among them, those taken out during a walk not.</summary>
    public int CountOf<T>() where T : class, IComponent => Count(GetPoolsOf(typeof(T)));

    // Puts in the tree under root: its entities, their components, and from now on what is added to them.
    internal void Join(Entity root)
    {
        var stack = Entity.StackPool.Get();
        try
        {
            stack.Push(root);
            while (stack.Count > 0)
            {
                var entity = stack.Pop();
                JoinOne(entity);
                PushChildren(stack, entity);
            }
        }
        finally
        {
            Entity.StackPool.Return(stack);
        }
    }

    // Takes out the tree under root.
    internal void Leave(Entity root)
    {
        var stack = Entity.StackPool.Get();
        try
        {
            stack.Push(root);
            while (stack.Count > 0)
            {
                var entity = stack.Pop();
                if (entity.ComponentIndex == this)
                    LeaveOne(entity);

                PushChildren(stack, entity);
            }
        }
        finally
        {
            Entity.StackPool.Return(stack);
        }
    }

    // Takes out every tree of the scene, as the world is reset.
    internal void Clear()
    {
        foreach (var root in _scene.RootEntities.AsSpan())
        {
            if (root != null)
                Leave(root);
        }

        _pools.Clear();
        _poolsOf.Clear();
        _gapped.Clear();
        _scene.Cameras.Clear();
    }

    internal void BeginWalk() => _walks++;

    internal void EndWalk()
    {
        if (_walks == 0)
            return;

        _walks--;
        if (_walks == 0)
            CloseGaps();
    }

    /// <summary>Ends the walks a frame left behind - an enumerator never disposed - so the gaps they kept open close; at
    /// the world's sync, between frames, when no walk goes on.</summary>
    internal void EndFrame()
    {
        if (_walks == 0)
            return;

        _walks = 0;
        CloseGaps();
    }

    private void CloseGaps()
    {
        foreach (var pool in _gapped)
        {
            pool.CloseGaps();
        }

        _gapped.Clear();
    }

    private void JoinOne(Entity entity)
    {
        if (entity.ComponentIndex == this)
            return;

        if (entity.ComponentIndex is { } other)
            other.LeaveOne(entity);

        entity.ComponentIndex = this;
        entity.ComponentsChanged += _componentsChanged;
        foreach (var component in entity.Components.AsSpan())
        {
            if (component != null)
                Add(component, entity);
        }
    }

    private void LeaveOne(Entity entity)
    {
        entity.ComponentsChanged -= _componentsChanged;
        entity.ComponentIndex = null;
        foreach (var component in entity.Components.AsSpan())
        {
            if (component != null)
                Remove(component);
        }

        if (entity.GetComponent<CameraBase>() != null)
            _scene.Cameras.Remove(entity);
    }

    private void OnComponentsChanged(object sender, EntityComponentEventArgs change)
    {
        var entity = change.ComponentOwner;
        if (entity.ComponentIndex != this)
            return;

        if (change.OldComponent != null)
        {
            Remove(change.OldComponent);
            if (change.OldComponent is CameraBase && entity.GetComponent<CameraBase>() == null)
                _scene.Cameras.Remove(entity);
        }

        if (change.NewComponent != null)
            Add(change.NewComponent, entity);
    }

    private void Add(IComponent component, Entity entity)
    {
        var type = component.GetType();
        if (!_pools.TryGetValue(type, out var pool))
        {
            pool = new ComponentPool();
            _pools.Add(type, pool);
            _poolsOf.Clear();
        }

        pool.Add(component, entity);
        if (component is CameraBase)
            _scene.Cameras.Add(entity);
    }

    private void Remove(IComponent component)
    {
        if (!_pools.TryGetValue(component.GetType(), out var pool))
            return;

        var hadGaps = pool.HasGaps;
        if (pool.Remove(component, _walks > 0) && _walks > 0 && !hadGaps)
            _gapped.Add(pool);
    }

    private ComponentPool[] GetPoolsOf(Type type)
    {
        if (_poolsOf.TryGetValue(type, out var pools))
            return pools;

        List<ComponentPool> found = [];
        foreach (var pair in _pools)
        {
            if (type.IsAssignableFrom(pair.Key))
                found.Add(pair.Value);
        }

        pools = [.. found];
        _poolsOf[type] = pools;
        return pools;
    }

    private static int Count(ComponentPool[] pools)
    {
        var count = 0;
        foreach (var pool in pools)
        {
            count += pool.Live;
        }

        return count;
    }

    private static void PushChildren(Stack<Entity> stack, Entity entity)
    {
        foreach (var child in entity.Dependencies.AsSpan())
        {
            if (child != null)
                stack.Push(child);
        }
    }
}
