namespace Adamantium.Pdf.Shadings;

/// <summary>A triangle of a mesh shading; its color is interpolated between its vertices.</summary>
public readonly record struct PdfMeshTriangle(PdfMeshVertex A, PdfMeshVertex B, PdfMeshVertex C);
