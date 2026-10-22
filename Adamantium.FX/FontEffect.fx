static const float EPSILON = 1.401298E-45;
static const int VERTICES_PER_SPRITE = 4;

struct FontItem
{
    float4 Destination: Position;
    float4 Source: TEXCOORD0;
    float2 Origin: TEXCOORD1;
    float Depth : PSIZE0;
    float Rotation : PSIZE1;
    float4 Color: COLOR0;
    int SpriteEffect : BLENDINDICES0;
    // The atlas array slice this glyph was packed into - read by the pixel stage, see the note below.
    float Layer : PSIZE2;
    // A face the font lacks: x = the outline's outward move for a bold, in field units; y = an italic's slant; z = the
    // baseline's place in the quad (0 top, 1 bottom), which the slant pivots on.
    float4 Synthesis : TEXCOORD2;
    // A 'COLR' version 1 glyph: x = its paint program plus one (0 = an ordinary glyph). Destination then holds the pen
    // and baseline (xy) and the pixels per font unit (zw).
    float4 Paint : TEXCOORD3;
    // A glyph on the way between two key instances of a variable font: the second key's cell over the same quad (zero
    // for a glyph drawn from one field), and its layer (x) and how far the glyph is from the first key to it (y).
    float4 SecondSource : TEXCOORD4;
    float2 Second : TEXCOORD5;
    // The cell the layout gave the glyph (x, y, width, height), which its effects turn it about and shift it by.
    float4 Cell : TEXCOORD6;
    // x = the character it draws, which picks the effects that reach it (-1: none do); y = 1 when it takes the text's
    // color, which an effect's color replaces.
    float2 Place : TEXCOORD7;
};

// The atlas layer is a runtime index, so glyphs the packer spills to layers 1+ are sampled from their own layer.

struct PSInput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
    float4 Color : COLOR;
    // The rounded ancestor clip's SHAPE, fetched from the transform table in the VERTEX stage like every other family
    // (see ClipFromSlot). That fetch is a SECOND read of the table from this shader, which used to kill the shader
    // compiler outright until the effect lost its two dead passes.
    nointerpolation float4 ClipBox : TEXCOORD1;
    nointerpolation float4 ClipRadii : TEXCOORD2;
    // The atlas array slice, as a runtime value - see the note above for why it was a compile-time 0 for so long.
    nointerpolation float Layer : TEXCOORD3;
    // How far a synthesized bold moves the glyph's edge out, in field units; 0 for a face drawn as it is.
    nointerpolation float Embolden : TEXCOORD4;
    // A 'COLR' version 1 glyph's paint program plus one (0 = an ordinary glyph), the pixel's point in the glyph's space,
    // and the fade the glyph's own colors take, raised to 2.2.
    nointerpolation float PaintProgram : TEXCOORD5;
    float2 PaintPoint : TEXCOORD6;
    nointerpolation float OwnFade : TEXCOORD7;
    // A glyph between two key instances: the point in the second key's cell, and that cell's layer (x, negative for a
    // glyph drawn from one field) and its share (y).
    float2 SecondUV : TEXCOORD8;
    nointerpolation float2 Second : TEXCOORD9;
    // The clip of the glyph's effects (see EffectClipCoverage): the fragment in its space, homogeneous, and its shape.
    float3 EffectClipPoint : TEXCOORD10;
    nointerpolation float4 EffectClipBox : TEXCOORD11;
    nointerpolation float4 EffectClipRadii : TEXCOORD12;
    // A glyph drawn as a shadow or a glow: the width of its soft edge in field units, 0 for a crisp one.
    nointerpolation float Soft : TEXCOORD13;
    // How far down its quad the point is, 0 at the top and 1 at the bottom, for a reflection's fade.
    float ReflectY : TEXCOORD14;
    // A reflection: x = the share of its quad, from the top, it fades over; y = how strong it starts. x = 0 for every
    // other glyph.
    nointerpolation float2 Reflection : TEXCOORD15;
};

// A reflection's strength at a point `y` down its quad; 1 for every other glyph.
float ReflectionFade(float2 reflection, float y)
{
    return reflection.x > 0.0 ? reflection.y * saturate(1.0 - y / reflection.x) : 1.0;
}

// Where a glyph is drawn between two key instances, the second field's place for this corner and its layer and share;
// a negative layer for a glyph drawn from one field.
void SecondField(float4 secondSource, float2 second, float2 corner, out float2 uv, out float2 field)
{
    uv = secondSource.xy + corner * secondSource.zw;
    field = secondSource.z > 0.0 ? second : float2(-1.0, 0.0);
}

// A synthesized italic: the corner's shift right, for a quad of the given height, slanted about its baseline.
float SlantShift(float4 synthesis, float cornerY, float height)
{
    return (synthesis.z - cornerY) * height * synthesis.y;
}

Texture2DArray Texture : register(t1);
SamplerState TextureSampler : register(s1);
// Color glyph images ('CBDT', 'sbix'): premultiplied, each in a cell with its own mip chain.
Texture2DArray ColorTexture : register(t2);

float4x4 MatrixTransform;
float2 TextureCornerCoords[4];
float4 ForegroundColor;
float FontSize;
float FontSizeThreshold;
float FontWeight;
float PxRange;
float2 MSDFAtlasSize;
// True-SDF blend band, in atlas texels per screen pixel. Below SdfBlendLo the glyph is magnified -> use the
// MSDF median (keeps sharp corners). Above SdfBlendHi it is minified -> use the single-channel true SDF
// (alpha), which stays crisp where median(bilinear) softens (its only cost, rounded corners, is sub-pixel
// at that size). Smoothly blended in between so there is no pop across the size threshold.
float SdfBlendLo;
float SdfBlendHi;
// The element's fade, raised to 2.2, for glyphs with a color of their own; ForegroundColor already carries it.
float GlyphFade;

