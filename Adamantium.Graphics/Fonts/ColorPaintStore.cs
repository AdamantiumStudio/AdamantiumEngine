using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Fonts;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts.Layout;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Fonts;

internal sealed class ColorPaintStore : IDisposable
{
    private const int InitialCapacity = 256;
    private const int MaxGroupDepth = 8;
    private const int MaxClipDepth = 8;
    private const float ClipCode = 1;
    private const float PopClipCode = 2;
    private const float GroupCode = 3;
    private const float PopGroupCode = 4;
    private const float FillCode = 5;
    private const float ImageCode = 6;
    private const int ImageProgramLength = 6;

    private readonly object _gate = new();
    private readonly Dictionary<ulong, int> _programs = new();
    private readonly List<Vector4F> _programData = [];
    private readonly List<Vector4F> _stopData = [];
    private Buffer<Vector4F> _programBuffer;
    private Buffer<Vector4F> _stopBuffer;
    private int _programsUploaded;
    private int _stopsUploaded;
    private IGraphicsDevice _device;
    private bool _disposed;

    public bool TryGetProgram(ulong colorGlyph, out int program)
    {
        lock (_gate)
        {
            return _programs.TryGetValue(colorGlyph, out program);
        }
    }

    public int GetProgram(ulong colorGlyph, IReadOnlyList<ColorPaintOperation> operations,
        IReadOnlyDictionary<uint, ColorPaintMask> masks, double unitsPerEm, uint fieldSize, RectangleF? clipBox)
    {
        lock (_gate)
        {
            if (_programs.TryGetValue(colorGlyph, out var known))
            {
                return known;
            }

            var program = Build(operations, masks, unitsPerEm, fieldSize, clipBox);
            _programs[colorGlyph] = program;
            return program;
        }
    }

    // An image glyph keeps its program for good: landed again after it was let go, it is written over its old one, so
    // text built before draws it again.
    public int AddImage(ulong colorGlyph, double left, double bottom, double width, double height, ColorBitmapCell cell)
    {
        lock (_gate)
        {
            var known = _programs.TryGetValue(colorGlyph, out var program);
            if (known && program < 0)
            {
                return program;
            }

            if (width <= 0 || height <= 0)
            {
                _programs[colorGlyph] = -1;
                return -1;
            }

            const float size = ColorBitmapAtlas.LayerSize;
            var scaleU = cell.Width / width / size;
            var scaleV = cell.Height / height / size;
            if (!known)
            {
                program = _programData.Count;
                CollectionsMarshal.SetCount(_programData, program + ImageProgramLength);
                _programs[colorGlyph] = program;
            }

            _programData[program] = new Vector4F((float)left, (float)bottom, (float)width, (float)height);
            _programData[program + 1] = new Vector4F(4, 0, 0, 0);
            _programData[program + 2] = new Vector4F(ImageCode, 4, cell.Layer, size);
            _programData[program + 3] = new Vector4F((float)scaleU, 0, 0, (float)-scaleV);
            _programData[program + 4] = new Vector4F((float)(cell.X / size - left * scaleU),
                (float)(cell.Y / size + (bottom + height) * scaleV), cell.Levels - 1, 0);
            _programData[program + 5] = new Vector4F(cell.X / size, cell.Y / size, (cell.X + cell.CellWidth) / size,
                (cell.Y + cell.CellHeight) / size);
            _programsUploaded = Math.Min(_programsUploaded, program);
            return program;
        }
    }

    // An image let go: its program draws nothing - an empty box - until the image lands again.
    public void ClearImage(int program)
    {
        lock (_gate)
        {
            _programData[program] = Vector4F.Zero;
            _programsUploaded = Math.Min(_programsUploaded, program);
        }
    }

    public void AddNone(ulong colorGlyph)
    {
        lock (_gate)
        {
            _programs.TryAdd(colorGlyph, -1);
        }
    }

