using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The code tree of a Huffman coded array, rebuilt from its leaves' codes.</summary>
internal sealed class PrcHuffmanTree
{
    /// <summary>The child a node has none of.</summary>
    internal const int NoChild = -1;

    /// <summary>The longest code a leaf may have.</summary>
    internal const int LongestCode = 32;

    private readonly List<int> _zeroChild = [NoChild];
    private readonly List<int> _oneChild = [NoChild];
    private readonly List<int> _leafValue = [0];
    private readonly List<bool> _isLeaf = [false];

    internal int ShortestCode { get; private set; } = LongestCode;

    internal bool IsLeaf(int node) => _isLeaf[node];

    internal int GetChild(int node, bool bit) => bit ? _oneChild[node] : _zeroChild[node];

    internal int GetValue(int node) => _leafValue[node];

    internal bool Add(uint code, int length, int value)
    {
        if (length == 0)
        {
            if (_zeroChild[0] != NoChild || _oneChild[0] != NoChild || _isLeaf[0])
            {
                return false;
            }

            _isLeaf[0] = true;
            _leafValue[0] = value;
            ShortestCode = 0;
            return true;
        }

        var node = 0;
        for (var bit = length - 1; bit >= 0; bit--)
        {
            if (_isLeaf[node])
            {
                return false;
            }

            var one = ((code >> bit) & 1) != 0;
            var next = GetChild(node, one);
            if (next == NoChild)
            {
                next = _isLeaf.Count;
                _zeroChild.Add(NoChild);
                _oneChild.Add(NoChild);
                _leafValue.Add(0);
                _isLeaf.Add(false);
                if (one)
                {
                    _oneChild[node] = next;
                }
                else
                {
                    _zeroChild[node] = next;
                }
            }
            else if (bit == 0)
            {
                return false;
            }

            node = next;
        }

        _isLeaf[node] = true;
        _leafValue[node] = value;
        ShortestCode = Math.Min(ShortestCode, length);
        return true;
    }
}
