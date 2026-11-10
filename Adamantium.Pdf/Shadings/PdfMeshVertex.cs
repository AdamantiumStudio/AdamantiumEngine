using Adamantium.Pdf.Document;

namespace Adamantium.Pdf.Shadings;

/// <summary>A vertex of a mesh shading: where it is in the shading's space, and its color values - the components, or
/// the one parameter of the shading's function.</summary>
public readonly record struct PdfMeshVertex(PdfPoint Position, double[] Values);