    // A glyph whose image found no room even when it was let go and asked for again: drawn by none from now on.
    public void SetNone(ulong colorGlyph)
    {
        lock (_gate)
        {
            _programs[colorGlyph] = -1;
        }
    }

    public (ulong Programs, ulong Stops) Upload(IGraphicsDevice device)
    {
        lock (_gate)
        {
            if (_disposed || _programData.Count == 0)
            {
                return (0, 0);
            }

            _device = device;
            _programsUploaded = Send(device, ref _programBuffer, _programData, _programsUploaded);
            _stopsUploaded = Send(device, ref _stopBuffer, _stopData, _stopsUploaded);
            return (_programBuffer.GetDeviceAddress(), _stopBuffer.GetDeviceAddress());
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Retire(_programBuffer);
            Retire(_stopBuffer);
            _programBuffer = null;
            _stopBuffer = null;
        }
    }

    private int Build(IReadOnlyList<ColorPaintOperation> operations, IReadOnlyDictionary<uint, ColorPaintMask> masks,
        double unitsPerEm, uint fieldSize, RectangleF? clipBox)
    {
        var steps = new List<Vector4F>();
        var stopCount = _stopData.Count;
        var skippedGroups = new Stack<bool>();
        var openClips = new Stack<int>();
        var groupDepth = 0;
        var box = new Box();
        for (var i = 0; i < operations.Count; i++)
        {
            var operation = operations[i];
            switch (operation.Kind)
            {
                case ColorPaintOperationKind.PushClip:
                    if (openClips.Count >= MaxClipDepth)
                    {
                        i = MatchingPopClip(operations, i);
                        break;
                    }

                    openClips.Push(steps.Count);
                    Clip(steps, operation, masks[operation.GlyphIndex], unitsPerEm, fieldSize, ref box);
                    break;
                case ColorPaintOperationKind.PopClip:
                    var clip = openClips.Pop();
                    var head = steps[clip];
                    head.W = steps.Count + 1 - clip;
                    steps[clip] = head;
                    steps.Add(new Vector4F(PopClipCode, 1, 0, 0));
                    break;
                case ColorPaintOperationKind.PushGroup:
                    var deepGroup = groupDepth >= MaxGroupDepth;
                    skippedGroups.Push(deepGroup);
                    if (!deepGroup)
                    {
                        groupDepth++;
                        steps.Add(new Vector4F(GroupCode, 1, 0, 0));
                    }

                    break;
                case ColorPaintOperationKind.PopGroup:
                    if (!skippedGroups.Pop())
                    {
                        groupDepth--;
                        steps.Add(new Vector4F(PopGroupCode, 1, (float)operation.Mode, 0));
                    }

                    break;
                default:
                    Fill(steps, operation.Fill);
                    break;
            }
        }

        if (clipBox is { } limit)
        {
            box.Clip(limit.X, limit.Y, limit.X + limit.Width, limit.Y + limit.Height);
        }

        if (box.IsEmpty)
        {
            _stopData.RemoveRange(stopCount, _stopData.Count - stopCount);
            return -1;
        }

        var program = _programData.Count;
        _programData.Add(new Vector4F((float)box.MinX, (float)box.MinY, (float)(box.MaxX - box.MinX),
            (float)(box.MaxY - box.MinY)));
        _programData.Add(new Vector4F(steps.Count, 0, 0, 0));
        _programData.AddRange(steps);
        return program;
    }

    private static int MatchingPopClip(IReadOnlyList<ColorPaintOperation> operations, int pushClip)
    {
        var depth = 0;
        for (var i = pushClip; i < operations.Count; i++)
        {
            if (operations[i].Kind == ColorPaintOperationKind.PushClip)
            {
                depth++;
            }
            else if (operations[i].Kind == ColorPaintOperationKind.PopClip && --depth == 0)
            {
                return i;
            }
        }

        return operations.Count;
    }

