// Per-instance sprite data (one element = one sprite quad). The quad corners come from SV_VertexID: plain instanced
// rendering, a 4-vertex triangle strip x N instances.
struct SpriteItem
{
    float4 Destination: Position;
    float4 Source: TEXCOORD0;
    float2 Origin:TEXCOORD1;
    float Depth : PSIZE0;
    float Rotation : PSIZE1;
    float4 Color: COLOR0;
    int4 TextureInfo : BINORMAL0;
    int SpriteEffect : BLENDINDICES0;
};

struct PSInput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
    float4 Color : COLOR;
    nointerpolation int Textured : TEXCOORD1;
    nointerpolation uint Slot : TEXCOORD2;
};

// Every texture of the resource heap - or of the shared texture set - by its slot there: each sprite names its own,
// so a batch of any textures is one draw. A set of its own, which descriptor sets bind apart from the other resources.
Texture2D spriteTextures[] : register(t0, space1);
SamplerState TextureSampler;
float2 TextureCornerCoords[4];
matrix MatrixTransform;

PSInput SpriteVertexShader(SpriteItem item, uint vertexId : SV_VertexID)
{
    PSInput vertex;
    int corner = (int)vertexId;
    float2 origin = item.Origin;

    // The source rectangle normalized by the texture's size.
    float2 inv = 1.0 / (float2)item.TextureInfo.xy;
    float2 sourceXY = item.Source.xy * inv;
    float2 sourceZW = item.Source.zw * inv;

    float2 rotation = float2(cos(item.Rotation), sin(item.Rotation));
    float2 cornerCoord = TextureCornerCoords[corner];

    // Size of the sprite at this corner, then the corner's position relative to the rotation origin.
    float2 size = cornerCoord * item.Destination.zw;
    float2 position = size - origin;

    [flatten]
    if (item.Rotation != 0.0)
    {
        vertex.Position.x = item.Destination.x + (position.x * rotation.x) - (position.y * rotation.y);
        vertex.Position.y = item.Destination.y + (position.x * rotation.y) + (position.y * rotation.x);

        // We subtracted the origin above; move the point back to its original place.
        vertex.Position.xy += origin;
    }
    else
    {
        vertex.Position.xy = item.Destination.xy + size;
    }

    vertex.Position.z = item.Depth;
    vertex.Position.w = 1;
    vertex.Color = item.Color;

    float2 uvCorner = TextureCornerCoords[corner ^ item.SpriteEffect];
    vertex.UV = sourceXY + uvCorner * sourceZW;

    vertex.Textured = item.TextureInfo.z;
    vertex.Slot = (uint)item.TextureInfo.w;

    vertex.Position = mul(vertex.Position, MatrixTransform);
    return vertex;
}

float4 SpritePixelShader(PSInput input) : SV_TARGET
{
    if (input.Textured < 0)
    {
        return input.Color;
    }

    return spriteTextures[NonUniformResourceIndex(input.Slot)].Sample(TextureSampler, input.UV) * input.Color;
}

technique SpriteBatch
{
    pass Render
    {
        VertexShader = SpriteVertexShader;
        PixelShader = SpritePixelShader;
    }
}
