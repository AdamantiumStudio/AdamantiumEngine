using System;
using System.Collections.Generic;
using System.Numerics;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Rectangle = Adamantium.Mathematics.Rectangle<int>;

namespace Adamantium.Engine;

public class TextLayout
{
    private IFont font;
    private string inputString;
    private float fontSizeInPixels;
    private Rectangle<int> boundingRectangle;
    public float Scale { get; }

    public Mesh Mesh { get; private set; }

    public TextLayout(IFont font, string inputString, float fontSizeInPixels, Rectangle<int> boundingRectangle)
    {
        this.font = font;
        this.inputString = inputString;
        this.fontSizeInPixels = fontSizeInPixels;
        this.boundingRectangle = boundingRectangle;
        Mesh = new Mesh();
        Scale = fontSizeInPixels / font.UnitsPerEm;

        ProcessString();
    }

    private void ProcessString()
    {
        // var glyphs = font.TranslateIntoGlyphs(inputString);
        // float stringWidth = 0;
        //
        // foreach (var glyph in glyphs)
        // {
        //     var outlines = glyph.Triangulate(2);
        //
        //     if (glyph.HasOutlines)
        //     {
        //         var mesh = new Mesh();
        //         mesh.SetPositions(outlines);
        //
        //         var translationMatrix = Matrix4x4F.Identity;
        //         //translationMatrix.ScaleVector = new Vector3F(Scale, Scale, 1);
        //         translationMatrix.TranslationVector = new Vector3F(stringWidth, 1000, 0);
        //         // var scale = Matrix4x4F.Scaling(Scale, Scale, 1);
        //         // var result = scale * translationMatrix;
        //         mesh.ApplyTransform(translationMatrix);
        //         Mesh = Mesh.Merge(mesh);
        //     }
        //
        //     stringWidth += glyph.AdvanceWidth/* - glyph.LeftSideBearing*/;
        // }
        //
        // Mesh.Optimize(false, false);
    }
}