    private static void Clip(List<Vector4F> steps, ColorPaintOperation operation, ColorPaintMask mask,
        double unitsPerEm, uint fieldSize, ref Box box)
    {
        var bounds = mask.Glyph.BoundingRectangle;
        var cell = mask.Cell;
        if (cell == null)
        {
            ClipToNothing(steps);
            return;
        }

        var margin = cell.Margin - 0.5;
        var uniform = margin * unitsPerEm / fieldSize;
        var marginX = bounds.Width > 0 && cell.BoundingRect.Width > 0 ? margin * bounds.Width / cell.BoundingRect.Width : uniform;
        var marginY = bounds.Height > 0 && cell.BoundingRect.Height > 0 ? margin * bounds.Height / cell.BoundingRect.Height : uniform;

        var transform = operation.Transform;
        var origin = Matrix3x2.TransformPoint(transform, new Vector2(mask.Bearing - marginX, bounds.Y + bounds.Height + marginY));
        var axisU = Linear(transform, new Vector2(bounds.Width + 2 * marginX, 0));
        var axisV = Linear(transform, new Vector2(0, -(bounds.Height + 2 * marginY)));
        var quad = new Matrix3x2(axisU.X, axisU.Y, axisV.X, axisV.Y, origin.X, origin.Y);
        var source = GlyphQuadBuilder.CellSource(cell);
        if (Math.Abs(quad.Determinant()) < 1e-12)
        {
            ClipToNothing(steps);
            return;
        }

        var extent = new Box();
        extent.Add(origin);
        extent.Add(origin + axisU);
        extent.Add(origin + axisV);
        extent.Add(origin + axisU + axisV);
        var toCell = Matrix3x2.Multiply(Matrix3x2.Multiply(Matrix3x2.Invert(quad),
            Matrix3x2.Scaling(source.Width, source.Height)), Matrix3x2.Translation(source.Left, source.Top));
        steps.Add(new Vector4F(ClipCode, 5, cell.DepthLayer, 0));
        steps.Add(new Vector4F((float)extent.MinX, (float)extent.MinY, (float)extent.MaxX, (float)extent.MaxY));
        steps.Add(new Vector4F((float)toCell.M11, (float)toCell.M12, (float)toCell.M21, (float)toCell.M22));
        steps.Add(new Vector4F((float)toCell.M31, (float)toCell.M32, 0, 0));
        steps.Add(new Vector4F(source.Left, source.Top, source.Right, source.Bottom));
        box.Add(origin);
        box.Add(origin + axisU);
        box.Add(origin + axisV);
        box.Add(origin + axisU + axisV);
    }

    private static void ClipToNothing(List<Vector4F> steps)
    {
        steps.Add(new Vector4F(ClipCode, 5, 0, 0));
        steps.Add(new Vector4F(1, 1, 0, 0));
        steps.Add(Vector4F.Zero);
        steps.Add(Vector4F.Zero);
        steps.Add(new Vector4F(1, 1, 0, 0));
    }

    private void Fill(List<Vector4F> steps, ColorFill fill)
    {
        if (fill.Stops.Count == 0 || Math.Abs(fill.Transform.Determinant()) < 1e-12)
        {
            return;
        }

        var toGradient = Matrix3x2.Invert(fill.Transform);
        var firstStop = _stopData.Count / 2;
        foreach (var stop in fill.Stops)
        {
            if (stop.Color is { } color)
            {
                _stopData.Add(new Vector4F(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f));
                _stopData.Add(new Vector4F(stop.Offset, 0, 0, 0));
            }
            else
            {
                _stopData.Add(new Vector4F(0, 0, 0, stop.Alpha));
                _stopData.Add(new Vector4F(stop.Offset, 1, 0, 0));
            }
        }

        steps.Add(new Vector4F(FillCode, 5, (float)fill.Kind, (float)fill.Extend));
        steps.Add(new Vector4F((float)toGradient.M11, (float)toGradient.M12, (float)toGradient.M21, (float)toGradient.M22));
        steps.Add(new Vector4F((float)toGradient.M31, (float)toGradient.M32, firstStop, fill.Stops.Count));
        steps.Add(Shape(fill));
        steps.Add(new Vector4F((float)fill.Radius0, (float)fill.Radius1, 0, 0));
    }

