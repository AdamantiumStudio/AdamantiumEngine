using System.Runtime.Intrinsics;

namespace Adamantium.Mathematics.Typed;

/// <summary>A row of four doubles as two 128-bit registers, for processors without 256-bit SIMD.</summary>
internal readonly record struct DoubleRowHalves(Vector128<double> Low, Vector128<double> High);