// Rounded ancestor clip for the per-block draw: xy origin and zw size in device pixels (zw = 0: no clip), plus corner
// radii. One draw is one block with one clip; the batched pass reads the same values from the transform table.
float4 DirectClipBox;
float4 DirectClipRadii;

// Where the target's top-left corner lies in device pixels of the surface (GraphicsDevice.TargetOrigin): non-zero when
// the target holds one region of it, so a fragment plus this is in the space the clip boxes are given in.
float2 TargetOrigin;

// Instanced glyph batch: per-instance GlyphData, and the transform table indexed by slot (0 = identity), so node-local
// glyphs move with one matrix write instead of a CPU re-bake.
uint64_t GlyphInstancesAddress;
uint64_t TransformsAddress;

// The paint programs of 'COLR' version 1 glyphs (Adamantium.Graphics/Fonts/ColorPaintStore.cs), in float4s: a header
// - the glyph's box in font units with y up (xy = its lower left corner, zw = its size), then the number of float4s of
// its steps - and the steps, each led by (code, length in float4s, a, b):
//   1 clip      (1, 5, atlas layer, how far past its pop clip), the outline's box in the glyph's space
//               (x0 y0 x1 y1), from the glyph's space to the outline's atlas cell (m11 m12 m21 m22),
//               (m31 m32, -, -), the cell (u0 v0 u1 v1)
//   2 pop clip  (2, 1, -, -)
//   3 group     (3, 1, -, -)
//   4 pop group (4, 1, mode, -), the modes of Adamantium.Fonts.ColorCompositeMode
//   5 fill      (5, 5, kind, extend), from the glyph's space to the gradient's (m11 m12 m21 m22),
//               (m31 m32, first stop, stop count), the shape, the radii (r0 r1, -, -)
//   6 image     (6, 4, color atlas layer, color atlas size), from the glyph's space to the color atlas
//               (m11 m12 m21 m22), (m31 m32, deepest level, -), the image's cell (u0 v0 u1 v1)
// Kinds: 0 solid, 1 linear (start, end), 2 radial (the two centers), 3 sweep (center, start and end angle in radians).
// Extends: 0 pad, 1 repeat, 2 reflect.
uint64_t PaintProgramsAddress;
uint64_t PaintStopsAddress;

static const uint PaintGroupDepth = 8;
static const uint PaintClipDepth = 8;

struct PaintStop
{
    float4 Color;     // straight RGBA; a stop in the text's color keeps only its alpha, in a
    float4 Params;    // x = offset, y = 1 for the text's color
};

// One entry of the transform table (see Adamantium.UI/Rendering/TransformTable.cs). It carries the node's ALPHA beside
// its matrix - both are one node's state, and this shader must know the layout even though it only reads the matrix,
// or it would stride through the buffer wrong.
struct NodeSlot
{
    float4x4 World;
    float4   Params;   // .x = alpha (1 = opaque); .yzw reserved
};

// ---- Text clip: a rounded rectangle in device pixels, box.xy = origin, box.zw = size (0 = no clip), radii = TL, TR,
// BR, BL. The text's own code, so the engine does not depend on the UI's shader headers.

float TextClipDistance(float2 p, float2 halfSize, float4 radii)
{
    // SDF y is down, so a negative p.y is the top half.
    float r = p.x < 0.0 ? (p.y < 0.0 ? radii.x : radii.w)
                        : (p.y < 0.0 ? radii.y : radii.z);
    float2 q = abs(p) - halfSize + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, float2(0.0, 0.0))) - r;
}

float ClipCoverage(float2 fragment, float4 box, float4 radii)
{
    if (box.z <= 0.0) return 1.0;
    float2 halfSize = max(box.zw * 0.5, float2(1.0, 1.0));
    float2 local = fragment + TargetOrigin - (box.xy + halfSize);
    float lim = min(halfSize.x, halfSize.y);
    float d = TextClipDistance(local, halfSize, min(radii, float4(lim, lim, lim, lim)));
    float aa = fwidth(d) + 1e-4;
    return 1.0 - smoothstep(-aa, aa, d);
}

// The batch reads its clip from the transform table: row 0 of the slot's matrix is the box, row 1 the radii, and
// Params.x marks the slot as carrying one. A slot below 0 means no clip.
void ClipFromSlot(float slotIndex, out float4 box, out float4 radii)
{
    box = float4(0.0, 0.0, 0.0, 0.0);
    radii = float4(0.0, 0.0, 0.0, 0.0);
    if (slotIndex < 0.0) return;
    NodeSlot clip = ((NodeSlot*)TransformsAddress)[(uint)slotIndex];
    if (clip.Params.x < 0.5) return;
    box = clip.World[0];
    radii = clip.World[1];
}

// The per-block draw's chain of glyph effects (see GlyphEffectsAt): the slot of its first effect, -1 for none.
float EffectHead;

// The longest chain walked, so a slot written wrong ends in a wrong glyph instead of a hung device.
static const uint MaxEffectChain = 1024;

// No effect clip: a negative size (a zero one cuts everything away).
static const float4 NoEffectClip = float4(0.0, 0.0, -1.0, -1.0);

// The cut of a glyph effect's clip (GlyphClip) at a fragment: `point` is the fragment in the clip's space, still
// homogeneous - divided here, so it is right under a perspective too.
float EffectClipCoverage(float3 point, float4 box, float4 radii)
{
    if (box.z < 0.0)
    {
        return 1.0;
    }

    if (box.z <= 0.0 || box.w <= 0.0)
    {
        return 0.0;
    }

    float2 halfSize = box.zw * 0.5;
    float2 local = point.xy / point.z - (box.xy + halfSize);
    float lim = min(halfSize.x, halfSize.y);
    float d = TextClipDistance(local, halfSize, min(radii, float4(lim, lim, lim, lim)));
    float aa = fwidth(d) + 1e-4;
    return 1.0 - smoothstep(-aa, aa, d);
}