    private static Vector4F Shape(ColorFill fill)
    {
        switch (fill.Kind)
        {
            case ColorFillKind.LinearGradient:
                var end = LinearEnd(fill.Point0, fill.Point1, fill.Point2);
                return new Vector4F((float)fill.Point0.X, (float)fill.Point0.Y, (float)end.X, (float)end.Y);
            case ColorFillKind.SweepGradient:
                return new Vector4F((float)fill.Point0.X, (float)fill.Point0.Y, (float)(fill.StartAngle * Math.PI / 180),
                    (float)(fill.EndAngle * Math.PI / 180));
            default:
                return new Vector4F((float)fill.Point0.X, (float)fill.Point0.Y, (float)fill.Point1.X, (float)fill.Point1.Y);
        }
    }

    private static Vector2 LinearEnd(Vector2 p0, Vector2 p1, Vector2 p2)
    {
        var normal = new Vector2(p2.Y - p0.Y, -(p2.X - p0.X));
        var length = normal.X * normal.X + normal.Y * normal.Y;
        if (length < 1e-12)
        {
            return p1;
        }

        var along = ((p1.X - p0.X) * normal.X + (p1.Y - p0.Y) * normal.Y) / length;
        return new Vector2(p0.X + normal.X * along, p0.Y + normal.Y * along);
    }

    private static Vector2 Linear(Matrix3x2 m, Vector2 v) =>
        new(v.X * m.M11 + v.Y * m.M21, v.X * m.M12 + v.Y * m.M22);

    private int Send(IGraphicsDevice device, ref Buffer<Vector4F> buffer, List<Vector4F> data, int uploaded)
    {
        if (buffer == null || (int)buffer.ElementCount < data.Count)
        {
            Retire(buffer);
            var capacity = Math.Max(InitialCapacity, (int)(buffer?.ElementCount ?? 0) * 2);
            while (capacity < data.Count)
            {
                capacity *= 2;
            }

            buffer = Buffer.New<Vector4F>(device, (ulong)capacity,
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                BufferMemoryUsage.UploadFromCpuToGpu);
            uploaded = 0;
        }

        if (uploaded < data.Count)
        {
            var span = CollectionsMarshal.AsSpan(data).Slice(uploaded);
            buffer.SetData((ReadOnlySpan<Vector4F>)span, (uint)(uploaded * Marshal.SizeOf<Vector4F>()));
        }

        return data.Count;
    }

    private void Retire(Buffer<Vector4F> buffer)
    {
        if (buffer != null)
        {
            _device.AddToDeferDisposeQueue(buffer);
        }
    }

    private struct Box
    {
        public double MinX;
        public double MinY;
        public double MaxX;
        public double MaxY;
        private bool _any;

        public readonly bool IsEmpty => !_any || MaxX <= MinX || MaxY <= MinY;

        public void Clip(double minX, double minY, double maxX, double maxY)
        {
            MinX = Math.Max(MinX, minX);
            MinY = Math.Max(MinY, minY);
            MaxX = Math.Min(MaxX, maxX);
            MaxY = Math.Min(MaxY, maxY);
        }

        public void Add(Vector2 point)
        {
            if (!_any)
            {
                MinX = MaxX = point.X;
                MinY = MaxY = point.Y;
                _any = true;
                return;
            }

            MinX = Math.Min(MinX, point.X);
            MinY = Math.Min(MinY, point.Y);
            MaxX = Math.Max(MaxX, point.X);
            MaxY = Math.Max(MaxY, point.Y);
        }
    }
}
