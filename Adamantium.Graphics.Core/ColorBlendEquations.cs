using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics.Core;

public static class ColorBlendEquations
{
    public static ColorBlendEquationEXT Opaque {get; private set;}
    
    public static ColorBlendEquationEXT Fonts {get; private set;}
    public static ColorBlendEquationEXT Fonts2 {get; private set;}
    
    public static ColorBlendEquationEXT AlphaBlend {get; private set;}
    
    public static ColorBlendEquationEXT LightMap {get; private set;}
    
    public static ColorBlendEquationEXT Additive {get; private set;}
    
    public static ColorBlendEquationEXT NonPremultiplied {get; private set;}

    // Premultiplied-alpha "over": rgb = src + dst*(1-srcA), a = srcA + dstA*(1-srcA). Use this whenever the
    // pixel shader already outputs premultiplied color (rgb * a), e.g. the font shaders rendering into the
    // intermediate text target and that target being composited back. Using a straight-alpha blend
    // (SrcAlpha,...) on premultiplied output multiplies by alpha a second time -> dark rim around glyphs.
    public static ColorBlendEquationEXT Premultiplied {get; private set;}

    /// <summary>What is drawn already, color and alpha alike, times the source alpha: rgb = dst*srcA, a = dstA*srcA. Fades
    /// a premultiplied target as a whole.</summary>
    public static ColorBlendEquationEXT TimesSourceAlpha {get; private set;}

    public static ColorBlendEquationEXT Default => Opaque;

    static ColorBlendEquations()
    {
        Opaque = New(BlendFactor.One, BlendFactor.Zero, BlendOp.Add, BlendOp.Add);
        Fonts = New(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOp.Add, BlendOp.Subtract);
        Fonts2 = New2();
        // Straight-alpha "over": rgb = src*srcA + dst*(1-srcA), a = srcA + dstA*(1-srcA). The alpha-channel factors
        // MUST be (One, OneMinusSrcAlpha) — with the default (One, Zero) the destination alpha is replaced by the
        // source alpha, so a transparent source pixel (srcA=0) zeroes an opaque background's alpha and punches a hole
        // through it (the transparent-texture-over-background artifact).
        AlphaBlend = New(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOp.Add, BlendOp.Add,
            BlendFactor.One, BlendFactor.OneMinusSrcAlpha);
        LightMap = New(BlendFactor.One, BlendFactor.One, BlendOp.Add, BlendOp.Add);
        Additive = New(BlendFactor.SrcAlpha, BlendFactor.One, BlendOp.Add, BlendOp.Add);
        NonPremultiplied = New(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha, BlendOp.Add, BlendOp.Add);
        Premultiplied = New(BlendFactor.One, BlendFactor.OneMinusSrcAlpha, BlendOp.Add, BlendOp.Add,
            BlendFactor.One, BlendFactor.OneMinusSrcAlpha);
        TimesSourceAlpha = New(BlendFactor.Zero, BlendFactor.SrcAlpha, BlendOp.Add, BlendOp.Add,
            BlendFactor.Zero, BlendFactor.SrcAlpha);
    }

    private static ColorBlendEquationEXT New(
        BlendFactor sourceBlend, 
        BlendFactor destinationBlend, 
        BlendOp colorBlendOp, 
        BlendOp alphaBlendOp,
        BlendFactor srcAlpha = BlendFactor.One, 
        BlendFactor dstAlpha = BlendFactor.Zero)
    {
        var equation = new ColorBlendEquationEXT();
        equation.SrcColorBlendFactor = sourceBlend;
        equation.DstColorBlendFactor = destinationBlend;
        equation.ColorBlendOp = colorBlendOp;
        
        equation.SrcAlphaBlendFactor = srcAlpha;
        equation.DstAlphaBlendFactor = dstAlpha;
        equation.AlphaBlendOp = alphaBlendOp;

        return equation;
    }
    
    private static ColorBlendEquationEXT New2()
    {
        var equation = new ColorBlendEquationEXT();
        equation.SrcColorBlendFactor = BlendFactor.SrcAlpha;
        equation.SrcAlphaBlendFactor = BlendFactor.One;
        equation.DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha;
        equation.DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha;
        equation.ColorBlendOp = BlendOp.Add;
        equation.AlphaBlendOp = BlendOp.Add;

        return equation;
    }
}