// A glyph's effects, read from the transform table as GlyphEffect.ToSlot writes them: from the slot `head`, each slot
// one effect of the text, applied in order to the glyphs of its range. Rows 0-2 of a slot are the transform with the
// range's start, its end and the next slot in their w; row 3 is the shift, the flags (1 around the glyph, 2 has a color)
// and the slot of its clip; Params is the color, which replaces the text's on a glyph in it. The last effect that clips
// the glyph gives `clipPoint` (homogeneous) - where the layout put it, or where the effects put it - `clipBox` and
// `clipRadii`. Returns the point as (x * w, y * w, w), w left undivided so it keeps its sign however far the glyph is
// from the origin.
float3 GlyphEffectsAt(float2 p, float4 cell, float2 place, float head, inout float4 color, out float3 clipPoint,
    out float4 clipBox, out float4 clipRadii)
{
    float3 h = float3(p, 1.0);
    clipPoint = float3(0.0, 0.0, 1.0);
    clipBox = NoEffectClip;
    clipRadii = float4(0.0, 0.0, 0.0, 0.0);
    if (head < 0.0 || place.x < 0.0)
    {
        return h;
    }

    NodeSlot* slots = (NodeSlot*)TransformsAddress;
    float3 laid = h;
    float clipSlot = -1.0;
    float next = head;
    [loop]
    for (uint i = 0; i < MaxEffectChain && next >= 0.0; i++)
    {
        NodeSlot e = slots[(uint)next];
        float4 r0 = e.World[0];
        float4 r1 = e.World[1];
        float4 r2 = e.World[2];
        float4 r3 = e.World[3];
        next = r2.w;
        if (place.x < r0.w || place.x >= r1.w)
        {
            continue;
        }

        uint flags = (uint)(r3.z + 0.5);
        float2 pivot = (flags & 1u) != 0u ? cell.xy + cell.zw * 0.5 : float2(0.0, 0.0);
        float3 q = float3(h.xy - pivot * h.z, h.z);
        q = float3(q.x * r0.x + q.y * r1.x + q.z * r2.x,
                   q.x * r0.y + q.y * r1.y + q.z * r2.y,
                   q.x * r0.z + q.y * r1.z + q.z * r2.z);
        h = float3(q.xy + (pivot + r3.xy * cell.zw) * q.z, q.z);
        if ((flags & 2u) != 0u && place.y > 0.5)
        {
            color = e.Params;
        }

        if (r3.w >= 0.0)
        {
            clipSlot = r3.w;
        }
    }

    if (clipSlot >= 0.0)
    {
        NodeSlot c = slots[(uint)clipSlot];
        float4 m = c.World[2];
        float4 t = c.World[3];
        float3 at = t.z > 0.5 ? h : laid;
        clipPoint = float3(at.x * m.x + at.y * m.z + at.z * t.x, at.x * m.y + at.y * m.w + at.z * t.y, at.z);
        clipBox = c.World[0];
        clipRadii = c.World[1];
    }

    return h;
}

// ---- 'COLR' version 1 glyphs: one quad over the glyph's box, placed at the pen and baseline and scaled to pixels; the
// pixel shader runs the glyph's paint program at the pixel's point in the glyph's space.

float2 PaintCorner(float paint, float2 corner, float4 origin, out float2 glyphPoint)
{
    float4 box = ((float4*)PaintProgramsAddress)[(uint)(paint - 1.0)];
    glyphPoint = float2(box.x + corner.x * box.z, box.y + (1.0 - corner.y) * box.w);
    return origin.xy + float2(glyphPoint.x, -glyphPoint.y) * origin.zw;
}

// A stop's color with its alpha linear, and what that alpha is multiplied by: the text color's alpha for a stop in the
// text's color, the fade for one of the font's own, both linear.
float4 StopColor(PaintStop stop, float4 foreground)
{
    return stop.Params.y > 0.5 ? float4(foreground.rgb, stop.Color.a) : stop.Color;
}

float StopFade(PaintStop stop, float foregroundAlpha, float fade)
{
    return stop.Params.y > 0.5 ? foregroundAlpha : fade;
}

float ExtendPosition(float t, float extend)
{
    if (extend > 1.5) return 1.0 - abs(frac(t * 0.5) * 2.0 - 1.0);
    if (extend > 0.5) return frac(t);
    return saturate(t);
}

// The color line at t, in the stops' own offsets: extended past its first and last stop, interpolated premultiplied;
// the stops' fades come out interpolated beside it.
float4 ColorLine(uint firstStop, uint count, float extend, float t, float4 foreground, float foregroundAlpha,
    float fade, out float lineFade)
{
    PaintStop* stops = ((PaintStop*)PaintStopsAddress) + firstStop;
    lineFade = 0.0;
    if (count == 0) return float4(0.0, 0.0, 0.0, 0.0);
    float first = stops[0].Params.x;
    float last = stops[count - 1].Params.x;
    if (last > first)
    {
        t = first + ExtendPosition((t - first) / (last - first), extend) * (last - first);
    }

    float4 previous = StopColor(stops[0], foreground);
    lineFade = StopFade(stops[0], foregroundAlpha, fade);
    if (t <= first) return previous;
    for (uint i = 1; i < count; i++)
    {
        float4 next = StopColor(stops[i], foreground);
        float nextFade = StopFade(stops[i], foregroundAlpha, fade);
        float from = stops[i - 1].Params.x;
        float to = stops[i].Params.x;
        if (t <= to)
        {
            float k = to > from ? (t - from) / (to - from) : 1.0;
            lineFade = lerp(lineFade, nextFade, k);
            float4 mixed = lerp(float4(previous.rgb * previous.a, previous.a), float4(next.rgb * next.a, next.a), k);
            return mixed.a > 0.0 ? float4(mixed.rgb / mixed.a, mixed.a) : float4(0.0, 0.0, 0.0, 0.0);
        }

        previous = next;
        lineFade = nextFade;
    }

    return previous;
}

