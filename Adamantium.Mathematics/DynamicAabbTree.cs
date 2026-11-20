using System;
using System.Collections.Generic;
using System.Numerics;

namespace Adamantium.Mathematics;

/// <summary>
/// A bounding volume hierarchy of boxes that move. Each item stands in a leaf whose box is its own grown by a margin, so
/// a small move leaves the tree as it is and a larger one takes the leaf out and puts it back where it adds the least
/// surface; rotations keep the tree balanced. A ray visits only the boxes it passes through. Not thread-safe.
/// </summary>
/// <typeparam name="TItem">What each leaf holds.</typeparam>
/// <typeparam name="TNumber">The boxes' numbers: float for a scene, double for a world far from its origin.</typeparam>
public sealed class DynamicAabbTree<TItem, TNumber> where TNumber : unmanaged, IFloatingPointIeee754<TNumber>
{
    private const int None = -1;
    private static readonly TNumber Two = TNumber.CreateChecked(2);
    private static readonly TNumber Four = TNumber.CreateChecked(4);

    private Node[] nodes = new Node[16];
    private int allocated;
    private int free = None;
    private int root = None;
    private int[] stack = new int[64];

    /// <param name="margin">How far a leaf's box reaches past its item's, as a share of the item's size on each axis.</param>
    /// <param name="minimumMargin">The least it reaches past, so a flat or tiny item still has room to move.</param>
    public DynamicAabbTree(TNumber margin, TNumber minimumMargin)
    {
        Margin = margin;
        MinimumMargin = minimumMargin;
    }

    /// <summary>A tree whose leaves reach a tenth of their item's size past it, and at least a centimeter.</summary>
    public DynamicAabbTree() : this(TNumber.CreateChecked(0.1), TNumber.CreateChecked(0.01))
    {
    }

    /// <summary>How far a leaf's box reaches past its item's, as a share of the item's size on each axis.</summary>
    public TNumber Margin { get; }

    /// <summary>The least a leaf's box reaches past its item's.</summary>
    public TNumber MinimumMargin { get; }

    /// <summary>How many items the tree holds.</summary>
    public int Count { get; private set; }

    /// <summary>How many levels the tree has; 0 when it is empty.</summary>
    public int Height => root == None ? 0 : nodes[root].Height + 1;

    /// <summary>Puts <paramref name="item"/> in the tree with <paramref name="box"/>; gives the handle that moves and
    /// removes it.</summary>
    public int Add(in BoundingBox<TNumber> box, TItem item)
    {
        var leaf = Allocate();
        nodes[leaf].Box = Grown(box);
        nodes[leaf].Item = item;
        nodes[leaf].Height = 0;
        InsertLeaf(leaf);
        Count++;
        return leaf;
    }

    /// <summary>Takes the item of <paramref name="handle"/> out of the tree.</summary>
    public void Remove(int handle)
    {
        RemoveLeaf(handle);
        Release(handle);
        Count--;
    }

    /// <summary>Gives the item of <paramref name="handle"/> its new box; gives whether its leaf had to move: once the box
    /// leaves the leaf's, or the leaf's grows far larger than the box needs.</summary>
    public bool Move(int handle, in BoundingBox<TNumber> box)
    {
        var grown = Grown(box);
        var leaf = nodes[handle].Box;
        if (leaf.Contains(box) == ContainmentType.Contains)
        {
            var reach = (grown.Maximum - box.Maximum) * Four;
            var largest = new BoundingBox<TNumber>(grown.Minimum - reach, grown.Maximum + reach);
            if (largest.Contains(leaf) == ContainmentType.Contains)
            {
                return false;
            }
        }

        RemoveLeaf(handle);
        nodes[handle].Box = grown;
        InsertLeaf(handle);
        return true;
    }

    /// <summary>The item of <paramref name="handle"/>.</summary>
    public TItem ItemOf(int handle) => nodes[handle].Item;

    /// <summary>The box the leaf of <paramref name="handle"/> has: its item's grown by the margin.</summary>
    public BoundingBox<TNumber> BoxOf(int handle) => nodes[handle].Box;

