using System;
using System.Collections.Generic;
using Adamantium.ECS;

namespace Adamantium.Engine;

/// <summary>
/// What the editor has selected: one entity or several, one of them current - the last one chosen. It outlives any tool:
/// switching tools keeps it. Each selected entity is marked <see cref="Entity.IsSelected"/>.
/// </summary>
public class Selection
{
    private readonly object sync = new();
    private Entity[] items = [];
    private Entity current;

    /// <summary>The entity chosen last, what one-entity views show; setting it selects it alone, null nothing.</summary>
    public Entity Current
    {
        get => current;
        set => Select(value == null ? [] : [value]);
    }

    /// <summary>Everything selected, the current one last; a new list on every change.</summary>
    public IReadOnlyList<Entity> Items => items;

    public bool Contains(Entity entity) => Array.IndexOf(items, entity) >= 0;

    /// <summary>What a click or a frame on <paramref name="part"/> of a model's form selects: the model it belongs to
    /// (<see cref="ModelOf"/>) first; once anything of that model - the model itself or a part of it - is selected,
    /// the part itself, so the parts are reached one click further. Null for null.</summary>
    public Entity ChosenByClick(Entity part)
    {
        var model = ModelOf(part);
        return model == null || ReferenceEquals(model, part) || HoldsAnyOf(model) ? part : model;
    }

    /// <summary>What a click taking something out of the selection on <paramref name="part"/> of a model's form takes
    /// out: the part when it is selected, else the nearest selected entity it is a part of, else what a click would
    /// select. Null for null.</summary>
    public Entity ChosenToRemove(Entity part)
    {
        for (var owner = part; owner != null; owner = owner.Owner)
        {
            if (Contains(owner))
            {
                return owner;
            }
        }

        return ChosenByClick(part);
    }

    /// <summary>The model <paramref name="part"/> belongs to: the nearest of it and its owners made from a file (with a
    /// <see cref="Entity.Source"/>), as an import makes a model's root; null for one in no such model - made in the scene,
    /// or a group of them.</summary>
    public static Entity ModelOf(Entity part)
    {
        for (var owner = part; owner != null; owner = owner.Owner)
        {
            if (owner.Source != null)
            {
                return owner;
            }
        }

        return null;
    }

    // Whether the model or anything under it is selected.
    private bool HoldsAnyOf(Entity model)
    {
        var held = items;
        for (int i = 0; i < held.Length; i++)
        {
            for (var owner = held[i]; owner != null; owner = owner.Owner)
            {
                if (ReferenceEquals(owner, model))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Selects these and nothing else; the last becomes current.</summary>
    public void Select(IReadOnlyList<Entity> entities)
    {
        Change(() =>
        {
            var chosen = new List<Entity>(entities.Count);
            AddNew(chosen, entities);
            return chosen;
        });
    }

    /// <summary>Adds these to what is selected; the last of them becomes current.</summary>
    public void Add(IReadOnlyList<Entity> entities)
    {
        Change(() =>
        {
            var chosen = new List<Entity>(items.Length + entities.Count);
            KeepAllBut(chosen, entities);
            AddNew(chosen, entities);
            return chosen;
        });
    }

    /// <summary>Takes these out of what is selected; the last of the rest stays or becomes current.</summary>
    public void Remove(IReadOnlyList<Entity> entities)
    {
        Change(() =>
        {
            var chosen = new List<Entity>(items.Length);
            KeepAllBut(chosen, entities);
            return chosen;
        });
    }

    public event EventHandler Changed;

    private void KeepAllBut(List<Entity> chosen, IReadOnlyList<Entity> entities)
    {
        for (int i = 0; i < items.Length; i++)
        {
            if (!Contains(entities, items[i]))
            {
                chosen.Add(items[i]);
            }
        }
    }

    private static void AddNew(List<Entity> chosen, IReadOnlyList<Entity> entities)
    {
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] != null && !chosen.Contains(entities[i]))
            {
                chosen.Add(entities[i]);
            }
        }
    }

    private void Change(Func<List<Entity>> choose)
    {
        lock (sync)
        {
            var chosen = choose();
            if (Same(chosen))
            {
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (!chosen.Contains(items[i]))
                {
                    items[i].IsSelected = false;
                }
            }

            for (int i = 0; i < chosen.Count; i++)
            {
                chosen[i].IsSelected = true;
            }

            items = chosen.ToArray();
            current = items.Length > 0 ? items[^1] : null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool Same(List<Entity> chosen)
    {
        if (chosen.Count != items.Length)
        {
            return false;
        }

        for (int i = 0; i < chosen.Count; i++)
        {
            if (chosen[i] != items[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(IReadOnlyList<Entity> entities, Entity entity)
    {
        for (int i = 0; i < entities.Count; i++)
        {
            if (entities[i] == entity)
            {
                return true;
            }
        }

        return false;
    }
}