// A two-point conical gradient's t at a point: the largest t whose circle passes through it with a radius of 0 or more;
// false where no circle does.
bool RadialPosition(float4 shape, float2 radii, float2 p, out float t)
{
    float2 c0 = shape.xy;
    float r0 = radii.x;
    float2 cd = shape.zw - c0;
    float2 pd = p - c0;
    float dr = radii.y - r0;
    float a = dot(cd, cd) - dr * dr;
    float b = dot(pd, cd) + r0 * dr;
    float c = dot(pd, pd) - r0 * r0;
    t = 0.0;
    if (abs(a) < 1e-6)
    {
        if (abs(b) < 1e-6) return false;
        t = c / (2.0 * b);
        return r0 + t * dr >= 0.0;
    }

    float discriminant = b * b - a * c;
    if (discriminant < 0.0) return false;
    float root = sqrt(discriminant);
    float t1 = (b + root) / a;
    float t2 = (b - root) / a;
    t = max(t1, t2);
    if (r0 + t * dr >= 0.0) return true;
    t = min(t1, t2);
    return r0 + t * dr >= 0.0;
}

// The fill step at `at`, at a point of the glyph's space: straight color, alpha linear.
float4 FillColor(float4* steps, uint at, float2 glyphPoint, float4 foreground, float foregroundAlpha, float fade)
{
    float4 head = steps[at];
    float4 linear = steps[at + 1];
    float4 offset = steps[at + 2];
    float4 shape = steps[at + 3];
    float2 radii = steps[at + 4].xy;
    float2 p = glyphPoint.x * linear.xy + glyphPoint.y * linear.zw + offset.xy;
    float kind = head.z;
    float t = 0.0;
    if (kind > 2.5)
    {
        float2 d = p - shape.xy;
        float angle = atan2(d.y, d.x);
        if (angle < 0.0) angle += 6.28318530718;
        float span = shape.w - shape.z;
        // An empty sweep keeps only its two end colors, padded; repeated or reflected it has nothing to repeat.
        if (abs(span) <= 1e-6 && head.w > 0.5) return float4(0.0, 0.0, 0.0, 0.0);
        t = abs(span) > 1e-6 ? (angle - shape.z) / span : (angle < shape.z ? -1e6 : 1e6);
    }
    else if (kind > 1.5)
    {
        if (!RadialPosition(shape, radii, p, t)) return float4(0.0, 0.0, 0.0, 0.0);
    }
    else if (kind > 0.5)
    {
        float2 d = shape.zw - shape.xy;
        float length2 = dot(d, d);
        if (length2 <= 0.0) return float4(0.0, 0.0, 0.0, 0.0);
        t = dot(p - shape.xy, d) / length2;
    }

    float lineFade;
    float4 color = ColorLine((uint)offset.z, (uint)offset.w, head.w, t, foreground, foregroundAlpha, fade, lineFade);
    color.a *= lineFade;
    return color;
}

float PaintLum(float3 c)
{
    return dot(c, float3(0.3, 0.59, 0.11));
}

float3 PaintClipColor(float3 c)
{
    float l = PaintLum(c);
    float n = min(c.r, min(c.g, c.b));
    float x = max(c.r, max(c.g, c.b));
    if (n < 0.0) c = l + (c - l) * l / max(l - n, 1e-6);
    if (x > 1.0) c = l + (c - l) * (1.0 - l) / max(x - l, 1e-6);
    return c;
}

float3 PaintSetLum(float3 c, float l)
{
    return PaintClipColor(c + (l - PaintLum(c)));
}

float PaintSat(float3 c)
{
    return max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b));
}

float3 PaintSetSat(float3 c, float s)
{
    float x = max(c.r, max(c.g, c.b));
    float n = min(c.r, min(c.g, c.b));
    return x > n ? (c - n) * s / (x - n) : float3(0.0, 0.0, 0.0);
}

float PaintHardLight(float s, float d)
{
    return s <= 0.5 ? d * 2.0 * s : d + (2.0 * s - 1.0) - d * (2.0 * s - 1.0);
}

float PaintSoftLight(float s, float d)
{
    if (s <= 0.5) return d - (1.0 - 2.0 * s) * d * (1.0 - d);
    float e = d <= 0.25 ? ((16.0 * d - 12.0) * d + 4.0) * d : sqrt(d);
    return d + (2.0 * s - 1.0) * (e - d);
}

float PaintColorDodge(float s, float d)
{
    if (d <= 0.0) return 0.0;
    if (s >= 1.0) return 1.0;
    return min(1.0, d / (1.0 - s));
}

float PaintColorBurn(float s, float d)
{
    if (d >= 1.0) return 1.0;
    if (s <= 0.0) return 0.0;
    return 1.0 - min(1.0, (1.0 - d) / s);
}

// A blend mode of the W3C Compositing and Blending specification, on straight colors.
float3 PaintBlend(float3 s, float3 d, uint mode)
{
    switch (mode)
    {
        case 13: return s + d - s * d;
        case 14: return float3(PaintHardLight(d.r, s.r), PaintHardLight(d.g, s.g), PaintHardLight(d.b, s.b));
        case 15: return min(s, d);
        case 16: return max(s, d);
        case 17: return float3(PaintColorDodge(s.r, d.r), PaintColorDodge(s.g, d.g), PaintColorDodge(s.b, d.b));
        case 18: return float3(PaintColorBurn(s.r, d.r), PaintColorBurn(s.g, d.g), PaintColorBurn(s.b, d.b));
        case 19: return float3(PaintHardLight(s.r, d.r), PaintHardLight(s.g, d.g), PaintHardLight(s.b, d.b));
        case 20: return float3(PaintSoftLight(s.r, d.r), PaintSoftLight(s.g, d.g), PaintSoftLight(s.b, d.b));
        case 21: return abs(s - d);
        case 22: return s + d - 2.0 * s * d;
        case 23: return s * d;
        case 24: return PaintSetLum(PaintSetSat(s, PaintSat(d)), PaintLum(d));
        case 25: return PaintSetLum(PaintSetSat(d, PaintSat(s)), PaintLum(d));
        case 26: return PaintSetLum(s, PaintLum(d));
        default: return PaintSetLum(d, PaintLum(s));
    }
}