    /// <summary>Adds to <paramref name="into"/> every item whose leaf <paramref name="ray"/> enters within
    /// <paramref name="maxEntry"/> lengths of its direction, with where it enters; 0 when it starts inside.</summary>
    public void RayCast(in Ray<TNumber> ray, List<(TItem Item, TNumber Entry)> into, TNumber maxEntry)
    {
        if (root == None)
        {
            return;
        }

        var count = 0;
        stack[count++] = root;
        while (count > 0)
        {
            var index = stack[--count];
            if (!Collision.RayIntersectsBox(ray, nodes[index].Box, out TNumber entry) || entry > maxEntry)
            {
                continue;
            }

            if (nodes[index].IsLeaf)
            {
                into.Add((nodes[index].Item, entry));
                continue;
            }

            if (count + 2 > stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[count++] = nodes[index].Child1;
            stack[count++] = nodes[index].Child2;
        }
    }

    /// <summary>Adds to <paramref name="into"/> every item whose leaf <paramref name="ray"/> enters, however far.
    /// </summary>
    public void RayCast(in Ray<TNumber> ray, List<(TItem Item, TNumber Entry)> into) =>
        RayCast(ray, into, TNumber.PositiveInfinity);

    /// <summary>Adds to <paramref name="into"/> every item whose leaf box overlaps <paramref name="box"/>, faces
    /// touching included.</summary>
    public void Query(in BoundingBox<TNumber> box, List<TItem> into)
    {
        if (root == None)
        {
            return;
        }

        var count = 0;
        stack[count++] = root;
        while (count > 0)
        {
            var index = stack[--count];
            if (!Collision.BoxIntersectsBox(nodes[index].Box, box))
            {
                continue;
            }

            if (nodes[index].IsLeaf)
            {
                into.Add(nodes[index].Item);
                continue;
            }

            if (count + 2 > stack.Length)
            {
                Array.Resize(ref stack, stack.Length * 2);
            }

            stack[count++] = nodes[index].Child1;
            stack[count++] = nodes[index].Child2;
        }
    }

    private BoundingBox<TNumber> Grown(in BoundingBox<TNumber> box)
    {
        var size = box.Maximum - box.Minimum;
        var reach = Vector3<TNumber>.Max(size * Margin, new Vector3<TNumber>(MinimumMargin));
        return new BoundingBox<TNumber>(box.Minimum - reach, box.Maximum + reach);
    }

    private int Allocate()
    {
        if (free == None)
        {
            if (allocated == nodes.Length)
            {
                Array.Resize(ref nodes, nodes.Length * 2);
            }

            nodes[allocated] = new Node { Parent = None, Child1 = None, Child2 = None, Height = 0 };
            return allocated++;
        }

        var node = free;
        free = nodes[node].Parent;
        nodes[node] = new Node { Parent = None, Child1 = None, Child2 = None, Height = 0 };
        return node;
    }

    private void Release(int node)
    {
        nodes[node] = new Node { Parent = free, Child1 = None, Child2 = None, Height = -1 };
        free = node;
    }

    private void InsertLeaf(int leaf)
    {
        if (root == None)
        {
            root = leaf;
            nodes[leaf].Parent = None;
            return;
        }

        var sibling = BestSibling(nodes[leaf].Box);
        var oldParent = nodes[sibling].Parent;
        var parent = Allocate();
        nodes[parent].Parent = oldParent;
        nodes[parent].Box = BoundingBox.CreateMerged(nodes[leaf].Box, nodes[sibling].Box);
        nodes[parent].Height = nodes[sibling].Height + 1;
        nodes[parent].Child1 = sibling;
        nodes[parent].Child2 = leaf;
        nodes[sibling].Parent = parent;
        nodes[leaf].Parent = parent;

        if (oldParent == None)
        {
            root = parent;
        }
        else if (nodes[oldParent].Child1 == sibling)
        {
            nodes[oldParent].Child1 = parent;
        }
        else
        {
            nodes[oldParent].Child2 = parent;
        }

        Refit(nodes[leaf].Parent);
    }

    // Down from the root, each step to the child whose box grows the least by the leaf, until stopping here is cheaper.
    private int BestSibling(in BoundingBox<TNumber> box)
    {
        var index = root;
        while (!nodes[index].IsLeaf)
        {
            var child1 = nodes[index].Child1;
            var child2 = nodes[index].Child2;
            var area = Area(nodes[index].Box);
            var combinedArea = Area(BoundingBox.CreateMerged(nodes[index].Box, box));
            var cost = Two * combinedArea;
            var inheritance = Two * (combinedArea - area);
            var cost1 = DescentCost(child1, box) + inheritance;
            var cost2 = DescentCost(child2, box) + inheritance;
            if (cost < cost1 && cost < cost2)
            {
                break;
            }

            index = cost1 < cost2 ? child1 : child2;
        }

        return index;
    }

    private TNumber DescentCost(int child, in BoundingBox<TNumber> box)
    {
        var merged = Area(BoundingBox.CreateMerged(nodes[child].Box, box));
        return nodes[child].IsLeaf ? merged : merged - Area(nodes[child].Box);
    }

    private void RemoveLeaf(int leaf)
    {
        if (leaf == root)
        {
            root = None;
            return;
        }

        var parent = nodes[leaf].Parent;
        var grandParent = nodes[parent].Parent;
        var sibling = nodes[parent].Child1 == leaf ? nodes[parent].Child2 : nodes[parent].Child1;
        if (grandParent == None)
        {
            root = sibling;
            nodes[sibling].Parent = None;
        }
        else
        {
            if (nodes[grandParent].Child1 == parent)
            {
                nodes[grandParent].Child1 = sibling;
            }
            else
            {
                nodes[grandParent].Child2 = sibling;
            }

            nodes[sibling].Parent = grandParent;
            Refit(grandParent);
        }

        Release(parent);
        nodes[leaf].Parent = None;
    }

    private void Refit(int index)
    {
        while (index != None)
        {
            index = Balance(index);
            var child1 = nodes[index].Child1;
            var child2 = nodes[index].Child2;
            nodes[index].Height = 1 + Math.Max(nodes[child1].Height, nodes[child2].Height);
            nodes[index].Box = BoundingBox.CreateMerged(nodes[child1].Box, nodes[child2].Box);
            index = nodes[index].Parent;
        }
    }

    // When one child is more than a level taller than the other, its taller child takes the node's place and the node
    // takes its shorter one; gives the node now standing where the given one stood.
    private int Balance(int a)
    {
        if (nodes[a].IsLeaf || nodes[a].Height < 2)
        {
            return a;
        }

        var b = nodes[a].Child1;
        var c = nodes[a].Child2;
        var balance = nodes[c].Height - nodes[b].Height;
        if (balance > 1)
        {
            return RotateUp(a, c, b, rightSide: true);
        }

        if (balance < -1)
        {
            return RotateUp(a, b, c, rightSide: false);
        }

        return a;
    }

    // `up` is the taller child of `a`, `other` the shorter; `up`'s taller child stays with it and `a` takes the shorter.
    private int RotateUp(int a, int up, int other, bool rightSide)
    {
        var f = nodes[up].Child1;
        var g = nodes[up].Child2;

        nodes[up].Child1 = a;
        nodes[up].Parent = nodes[a].Parent;
        nodes[a].Parent = up;

        var parent = nodes[up].Parent;
        if (parent == None)
        {
            root = up;
        }
        else if (nodes[parent].Child1 == a)
        {
            nodes[parent].Child1 = up;
        }
        else
        {
            nodes[parent].Child2 = up;
        }

        var (taller, shorter) = nodes[f].Height > nodes[g].Height ? (f, g) : (g, f);
        nodes[up].Child2 = taller;
        if (rightSide)
        {
            nodes[a].Child2 = shorter;
        }
        else
        {
            nodes[a].Child1 = shorter;
        }

        nodes[shorter].Parent = a;
        nodes[a].Box = BoundingBox.CreateMerged(nodes[other].Box, nodes[shorter].Box);
        nodes[a].Height = 1 + Math.Max(nodes[other].Height, nodes[shorter].Height);
        nodes[up].Box = BoundingBox.CreateMerged(nodes[a].Box, nodes[taller].Box);
        nodes[up].Height = 1 + Math.Max(nodes[a].Height, nodes[taller].Height);
        return up;
    }

    private static TNumber Area(in BoundingBox<TNumber> box)
    {
        var size = box.Maximum - box.Minimum;
        return Two * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
    }

    private struct Node
    {
        public BoundingBox<TNumber> Box;
        public TItem Item;
        public int Parent;
        public int Child1;
        public int Child2;
        public int Height;

        public readonly bool IsLeaf => Child1 == None;
    }
}
