// The editor's ground grid: a square laid flat on the ground round its center, ruled into cells by the pixel shader.
// Everything is in the camera-relative space the eye sits at the origin of.

// Places the square.
float4x4 placement;
float4x4 viewProjection;
// The eye's place in the plane modulo the cells of each level, 1, 10, 100... times the smallest, two levels to an entry
// (xy, then zw): worked out on the CPU, so what a level's lines are drawn from stays small however far the eye has gone.
float4 phases[4];
// x: the smallest cell; y: how far from its center the grid reaches; zw: its center relative to the eye, along x and z.
float4 layout;
// xy: where the X axis (z = 0) and the Z axis (x = 0) cross the plane relative to the eye, along x and z, held to the
// grid's reach; z: a line's width in pixels; w: how wide a level's cells are on screen, in pixels, when it starts to show.
float4 lines;
// The lines of the smallest cells shown and of those ten times bigger and up; the alphas are their strengths.
float4 minorColor;
float4 majorColor;
float4 axisXColor;
float4 axisZColor;
// The floor under the lines; alpha 0 leaves none.
float4 floorColor;

struct MESH_VERTEX
{
    float4 position : POSITION;
    float4 color : COLOR;
    float3 normal : NORMAL;
    float2 uv0 : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
    float2 uv3 : TEXCOORD3;
    float4 tan : TANGENT;
    float3 biTangent : BINORMAL0;
};

struct PS_INPUT
{
    float4 position : SV_POSITION;
    float2 plane : TEXCOORD0;
};

// How far behind the ground the grid is drawn, in depth: a few steps of a 32-bit depth buffer by 1.0, so a floor lying
// at y = 0 hides it instead of fighting it, and nothing nearer than that - nothing the buffer can tell from the ground -
// shows through it. Added as a part of w, so the depth stays linear across the square. A fixed depth is a distance that
// grows with the square of the eye's: 2e-5 was metres a hundred away, and from below what stood on the ground showed
// through the floor. The draw holds each fragment's depth at the far plane (depth clamp), so the ground past it still
// shows, behind everything.
static const float BehindTheGround = 4.0f / 16777216.0f;

[shader("vertex")]
PS_INPUT Ground_VS(MESH_VERTEX input)
{
    PS_INPUT output;

    float4 world = mul(float4(input.position.xyz, 1.0f), placement);
    output.position = mul(world, viewProjection);
    output.position.z += BehindTheGround * output.position.w;
    output.plane = world.xz;
    return output;
}

static const int Levels = 8;

float2 PhaseOf(int level)
{
    int at = min(level, Levels - 1);
    float4 pair = phases[at / 2];
    return (at & 1) == 0 ? pair.xy : pair.zw;
}

// How much of a pixel the nearest line of cells `cell` wide covers, for lines `halfWidth` pixels to each side.
float Ruled(float2 at, float2 perPixel, float cell, float halfWidth)
{
    float2 toLine = abs(frac(at / cell - 0.5f) - 0.5f) * cell / perPixel;
    return saturate(halfWidth + 0.5f - min(toLine.x, toLine.y));
}

// A level's strength by how far up it sits: fading in from nothing below the smallest shown cell, minor at the smallest,
// major from the next one up; continuous, so a level grows into the next as the eye moves away.
float Strength(float rise)
{
    return rise < 0.0f ? minorColor.a * (1.0f + rise)
        : rise < 1.0f ? lerp(minorColor.a, majorColor.a, rise)
        : majorColor.a;
}

// Lines of cells 1, 10, 100... times the smallest, the level shown by how big its cells are on screen, and the two axes
// over them; all of it fades away towards the grid's reach.
[shader("fragment")]
float4 Ground_PS(PS_INPUT input) : SV_TARGET
{
    float2 perPixel = max(fwidth(input.plane), 1e-6f);
    float halfWidth = lines.z * 0.5f;

    float level = max(0.0f, log10(max(perPixel.x, perPixel.y) * lines.w / layout.x));
    float step = floor(level);
    float rise = level - step;
    float cell = layout.x * pow(10.0f, step);

    float ruled = 0.0f;
    float major = 0.0f;
    for (int k = 0; k < 3; k++)
    {
        // A level past the phases kept has no place in the world to be drawn at.
        if ((int)step + k >= Levels)
        {
            break;
        }

        float2 at = input.plane + PhaseOf((int)step + k);
        float covered = Ruled(at, perPixel, cell, halfWidth) * Strength(k - rise);
        ruled = max(ruled, covered);
        major = max(major, covered * saturate(k - rise));
        cell *= 10.0f;
    }

    // Premultiplied, the lines over the floor and each axis over what is under it: an axis' smoothed edge keeps its own
    // hue.
    float3 lineColor = lerp(minorColor.rgb, majorColor.rgb, ruled > 0.0f ? major / ruled : 0.0f);
    float4 shade = float4(lineColor * ruled, ruled) + float4(floorColor.rgb * floorColor.a, floorColor.a) * (1.0f - ruled);

    float alongX = saturate(halfWidth + 0.5f - abs(input.plane.y - lines.y) / perPixel.y) * axisXColor.a;
    float alongZ = saturate(halfWidth + 0.5f - abs(input.plane.x - lines.x) / perPixel.x) * axisZColor.a;
    shade = float4(axisXColor.rgb * alongX, alongX) + shade * (1.0f - alongX);
    shade = float4(axisZColor.rgb * alongZ, alongZ) + shade * (1.0f - alongZ);

    shade *= 1.0f - smoothstep(layout.y * 0.4f, layout.y, length(input.plane - layout.zw));
    if (shade.a <= 0.0f)
    {
        discard;
    }

    return float4(shade.rgb / shade.a, shade.a);
}

technique Grid
{
    pass Ground
    {
        VertexShader = Ground_VS;
        PixelShader = Ground_PS;
    }
}