// A group composited onto what is under it, both premultiplied.
float4 PaintComposite(float4 s, float4 d, uint mode)
{
    switch (mode)
    {
        case 0: return float4(0.0, 0.0, 0.0, 0.0);
        case 1: return s;
        case 2: return d;
        case 3: return s + d * (1.0 - s.a);
        case 4: return d + s * (1.0 - d.a);
        case 5: return s * d.a;
        case 6: return d * s.a;
        case 7: return s * (1.0 - d.a);
        case 8: return d * (1.0 - s.a);
        case 9: return s * d.a + d * (1.0 - s.a);
        case 10: return d * s.a + s * (1.0 - d.a);
        case 11: return s * (1.0 - d.a) + d * (1.0 - s.a);
        case 12: return min(s + d, float4(1.0, 1.0, 1.0, 1.0));
    }

    float3 cs = s.a > 0.0 ? s.rgb / s.a : float3(0.0, 0.0, 0.0);
    float3 cd = d.a > 0.0 ? d.rgb / d.a : float3(0.0, 0.0, 0.0);
    float3 rgb = (1.0 - d.a) * s.rgb + (1.0 - s.a) * d.rgb + s.a * d.a * PaintBlend(cs, cd, mode);
    return float4(rgb, s.a + d.a - s.a * d.a);
}

// Per-glyph quad expansion, now in the VERTEX stage (corner from SV_VertexID), so the geometry shader is gone:
// plain instanced rendering (4-vertex triangle strip x N glyphs), portable to Metal/MoltenVK.
PSInput ExpandGlyphCorner(FontItem item, int corner)
{
    PSInput vertex;
    float2 origin = item.Origin;
    float2 rotation = float2(cos(item.Rotation), sin(item.Rotation));

    float2 cornerCoord = TextureCornerCoords[corner];
    float2 size = cornerCoord * item.Destination.zw;
    size.x += SlantShift(item.Synthesis, cornerCoord.y, item.Destination.w);
    float2 position = item.Destination.xy + size;

    float2 paintPoint = float2(0.0, 0.0);
    if (item.Paint.x > 0.5)
    {
        position = PaintCorner(item.Paint.x, cornerCoord, item.Destination, paintPoint);
    }

    // Turned about Destination.xy + Origin, a color glyph's quad as well.
    [flatten]
    if (item.Rotation != 0.0)
    {
        float2 pivot = item.Destination.xy + origin;
        float2 arm = position - pivot;
        position = pivot + float2(arm.x * rotation.x - arm.y * rotation.y, arm.x * rotation.y + arm.y * rotation.x);
    }

    float4 color = item.Color;
    float3 moved = GlyphEffectsAt(position, item.Cell, item.Place, EffectHead, color, vertex.EffectClipPoint,
        vertex.EffectClipBox, vertex.EffectClipRadii);
    vertex.Position.xy = moved.xy;

    vertex.PaintProgram = item.Paint.x;
    vertex.PaintPoint = paintPoint;
    vertex.OwnFade = GlyphFade;

    vertex.Position.z = item.Depth * moved.z;
    vertex.Position.w = moved.z;
    vertex.Color = color;

    float2 uvCorner = TextureCornerCoords[corner ^ item.SpriteEffect];
    vertex.UV = item.Source.xy + uvCorner * item.Source.zw;
    SecondField(item.SecondSource, item.Second, uvCorner, vertex.SecondUV, vertex.Second);

    // The clip comes in as a uniform here (see DirectClipBox) instead of from the table by slot, and it has to be
    // WRITTEN either way: a varying this vertex shader does not set reaches the pixel shader as whatever was in the
    // register, and the batch shares this PSInput with it. A zero box is "no clip".
    vertex.Layer = item.Layer;
    vertex.Embolden = item.Synthesis.x;
    vertex.Soft = item.Synthesis.w;
    vertex.ReflectY = cornerCoord.y;
    vertex.Reflection = item.Paint.zw;
    vertex.ClipBox = DirectClipBox;
    vertex.ClipRadii = DirectClipRadii;

    vertex.Position = mul(vertex.Position, MatrixTransform);
    return vertex;
}

float Median(float r, float g, float b)
{
    return max(min(r, g), min(max(r, g), b));
}

float ScreenPxRange(float2 uv)
{
    float2 unitRange = float2(PxRange, PxRange) / MSDFAtlasSize;
    float2 screenTexSize = float2(1.0, 1.0) / fwidth(uv);
    return max(0.5 * dot(unitRange, screenTexSize), 1.0);
}

// How much of the pixel the glyph covers, `edge` being its field less the outline's level: crisp over one screen pixel,
// or spread over `soft` field units - never less than the pixel - for a shadow or a glow. Where the field `sd` runs out
// (its range past the outline) nothing is covered, so a band grown or softened up to it fades there rather than filling
// the quad.
float Coverage(float edge, float2 uv, float soft, float sd)
{
    float pixel = ScreenPxRange(uv);
    float reach = smoothstep(0.02, 0.1, sd);
    if (soft <= 0.0)
    {
        return clamp(pixel * edge + 0.5, 0.0, 1.0) * reach;
    }

    float width = max(soft, 1.0 / pixel);
    return smoothstep(-0.5 * width, 0.5 * width, edge) * reach;
}

// Coverage source for the glyph body: MSDF median when magnified (sharp corners), true-SDF alpha when
// minified (crisp where median(bilinear) softens), blended by the minification factor = the max UV
// derivative in atlas texels (the standard texture-LOD metric). With SdfBlendLo >= SdfBlendHi (or both very
// large) it stays pure MSDF, so the blend can be disabled purely via the uniforms - no hardcoded switch.
float SampleGlyphCoverage(float4 samp, float2 uv)
{
    float msdf = Median(samp.r, samp.g, samp.b);
    float texelsPerPx = max(length(ddx(uv) * MSDFAtlasSize), length(ddy(uv) * MSDFAtlasSize));
    float t = smoothstep(SdfBlendLo, SdfBlendHi, texelsPerPx);
    return lerp(msdf, samp.a, t);
}

// The field a glyph's coverage comes from. A glyph between two key instances of a variable font blends the true
// distance of both: their colored channels are split into edges differently and do not blend, and the true distance
// only rounds its corners while it moves.
float GlyphField(float4 samp, float2 uv, float2 secondUV, float2 second)
{
    if (second.x < 0.0)
    {
        return SampleGlyphCoverage(samp, uv);
    }

    float4 secondSamp = Texture.SampleLevel(TextureSampler, float3(secondUV, second.x), 0.0);
    return lerp(samp.a, secondSamp.a, second.y);
}

float MaskField(float2 uv, float layer, float blend)
{
    float4 samp = Texture.SampleLevel(TextureSampler, float3(uv, layer), 0.0);
    return lerp(Median(samp.r, samp.g, samp.b), samp.a, blend);
}

// A clip step's outline coverage at a point of the glyph's space, dx and dy the point's steps per pixel: the same field
// reconstruction as a glyph's, its derivatives carried through the step's mapping, as the loop that calls it is not
// uniform. A point outside the outline's cell is outside the outline. The field's distance becomes pixels along the
// outline's normal, so a mapping that squeezes one axis (a bar drawn from another bar scaled 1/8 by 8) keeps its edges.
float MaskCoverage(float4* steps, uint at, float2 p, float2 dx, float2 dy)
{
    float4 head = steps[at];
    float4 linear = steps[at + 2];
    float2 offset = steps[at + 3].xy;
    float4 cell = steps[at + 4];
    float2 uv = p.x * linear.xy + p.y * linear.zw + offset;
    if (uv.x < cell.x || uv.y < cell.y || uv.x > cell.z || uv.y > cell.w) return 0.0;
    float2 texelDx = (dx.x * linear.xy + dx.y * linear.zw) * MSDFAtlasSize;
    float2 texelDy = (dy.x * linear.xy + dy.y * linear.zw) * MSDFAtlasSize;
    float texelsPerPx = max(length(texelDx), length(texelDy));
    float blend = smoothstep(SdfBlendLo, SdfBlendHi, texelsPerPx);
    float sd = MaskField(uv, head.z, blend);
    float2 texel = float2(1.0, 1.0) / MSDFAtlasSize;
    float2 gradient = float2(MaskField(uv + float2(texel.x, 0.0), head.z, blend) - sd,
                             MaskField(uv + float2(0.0, texel.y), head.z, blend) - sd);
    float gradientLength = length(gradient);
    float2 normal = gradientLength > 1e-6 ? gradient / gradientLength : float2(0.70710678, 0.70710678);
    float texelsAlongNormal = length(float2(dot(texelDx, normal), dot(texelDy, normal)));
    float range = max(PxRange / max(texelsAlongNormal, 1e-9), 1.0);
    return saturate(range * (sd - 0.5 + FontWeight) + 0.5);
}

// An image step's color at a point of the glyph's space, premultiplied: the level the point's steps per pixel ask for,
// trilinear, kept half a texel of that level inside the image's cell so no neighbor bleeds in.
float4 ImageColor(float4* steps, uint at, float2 p, float2 dx, float2 dy)
{
    float4 head = steps[at];
    float4 linear = steps[at + 1];
    float4 offset = steps[at + 2];
    float4 cell = steps[at + 3];
    float2 uv = p.x * linear.xy + p.y * linear.zw + offset.xy;
    float2 uvDx = dx.x * linear.xy + dx.y * linear.zw;
    float2 uvDy = dy.x * linear.xy + dy.y * linear.zw;
    float footprint = max(length(uvDx), length(uvDy)) * head.w;
    float level = clamp(log2(max(footprint, 1e-6)), 0.0, offset.z);
    float inset = 0.5 * exp2(ceil(level)) / head.w;
    uv = clamp(uv, cell.xy + inset, cell.zw - inset);
    return ColorTexture.SampleLevel(TextureSampler, float3(uv, head.z), level);
}

// A 'COLR' version 1 glyph's paint program run at a point of its space: a stack of groups, each composited onto the
// one under it, and a stack of clips whose coverage multiplies. Premultiplied, its alpha linear.
float4 RunPaint(float paint, float2 p, float2 dx, float2 dy, float4 foreground, float foregroundAlpha, float fade)
{
    float4* steps = (float4*)PaintProgramsAddress;
    uint at = (uint)(paint - 1.0);
    uint end = at + 2 + (uint)steps[at + 1].x;
    at += 2;
    // The top of each stack lives in a register; only what lies under it goes to the arrays, at a push.
    // A group is drawn at full coverage and takes the clips around it once, as it is composited.
    float4 groups[PaintGroupDepth];
    float groupCoverage[PaintGroupDepth];
    float covered[PaintClipDepth];
    float4 top = float4(0.0, 0.0, 0.0, 0.0);
    float coverage = 1.0;
    uint depth = 0;
    uint clips = 0;
    while (at < end)
    {
        float4 head = steps[at];
        uint code = (uint)head.x;
        if (code == 1)
        {
            // A point outside the outline skips everything the clip holds, its pop clip included.
            float4 box = steps[at + 1];
            float mask = p.x < box.x || p.y < box.y || p.x > box.z || p.y > box.w ? 0.0 : MaskCoverage(steps, at, p, dx, dy);
            if (mask <= 0.0)
            {
                at += max((uint)head.w, 1u);
                continue;
            }

            covered[clips] = coverage;
            clips++;
            coverage *= mask;
        }
        else if (code == 2)
        {
            clips--;
            coverage = covered[clips];
        }
        else if (code == 3)
        {
            groups[depth] = top;
            groupCoverage[depth] = coverage;
            depth++;
            top = float4(0.0, 0.0, 0.0, 0.0);
            coverage = 1.0;
        }
        else if (code == 4)
        {
            depth--;
            float4 under = groups[depth];
            coverage = groupCoverage[depth];
            top = lerp(under, PaintComposite(top, under, (uint)head.z), coverage);
        }
        else if (code == 6)
        {
            float4 image = ImageColor(steps, at, p, dx, dy) * fade * coverage;
            top = image + top * (1.0 - image.a);
        }
        else
        {
            float4 color = FillColor(steps, at, p, foreground, foregroundAlpha, fade);
            float4 premultiplied = float4(color.rgb * color.a, color.a) * coverage;
            top = premultiplied + top * (1.0 - premultiplied.a);
        }

        at += max((uint)head.y, 1u);
    }

    return top;
}

[shader("vertex")]
PSInput FontVertexShader(FontItem item, uint vertexId : SV_VertexID)
{
    return ExpandGlyphCorner(item, (int)vertexId);   // vertexId 0..3 = strip corner
}

// The rounded ancestor clip and the clip of the glyph's effects, as one coverage.
float Cut(PSInput input)
{
    return ClipCoverage(input.Position.xy, input.ClipBox, input.ClipRadii)
           * EffectClipCoverage(input.EffectClipPoint, input.EffectClipBox, input.EffectClipRadii);
}

// Canonical MSDF reconstruction (Chlumsky). ScreenPxRange() gives the field slope in screen pixels.
// FontWeight shifts the 0.5 contour INSIDE the ScreenPxRange term (a true distance bias, not an opacity
// add), so it makes stems thinner/thicker without hazing the background. Selected via the RenderMsdf pass,
// toggled from FontRenderer.UseCanonicalMsdf.
[shader("fragment")]
float4 FontPixelShaderMsdf(PSInput input) : SV_Target
{
    float2 glyphDx = ddx(input.PaintPoint);
    float2 glyphDy = ddy(input.PaintPoint);
    float4 samp = Texture.Sample(TextureSampler, float3(input.UV, input.Layer));
    float sd = GlyphField(samp, input.UV, input.SecondUV, input.Second);
    float opacity = Coverage(sd - 0.5 + FontWeight + input.Embolden, input.UV, input.Soft, sd)
                    * ReflectionFade(input.Reflection, input.ReflectY);
    // A glyph of attributed text brings its own color; a negative alpha means the element's foreground.
    float4 color = input.Color.a < 0 ? ForegroundColor : float4(input.Color.rgb, input.Color.a * GlyphFade);
    if (input.PaintProgram > 0.5)
    {
        // A color glyph draws its own coverage, so no gamma boost: its alphas and fades go in linear.
        float4 painted = RunPaint(input.PaintProgram, input.PaintPoint, glyphDx, glyphDy, color,
            pow(max(color.a, 0.0), 1.0 / 2.2), pow(input.OwnFade, 1.0 / 2.2));
        return painted * Cut(input);
    }
    // Gamma-boost coverage times the color's alpha so thin stems keep their color; splitting the two washed text out.
    // The element's fade arrives pre-raised to 2.2, so the boost hands it back linear.
    float alpha = pow(color.a * opacity, 1.0 / 2.2);
    // The rounded ancestor clip, as coverage, exactly as the batch pass applies it. Both the premultiplied color and
    // the alpha are cut: this pass outputs rgb*alpha, so cutting one without the other leaves color where the glyph
    // was cut away. A zero-size box gives 1 and costs nothing.
    alpha *= Cut(input);
    return float4(color.rgb * alpha, alpha);
}

// Batch variant of FontPixelShaderMsdf: the color comes per instance (input.Color), so one instanced draw renders glyphs
// of many text blocks, each in its own color.
[shader("fragment")]
float4 FontPixelShaderMsdfBatch(PSInput input) : SV_Target
{
    float2 glyphDx = ddx(input.PaintPoint);
    float2 glyphDy = ddy(input.PaintPoint);
    float4 samp = Texture.Sample(TextureSampler, float3(input.UV, input.Layer));
    float sd = GlyphField(samp, input.UV, input.SecondUV, input.Second);
    float opacity = Coverage(sd - 0.5 + FontWeight + input.Embolden, input.UV, input.Soft, sd)
                    * ReflectionFade(input.Reflection, input.ReflectY);
    // Unchanged on purpose - the element's fade is pre-compensated in the vertex stage so that this very boost hands
    // it back linear. See the FADE line in FontBatchInstancedVS.
    float4 color = input.Color;
    if (input.PaintProgram > 0.5)
    {
        // A color glyph draws its own coverage, so no gamma boost: its alphas and fades go in linear.
        float4 painted = RunPaint(input.PaintProgram, input.PaintPoint, glyphDx, glyphDy, color,
            pow(max(color.a, 0.0), 1.0 / 2.2), pow(input.OwnFade, 1.0 / 2.2));
        return painted * Cut(input);
    }

    float alpha = pow(color.a * opacity, 1.0 / 2.2);
    // The rounded ancestor clip, as coverage. Applied to the PREMULTIPLIED color as well as the alpha - this pass
    // outputs rgb*alpha, so cutting only the alpha would leave the color standing where the glyph was cut away.
    alpha *= Cut(input);
    return float4(color.rgb * alpha, alpha);
}

// ---- Instanced glyph batch: per-instance GlyphData read from a BDA STORAGE buffer by SV_InstanceID (mirrors
// RectBatchInstancedVS in BatchEffect.fx); the quad comes from SV_VertexID. Node-local glyph rects are transformed to
// world on the GPU by the instance's transform-table slot (0 = identity), so a scrolling block moves via one matrix
// write, not a per-glyph CPU re-bake, and the batch is node-aware. Reuses FontPixelShaderMsdfBatch (per-instance color).
struct GlyphData
{
    float4 LocalRect;   // node-local x, y, w, h (world for slot-0 legacy bakes)
    float4 Source;      // atlas UV rect
    float4 Params;      // .x = transform-table slot; .y = atlas layer (read by the PS); .z = depth; .w reserved
    float4 Clip;        // .x = the ROUNDED CLIP's slot, or -1; .yzw = the glyph's synthesis (FontItem.Synthesis.xyz)
    float4 Color;       // straight RGBA, element/brush opacity folded into .w
    float4 Paint;       // .x = a 'COLR' version 1 glyph's paint program plus one, or 0 (LocalRect is then the pen and
                        // baseline, xy, and pixels per font unit, zw); .y = the element's opacity raised to 2.2, for the
                        // glyph's own colors; .z = a shadow's or a glow's soft edge, in field units; .w = the share of
                        // a reflection's quad it fades over (0 for every other glyph)
    float4 SecondSource; // a glyph between two key instances: the second key's cell over the same quad, or zero
    float4 Second;      // .x = that cell's layer; .y = its share; .z = the glyph's turn, radians clockwise about
                        // LocalRect.xy; .w = how strong a reflection starts
    float4 Cell;        // the glyph's cell in the LAYOUT's units, as FontItem.Cell holds it
    float4 ToNode;      // the layout's units to node-local: x * ToNode.x + ToNode.z, y * ToNode.y + ToNode.w
    float4 Place;       // .x, .y = FontItem.Place; .z = the slot of the block's first effect, -1 for none; .w = the
                        // element's opacity, which an effect's color takes
};

[shader("vertex")]
PSInput FontBatchInstancedVS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    GlyphData* items = (GlyphData*)GlyphInstancesAddress;
    GlyphData g = items[instanceId];

    PSInput o;
    // SAME corner mapping as ExpandGlyphCorner (TextureCornerCoords[vertexId]) so the quad + UV match the direct path.
    float2 corner = TextureCornerCoords[vertexId];
    float2 localPos = g.LocalRect.xy + corner * g.LocalRect.zw;
    localPos.x += SlantShift(float4(0.0, g.Clip.z, g.Clip.w, 0.0), corner.y, g.LocalRect.w);
    float2 paintPoint = float2(0.0, 0.0);
    if (g.Paint.x > 0.5)
    {
        localPos = PaintCorner(g.Paint.x, corner, g.LocalRect, paintPoint);
    }
    // A glyph turned in vertical text: Second.z radians clockwise about LocalRect.xy.
    if (g.Second.z != 0.0)
    {
        float2 turn = float2(cos(g.Second.z), sin(g.Second.z));
        float2 arm = localPos - g.LocalRect.xy;
        localPos = g.LocalRect.xy + float2(arm.x * turn.x - arm.y * turn.y, arm.x * turn.y + arm.y * turn.x);
    }
    // The block's effects work in the layout's units: there and back again, w kept.
    float effectW = 1.0;
    float4 recolor = float4(0.0, 0.0, 0.0, -1.0);
    o.EffectClipPoint = float3(0.0, 0.0, 1.0);
    o.EffectClipBox = NoEffectClip;
    o.EffectClipRadii = float4(0.0, 0.0, 0.0, 0.0);
    if (g.Place.z >= 0.0)
    {
        float3 moved = GlyphEffectsAt((localPos - g.ToNode.zw) / g.ToNode.xy, g.Cell, g.Place.xy, g.Place.z, recolor,
            o.EffectClipPoint, o.EffectClipBox, o.EffectClipRadii);
        localPos = moved.xy * g.ToNode.xy + g.ToNode.zw * moved.z;
        effectW = moved.z;
    }
    // Node-local -> world via the instance's transform-table matrix (slot 0 = identity for legacy world bakes).
    NodeSlot* nodes = (NodeSlot*)TransformsAddress;
    float4x4 nodeWorld = nodes[(uint)g.Params.x].World;
    float4 worldPos = mul(float4(localPos, g.Params.z * effectW, effectW), nodeWorld);
    o.Position = mul(worldPos, MatrixTransform);   // MatrixTransform = the (transposed-on-upload) projection
    o.UV = g.Source.xy + corner * g.Source.zw;     // SpriteEffect == 0 for batched glyphs
    SecondField(g.SecondSource, g.Second.xy, corner, o.SecondUV, o.Second);
    // The element's alpha from the OPACITY SLOT, exactly as every other family reads it: a fading ancestor then moves
    // one number in the table instead of re-baking every glyph under it. Params.w is -1 when nothing above fades.
    float fadeSlot = g.Params.w;
    float fade = nodes[(uint)max(fadeSlot, 0.0)].Params.x;
    fade = lerp(1.0, fade, step(0.0, fadeSlot));
    // The fade is raised to 2.2 so the pixel shader's gamma boost hands back exactly `fade`, matching the shapes beside it.
    float4 color = recolor.a >= 0.0 ? float4(recolor.rgb, recolor.a * g.Place.w) : g.Color;
    o.Color = float4(color.rgb, color.a * pow(fade, 2.2));
    o.PaintProgram = g.Paint.x;
    o.PaintPoint = paintPoint;
    o.OwnFade = g.Paint.y * pow(fade, 2.2);
    // The clip's shape, from the table by the slot the record carries - one fetch per instance, as everywhere else.
    o.Layer = g.Params.y;   // the atlas layer this glyph was packed into
    o.Embolden = g.Clip.y;
    o.Soft = g.Paint.z;
    o.ReflectY = corner.y;
    o.Reflection = float2(g.Paint.w, g.Second.w);
    ClipFromSlot(g.Clip.x, o.ClipBox, o.ClipRadii);
    return o;
}

technique FontBatch
{
    pass RenderMsdf
    {
        EffectName = "FontEffectMsdf";
        VertexShader = FontVertexShader;
        PixelShader = FontPixelShaderMsdf;
    }

    pass RenderMsdfBatchInstanced
    {
        EffectName = "FontEffectMsdfBatchInstanced";
        VertexShader = FontBatchInstancedVS;
        PixelShader = FontPixelShaderMsdfBatch;
    }
}
