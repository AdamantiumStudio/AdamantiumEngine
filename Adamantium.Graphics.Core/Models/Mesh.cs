using System.Collections.Generic;
using System.Linq;
using Adamantium.Core;
using Adamantium.Core.Collections;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Triangulation;

namespace Adamantium.Graphics.Core.Models;

public class Mesh
{
    private const int StripIndicesSeparator = -1;
    private MeshContour defaultMeshContour;
    private bool isModified;
    private int version;
    private TriangleTree triangleTree;
    private int triangleTreeVersion;
    private ConvexHull convexHull;
    private int convexHullVersion;
    private Vector3[] convexHullPoints;

    public static int RoundPrecision => 4;
    
    public string Name { get; set; }

    public Mesh() : this(PrimitiveType.TriangleList)
    {
        
    }
    
    public Mesh(PrimitiveType primitiveType)
    {
        Initialize();
        defaultMeshContour = new MeshContour();
        MeshTopology = primitiveType;
    }

    private void Initialize()
    {
        MeshTopology = PrimitiveType.TriangleList;
        UpAxis = UpAxis.Y_DOWN_RH;

        Points = [];
        Colors = [];
        Normals = [];
        UV0 = [];
        UV1 = [];
        UV2 = [];
        UV3 = [];
        Tangents = [];
        BiTangents = [];
        JointWeights = [];
        JointIndices = [];
        Indices = [];

        Contours = [];
    }

    public int ContoursCount => Contours.Count;
        
    public AdamantiumCollection<MeshContour> Contours { get; private set; }

    public Vector3[] Points { get; private set; }
    
    public Color[] Colors { get; private set; }

    public Vector3F[] Normals { get; private set; }

    public Vector2F[] UV0 { get; private set; }

    public Vector2F[] UV1 { get; private set; }

    public Vector2F[] UV2 { get; private set; }

    public Vector2F[] UV3 { get; private set; }

    public Vector4F[] Tangents { get; private set; }

    public Vector3F[] BiTangents { get; private set; }

    public Vector4F[] JointIndices { get; private set; }

    public Vector4F[] JointWeights { get; private set; }

    public int[] Indices { get; private set; }
        
    public Bounds Bounds { get; private set; }

    public PrimitiveType MeshTopology { get; set; }

    public bool IsOptimized { get; private set; }

    public bool IsModified
    {
        get => isModified;
        private set
        {
            isModified = value;
            if (value)
            {
                version++;
            }
        }
    }

    /// <summary>Counts the changes to the mesh: whoever keeps a copy of it - GPU buffers, a cache - holds the version it
    /// copied and makes the copy again when this one differs, each on its own.</summary>
    public int Version => version;

    public string MaterialID { get; set; }

    public VertexSemantic Semantic { get; private set; }

    public UpAxis UpAxis { get; set; }

    public bool IsNormalsPresent => Semantic.HasFlag(VertexSemantic.Normal);
        
    public bool IsUv0Present => Semantic.HasFlag(VertexSemantic.UV0);
        
    public bool IsUv1Present => Semantic.HasFlag(VertexSemantic.UV1);
        
    public bool IsUv2Present => Semantic.HasFlag(VertexSemantic.UV2);
        
    public bool IsUv3Present => Semantic.HasFlag(VertexSemantic.UV3);
        
    public bool IsColorPresent => Semantic.HasFlag(VertexSemantic.Color);

    public bool IsTangetBinormalsPresent => Semantic.HasFlag(VertexSemantic.TangentBiNormal);

    public bool IsAnimationPresent => Semantic.HasFlag(VertexSemantic.JointIndices) &&
                                      Semantic.HasFlag(VertexSemantic.JointWeights);
        
    public void AcceptChanges()
    {
        IsModified = false;
    }

    /// <summary>
    /// The mesh's triangles sorted for ray tests: built on first use and again after the geometry changes, so a mesh
    /// that is never picked never pays for it.
    /// </summary>
    public TriangleTree GetTriangleTree()
    {
        if (triangleTree == null || triangleTreeVersion != version || !triangleTree.Describes(Points, Indices, MeshTopology))
        {
            triangleTree = new TriangleTree(Points, Indices, MeshTopology);
            triangleTreeVersion = version;
        }

        return triangleTree;
    }

    /// <summary>
    /// The convex hull of the mesh's points, in the mesh's own space: built on first use and again after the points
    /// change, and shared by every entity that draws this mesh.
    /// </summary>
    public ConvexHull GetConvexHull()
    {
        if (convexHull == null || convexHullVersion != version || !ReferenceEquals(convexHullPoints, Points))
        {
            var points = new Vector3F[Points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = Points[i];
            }

            convexHull = ConvexHull.FromPoints(points);
            convexHullVersion = version;
            convexHullPoints = Points;
        }

        return convexHull;
    }

    public bool HasIndices => Indices.Length > 0;

    public bool HasPoints => Points.Length > 0;

    public Mesh SetTopology(PrimitiveType topology)
    {
        MeshTopology = topology;
        return this;
    }
    
    public void AddContour(IEnumerable<Vector3> inPoints, bool isGeometryClosed, bool generateSegments = true)
    {
        Contours.Add(new MeshContour(inPoints, isGeometryClosed));
    }

    public void AddContour(IEnumerable<Vector2> inPoints, bool isGeometryClosed, bool generateSegments = true)
    {
        var points = inPoints as Vector2[] ?? inPoints?.ToArray();
        if (points == null || points.Length == 0) return;

        Contours.Add(new MeshContour(points, isGeometryClosed));
    }

    public void AddContour(IEnumerable<GeometrySegment> segments)
    {
        Contours.Add(new MeshContour(segments));
    }
    
    public void AddContour(MeshContour contour)
    {
        Contours.Add(contour);
    }
    
    public void AddContours(IEnumerable<MeshContour> contours)
    {
        Contours.AddRange(contours);
    }

    public MeshContour GetContour(int contour)
    {
        if (contour >= ContoursCount) return defaultMeshContour;
            
        return Contours[contour];
    }

    public List<GeometryIntersection> MergeGeometryContourPoints()
    {
        var pointsHashSet = new HashSet<GeometryIntersection>();
        var geometryPoints = new List<GeometryIntersection>();

        foreach (var contour in Contours)
        {
            foreach (var point in contour.GeometryPoints)
            {
                if (!pointsHashSet.Contains(point))
                {
                    pointsHashSet.Add(point);
                    geometryPoints.Add(point);
                }
            }
        }

        return geometryPoints;
    }
    
    public List<Vector2> MergeContourPoints()
    {
        var pointsHashSet = new HashSet<Vector2>();
        var points = new List<Vector2>();

        foreach (var contour in Contours)
        {
            foreach (var point in contour.Points)
            {
                if (!pointsHashSet.Contains(point))
                {
                    pointsHashSet.Add(point);
                    points.Add(point);
                }
            }
        }

        return points;
    }

    public List<GeometrySegment> MergeContourSegments()
    {
        var segments = new List<GeometrySegment>();
        foreach (var contour in Contours)
        {
            segments.AddRange(contour.Segments);
        }

        return segments;
    }

    public Mesh SetPoints(IEnumerable<Vector2> points, bool updateBoundingBox = true)
    {
        if (points == null) return this;

        return SetPoints(Utilities.ToVector3(points), updateBoundingBox);
    }
        
    public Mesh SetPoints(IEnumerable<Vector3> points, bool updateBoundingBox = true)
    {
        if (points == null) return this;

        var pts = points as Vector3[] ?? points.ToArray();
        Points = pts;

        if (updateBoundingBox)
        {
            CalculateBoundingVolumes();
        }
        Semantic |= VertexSemantic.Position;
        IsModified = true;

        return this;
    }
        
    public Mesh SetIndices(IEnumerable<int> indices)
    {
        if (indices == null) return this;

        Indices = indices as int[] ?? indices.ToArray();
        IsModified = true;

        return this;
    }

    public Mesh SetNormals(IEnumerable<Vector3F> inNormals)
    {
        if (inNormals == null)
        {
            return this;
        }

        Normals = inNormals as Vector3F[] ?? inNormals.ToArray();
        Semantic |= VertexSemantic.Normal;
        IsModified = true;

        return this;
    }

    public Mesh SetColors(IEnumerable<Color> inColors)
    {
        if (inColors == null) return this;

        Colors = inColors as Color[] ?? inColors.ToArray();
        Semantic |= VertexSemantic.Color;
        IsModified = true;

        return this;
    }

    public Mesh SetUVs(int channel, IEnumerable<Vector2F> uvs)
    {
        if (uvs == null) return this;
            
        var uv = uvs as Vector2F[] ?? uvs.ToArray();

        switch (channel)
        {
            case 0:
                UV0 = uv;
                IsModified = true;
                Semantic |= VertexSemantic.UV0;
                break;
            case 1:
                UV1 = uv;
                IsModified = true;
                Semantic |= VertexSemantic.UV1;
                break;
            case 2:
                UV2 = uv;
                IsModified = true;
                Semantic |= VertexSemantic.UV2;
                break;
            case 3:
                UV3 = uv;
                IsModified = true;
                Semantic |= VertexSemantic.UV3;
                break;
        }

        return this;
    }

    public Mesh SetTangents(IEnumerable<Vector4F> inTangents)
    {
        if (inTangents == null) return this;
            
        Tangents = inTangents as Vector4F[] ?? inTangents.ToArray();
        Semantic |= VertexSemantic.TangentBiNormal;
        IsModified = true;

        return this;
    }

    public Mesh SetTangentsAndBiTangents(IEnumerable<Vector4F> inTangents, IEnumerable<Vector3F> inBiTangents)
    {
        if (inTangents == null || inBiTangents == null) return this;

        Tangents = inTangents as Vector4F[] ?? inTangents.ToArray();
        BiTangents = inBiTangents as Vector3F[] ?? inBiTangents.ToArray();
        Semantic |= VertexSemantic.TangentBiNormal;
        IsModified = true;

        return this;
    }

    public Mesh SetJointIndices(IEnumerable<Vector4F> inJointIndices)
    {
        if (inJointIndices == null) return this;
            
        JointIndices = inJointIndices as Vector4F[] ?? inJointIndices.ToArray();
        Semantic |= VertexSemantic.JointIndices;
        IsModified = true;

        return this;
    }

    public Mesh SetJointWeights(IEnumerable<Vector4F> inJointWeights)
    {
        if (inJointWeights == null) return this;

        JointWeights = inJointWeights as Vector4F[] ?? inJointWeights.ToArray();
        Semantic |= VertexSemantic.JointWeights;
        IsModified = true;

        return this;
    }

    public Mesh ClearUVs(int channel)
    {
        switch (channel)
        {
            case 0:
                UV0 = null;
                Semantic &= ~VertexSemantic.UV0;
                IsModified = true;
                break;
            case 1:
                UV1 = null;
                Semantic &= ~VertexSemantic.UV1;
                IsModified = true;
                break;
            case 2:
                UV2 = null;
                Semantic &= ~VertexSemantic.UV2;
                IsModified = true;
                break;
            case 3:
                UV3 = null;
                Semantic &= ~VertexSemantic.UV3;
                IsModified = true;
                break;
        }

        return this;
    }

    public Mesh ClearJoints()
    {
        JointIndices = null;
        JointWeights = null;
        Semantic &= ~VertexSemantic.JointIndices & ~VertexSemantic.JointWeights;

        return this;
    }

    public Mesh ClearColors()
    {
        Colors = null;
        Semantic &= ~VertexSemantic.Color;

        return this;
    }

    public Mesh ClearNormals()
    {
        Normals = [];
        Semantic &= ~VertexSemantic.Normal;
        IsModified = true;

        return this;
    }

    public Mesh ClearContours()
    {
        Contours?.Clear();

        return this;
    }

    /// <summary>
    /// A line mesh through this mesh's contours, for an outline drawn in 3D: the contours themselves are flat and only an
    /// outline stroke reads them.
    /// </summary>
    public Mesh ContoursToLines()
    {
        var points = new List<Vector3>();
        foreach (var contour in Contours)
        {
            var outline = contour.Points;
            var count = contour.IsGeometryClosed ? outline.Length : outline.Length - 1;
            for (int i = 0; i < count; i++)
            {
                var next = outline[(i + 1) % outline.Length];
                points.Add(new Vector3(outline[i].X, outline[i].Y, 0));
                points.Add(new Vector3(next.X, next.Y, 0));
            }
        }

        return new Mesh(PrimitiveType.LineList).SetPoints(points);
    }

    public Mesh Merge(Mesh mesh)
    {
        return Merge(new []{new MergeInstance(mesh, Matrix4x4.Identity, false)});
    }

    public Mesh Merge(params Mesh[] meshes)
    {
        MergeInstance[] instances = new MergeInstance[meshes.Length];
        for (var i = 0; i < meshes.Length; i++)
        {
            var mesh = meshes[i];
            var mergeInstance = new MergeInstance(mesh, Matrix4x4.Identity, false);
            instances[i] = mergeInstance;
        }
        return Merge(instances);
    }

    /// <summary>
    /// Appends the instances' geometry. Normals survive only when every part has them; texture coordinates when any part
    /// has them, zero where a part has none. What is not merged is dropped rather than left out of step with the points.
    /// </summary>
    public Mesh Merge(MergeInstance[] instances, bool forceOptimization = false)
    {
        var positions = new List<Vector3>(Points);
        var normals = new List<Vector3F>(Normals.Length == Points.Length ? Normals : []);
        var everyHasNormals = Normals.Length == Points.Length;
        var uv0 = new List<Vector2F>(UV0.Length == Points.Length ? UV0 : new Vector2F[Points.Length]);
        var anyHasUv0 = UV0.Length == Points.Length && Points.Length > 0;
        List<int> indices = new List<int>(Indices);
        int indexShift = Points.Length;
        foreach (var instance in instances)
        {
            if (instance?.Mesh == null)
            {
                continue;
            }

            if (forceOptimization && !instance.Mesh.IsOptimized)
            {
                instance.Mesh.Optimize();
            }

            var mesh = instance.Mesh;
            var transformed = mesh.Points;
            if (instance.ApplyTransform)
            {
                transformed = ApplyTransform(transformed, instance.Transform);
            }

            if (!mesh.HasIndices)
            {
                mesh.GenerateBasicIndices();
            }

            positions.AddRange(transformed);

            if (everyHasNormals && mesh.Normals.Length == mesh.Points.Length)
            {
                normals.AddRange(instance.ApplyTransform ? TransformedNormals(mesh.Normals, instance.Transform) : mesh.Normals);
            }
            else
            {
                everyHasNormals = false;
            }

            if (mesh.UV0.Length == mesh.Points.Length && mesh.Points.Length > 0)
            {
                uv0.AddRange(mesh.UV0);
                anyHasUv0 = true;
            }
            else
            {
                uv0.AddRange(new Vector2F[mesh.Points.Length]);
            }

            int startIndex = indices.Count;
            indices.AddRange(mesh.Indices);
            if (instance.ApplyTransform && instance.Transform.Determinant() < 0)
            {
                if (MeshTopology == PrimitiveType.TriangleList)
                {
                    for (var i = startIndex; i + 2 < indices.Count; i += 3)
                    {
                        (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                    }
                }
                else if (MeshTopology == PrimitiveType.TriangleStrip)
                {
                    TurnStripOver(indices, startIndex);
                }
            }

            if (MeshTopology == PrimitiveType.LineStrip || MeshTopology == PrimitiveType.TriangleStrip)
            {
                indices.Add(StripIndicesSeparator);
            }
            ShiftIndices(indices, startIndex, indexShift);
            indexShift += mesh.Points.Length;
        }

        SetPoints(positions);
        SetIndices(indices);

        if (everyHasNormals && normals.Count > 0)
        {
            SetNormals(normals);
        }
        else
        {
            Normals = [];
            Semantic &= ~VertexSemantic.Normal;
        }

        if (anyHasUv0)
        {
            SetUVs(0, uv0);
        }
        else
        {
            UV0 = [];
            Semantic &= ~VertexSemantic.UV0;
        }

        DropAttributesOutOfStep();

        if (forceOptimization)
        {
            Optimize();
        }

        return this;
    }

    private static Vector3F[] TransformedNormals(Vector3F[] normals, Matrix4x4 transform)
    {
        if (transform.IsIdentity)
        {
            return normals;
        }

        var normalMatrix = Matrix4x4.Transpose(Matrix4x4.Invert(transform));
        var turned = new Vector3F[normals.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            var normal = (Vector3)normals[i];
            Vector3.TransformNormal(ref normal, ref normalMatrix, out normal);
            turned[i] = (Vector3F)Vector3.Normalize(normal);
        }

        return turned;
    }

    private void DropAttributesOutOfStep()
    {
        var count = Points.Length;
        if (Colors.Length != count)
        {
            Colors = [];
            Semantic &= ~VertexSemantic.Color;
        }

        if (UV1.Length != count)
        {
            UV1 = [];
            Semantic &= ~VertexSemantic.UV1;
        }

        if (UV2.Length != count)
        {
            UV2 = [];
            Semantic &= ~VertexSemantic.UV2;
        }

        if (UV3.Length != count)
        {
            UV3 = [];
            Semantic &= ~VertexSemantic.UV3;
        }

        if (Tangents.Length != count || BiTangents.Length != count)
        {
            Tangents = [];
            BiTangents = [];
            Semantic &= ~VertexSemantic.TangentBiNormal;
        }

        if (JointIndices.Length != count || JointWeights.Length != count)
        {
            JointIndices = [];
            JointWeights = [];
            Semantic &= ~(VertexSemantic.JointIndices | VertexSemantic.JointWeights);
        }
    }

    private void ShiftIndices(List<int> indices, int startIndex, int shiftValue)
    {
        for (int i = startIndex; i < indices.Count; i++)
        {
            if (indices[i] < 0)
            {
                continue;
            }
            indices[i] += shiftValue;
        }
    }

    public static Vector3[] ApplyTransform(IEnumerable<Vector3> positions, Matrix4x4 transform)
    {
        var points = positions as Vector3[] ?? positions.ToArray();
        
        if (transform.IsIdentity)
        {
            return points;
        }
        
        var transformed = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var position = points[i];
            Vector3.TransformCoordinate(ref position, ref transform, out position);
            transformed[i] = Vector3.Round(position, RoundPrecision);
        }

        return transformed;
    }

    public Mesh ApplyTransform(Matrix4x4? transform)
    {
        if (!transform.HasValue || transform.Value.IsIdentity)
        {
            return this;
        }

        var transformMatrix = transform.Value;
            
        for (int i = 0; i < Points.Length; i++)
        {
            var position = Points[i];
            Vector3.TransformCoordinate(ref position, ref transformMatrix, out position);
            Points[i] = Vector3.Round(position, RoundPrecision);
        }

        foreach (var meshContour in Contours)
        {
            var points = meshContour.Points;

            var transformed = new Vector2[points.Length];
            Vector2.TransformCoordinate(points, ref transformMatrix, transformed);
            meshContour.SetPoints(transformed, false);
        }

        if (transformMatrix.Determinant() < 0)
        {
            ReverseWinding();
        }

        // Normals the mesh already CARRIES are moved, not re-derived: a generator's exact normals survive being placed,
        // and a plain translation leaves them alone. Re-averaging them off the triangles here threw that away.
        if (Normals.Length == Points.Length) TransformNormals(ref transformMatrix);
        else CalculateNormals();

        CalculateTangentsAndBinormals();
        CalculateBoundingVolumes();

        IsModified = true;

        return this;
    }

    // Normals follow the inverse transpose, which is what keeps them perpendicular through a non-uniform scale.
    private void TransformNormals(ref Matrix4x4 transform)
    {
        Normals = TransformedNormals(Normals, transform);
    }

    public void Clear()
    {
        Initialize();
    }

    public Mesh Clone()
    {
        return Clone(Matrix4x4.Identity);
    }

    /// <summary>
    /// Takes over everything <paramref name="source"/> holds - a mesh just generated and not used elsewhere - so the
    /// geometry changes in place: whatever draws this mesh uploads it again rather than holding a new mesh besides.
    /// </summary>
    public void ReplaceWith(Mesh source)
    {
        MeshTopology = source.MeshTopology;
        Contours = source.Contours;
        Points = source.Points;
        Colors = source.Colors;
        Normals = source.Normals;
        UV0 = source.UV0;
        UV1 = source.UV1;
        UV2 = source.UV2;
        UV3 = source.UV3;
        Tangents = source.Tangents;
        BiTangents = source.BiTangents;
        JointIndices = source.JointIndices;
        JointWeights = source.JointWeights;
        Indices = source.Indices;
        Bounds = source.Bounds;
        IsOptimized = source.IsOptimized;
        Semantic = source.Semantic;
        UpAxis = source.UpAxis;
        IsModified = true;
    }

    public Mesh Clone(Matrix4x4 transform)
    {
        Mesh clonedMesh = new Mesh();
        clonedMesh.MeshTopology = MeshTopology;

        clonedMesh.Points = new Vector3[Points.Length];
        Points.CopyTo(clonedMesh.Points, 0);

        clonedMesh.Indices = new int[Indices.Length];
        Indices.CopyTo(clonedMesh.Indices, 0);

        clonedMesh.UV0 = new Vector2F[UV0.Length];
        UV0.CopyTo(clonedMesh.UV0, 0);

        clonedMesh.UV1 = new Vector2F[UV1.Length];
        UV1.CopyTo(clonedMesh.UV1, 0);

        clonedMesh.UV2 = new Vector2F[UV2.Length];
        UV2.CopyTo(clonedMesh.UV2, 0);

        clonedMesh.UV3 = new Vector2F[UV3.Length];
        UV3.CopyTo(clonedMesh.UV3, 0);

        clonedMesh.Colors = new Color[Colors.Length];
        Colors.CopyTo(clonedMesh.Colors, 0);

        // Copied whatever the transform is - ApplyTransform moves them. Skipping the copy for a placed clone left it
        // with no normals to move, so every placed copy fell back to averaging them out of its own triangles.
        clonedMesh.Normals = new Vector3F[Normals.Length];
        Normals.CopyTo(clonedMesh.Normals, 0);

        clonedMesh.Tangents = new Vector4F[Tangents.Length];
        Tangents.CopyTo(clonedMesh.Tangents, 0);

        clonedMesh.BiTangents = new Vector3F[BiTangents.Length];
        BiTangents.CopyTo(clonedMesh.BiTangents, 0);

        clonedMesh.JointIndices = new Vector4F[JointIndices.Length];
        JointIndices.CopyTo(clonedMesh.JointIndices, 0);

        clonedMesh.JointWeights = new Vector4F[JointWeights.Length];
        JointWeights.CopyTo(clonedMesh.JointWeights, 0);

        clonedMesh.IsOptimized = IsOptimized;
        clonedMesh.IsModified = true;
        clonedMesh.Semantic = Semantic;
        clonedMesh.UpAxis = UpAxis;

        clonedMesh.ApplyTransform(transform);
        clonedMesh.CalculateBoundingVolumes();
        clonedMesh.IsModified = true;

        return clonedMesh;
    }

    public Mesh Optimize(bool recalculateNormals = true, bool recalculateTangents = true)
    {
        if (!Semantic.HasFlag(VertexSemantic.Position) 
            || MeshTopology == PrimitiveType.LineStrip 
            || MeshTopology == PrimitiveType.TriangleStrip)
        {
            return this;
        }

        if (!HasIndices)
        {
            GenerateBasicIndices();
        }

        var vertexDict = new Dictionary<Vertex, int>();

        int uniqueIndex = 0;
        var optimizedPositions = new List<Vector3>();
        var optimizedNormals = new List<Vector3F>();
        var optimizedUV0 = new List<Vector2F>();
        var optimizedUV1 = new List<Vector2F>();
        var optimizedUV2 = new List<Vector2F>();
        var optimizedUV3 = new List<Vector2F>();
        var optimizedColors = new List<Color>();
        var optimizedBoneIndices = new List<Vector4F>();
        var optimizedBoneWeights = new List<Vector4F>();

        List<int> indices = new List<int>();

        for (int i = 0; i < Indices.Length; ++i)
        {
            int index = Indices[i];
            if (index < 0)
            {
                continue;
            }

            var position = Points[index];
            var normal = Normals != null && Normals.Length - 1 >= index ? Normals[index] : Vector3F.Zero;
            var uv0 = UV0 != null && UV0.Length - 1 >= index ? UV0[index] : Vector2F.Zero;
            var uv1 = UV1 != null && UV1.Length - 1 >= index ? UV1[index] : Vector2F.Zero;
            var uv2 = UV2 != null && UV2.Length - 1 >= index ? UV2[index] : Vector2F.Zero;
            var uv3 = UV3 != null && UV3.Length - 1 >= index ? UV3[index] : Vector2F.Zero;
            Color color = Colors != null && Colors.Length - 1 >= index ? Colors[index] : Mathematics.Colors.White;
            Vector4F jointIndex = JointIndices != null && JointIndices.Length - 1 >= index ? JointIndices[index] : Vector4F.Zero;
            Vector4F jointWeight = JointWeights != null && JointWeights.Length - 1 >= index ? JointWeights[index] : Vector4F.Zero;
            var vertex = new Vertex(position, normal, uv0, uv1, uv2, uv3, color, jointIndex, jointWeight);

            if (!vertexDict.ContainsKey(vertex))
            {
                vertexDict.Add(vertex, uniqueIndex);
                optimizedPositions.Add(position);
                indices.Add(uniqueIndex);
                uniqueIndex++;
                if (Semantic.HasFlag(VertexSemantic.Normal))
                {
                    optimizedNormals.Add(vertex.Normal);
                }
                if (Semantic.HasFlag(VertexSemantic.UV0))
                {
                    optimizedUV0.Add(vertex.UV0);
                }
                if (Semantic.HasFlag(VertexSemantic.UV1))
                {
                    optimizedUV1.Add(vertex.UV1);
                }
                if (Semantic.HasFlag(VertexSemantic.UV2))
                {
                    optimizedUV2.Add(vertex.UV2);
                }
                if (Semantic.HasFlag(VertexSemantic.UV3))
                {
                    optimizedUV3.Add(vertex.UV3);
                }
                if (Semantic.HasFlag(VertexSemantic.Color))
                {
                    optimizedColors.Add(vertex.Color);
                }
                if (Semantic.HasFlag(VertexSemantic.JointIndices))
                {
                    optimizedBoneIndices.Add(vertex.JointIndex);
                }
                if (Semantic.HasFlag(VertexSemantic.JointWeights))
                {
                    optimizedBoneWeights.Add(vertex.JointWeight);
                }
            }
            else
            {
                index = vertexDict[vertex];
                indices.Add(index);
            }
        }

        vertexDict.Clear();

        Points = optimizedPositions.ToArray();
        Indices = indices.ToArray();

        // Normals used not to be carried over: the array stayed on the OLD vertex numbering, i.e. plain garbage.
        if (Semantic.HasFlag(VertexSemantic.Normal))
        {
            Normals = optimizedNormals.ToArray();
        }
        if (Semantic.HasFlag(VertexSemantic.UV0))
        {
            UV0 = optimizedUV0.ToArray();
        }
        if (Semantic.HasFlag(VertexSemantic.UV1))
        {
            UV1 = optimizedUV1.ToArray();
        }
        if (Semantic.HasFlag(VertexSemantic.UV2))
        {
            UV2 = optimizedUV2.ToArray();
        }
        if (Semantic.HasFlag(VertexSemantic.UV3))
        {
            UV3 = optimizedUV3.ToArray();
        }
        if (Semantic.HasFlag(VertexSemantic.Color))
        {
            Colors = optimizedColors.ToArray();
        }
        if (Semantic.HasFlag(VertexSemantic.JointIndices))
        {
            JointIndices = optimizedBoneIndices.ToArray();
        }
        if (Semantic.HasFlag(VertexSemantic.JointWeights))
        {
            JointWeights = optimizedBoneWeights.ToArray();
        }

        CalculateBoundingVolumes();

        if (recalculateNormals)
        {
            CalculateNormals();
        }

        if (recalculateTangents)
        {
            CalculateTangentsAndBinormals();
        }

        return this;
    }

    public Mesh CalculateBoundingVolumes()
    {
        if (Points.Length == 0)
        {
            return this;
        }
            
        Bounds = Bounds.FromPoints(Points);

        return this;
    }

    /// <summary>Normals out of the triangles' front: the side they are clockwise on screen from, which the GPU draws as
    /// the front.</summary>
    public Mesh CalculateNormals(bool smoothNormals = true)
    {
        if (MeshTopology != PrimitiveType.TriangleList || Points.Length < 3 || Indices.Length % 3 != 0)
            return this;

        Vector3F[] normalsNew = new Vector3F[Points.Length];
        for (int i = 0; i < Indices.Length; i += 3)
        {
            var v0 = Points[Indices[i + 1]] - Points[Indices[i]];
            var v1 = Points[Indices[i + 2]] - Points[Indices[i]];
            var n = Vector3F.Cross(v1, v0);
            if (smoothNormals)
            {
                normalsNew[Indices[i]] += n;
                normalsNew[Indices[i + 1]] += n;
                normalsNew[Indices[i + 2]] += n;
            }
            else
            {
                normalsNew[Indices[i]] = n;
                normalsNew[Indices[i + 1]] = n;
                normalsNew[Indices[i + 2]] = n;
            }
        }

        Normals = new Vector3F[normalsNew.Length];
        for (int i = 0; i < normalsNew.Length; ++i)
        {
            Normals[i] = Vector3F.Normalize(normalsNew[i]);
        }

        Semantic |= VertexSemantic.Normal;

        return this;
    }

    public Mesh CalculateTangentsAndBinormals(int uvChannel = 0)
    {
        // Multiple of three, as in CalculateNormals: on a malformed index array the loop read past the end
        if (MeshTopology == PrimitiveType.TriangleList && Semantic.HasFlag(VertexSemantic.UV0)
            && Semantic.HasFlag(VertexSemantic.Position) && Indices.Length % 3 == 0)
        {
            Vector3F[] tan1 = new Vector3F[Points.Length];
            Vector3F[] tan2 = new Vector3F[Points.Length];
            for (int i = 0; i < Indices.Length; i += 3)
            {
                int index0 = Indices[i];
                int index1 = Indices[i + 1];
                int index2 = Indices[i + 2];

                Vector3F v1 = Points[index0];
                Vector3F v2 = Points[index1];
                Vector3F v3 = Points[index2];

                Vector2F uv1 = UV0[index0];
                Vector2F uv2 = UV0[index1];
                Vector2F uv3 = UV0[index2];

                Vector3F v1v0 = v2 - v1;
                Vector3F v2v0 = v3 - v1;

                float s1 = uv2.X - uv1.X;
                float t1 = uv2.Y - uv1.Y;

                float s2 = uv3.X - uv1.X;
                float t2 = uv3.Y - uv1.Y;

                Vector3F uDirection = new Vector3F((t2 * v1v0.X - t1 * v2v0.X), (t2 * v1v0.Y - t1 * v2v0.Y),
                    (t2 * v1v0.Z - t1 * v2v0.Z));
                Vector3F vDirection = new Vector3F((s1 * v2v0.X - s2 * v1v0.X), (s1 * v2v0.Y - s2 * v1v0.Y),
                    (s1 * v2v0.Z - s2 * v1v0.Z));

                tan1[index0] += uDirection;
                tan1[index1] += uDirection;
                tan1[index2] += uDirection;

                tan2[index0] += vDirection;
                tan2[index1] += vDirection;
                tan2[index2] += vDirection;
            }

            Vector4F[] tangentArray = new Vector4F[Points.Length];
            Vector3F[] bitangentArray = new Vector3F[Points.Length];
            for (int k = 0; k < Points.Length; k++)
            {
                Vector3F normal = Normals[k];

                // Gram-Schmidt orthogonalize
                var value = tan1[k] - normal * Vector3F.Dot(normal, tan1[k]);

                // Calculate handedness (for case if tangent is Vector4F)
                var handedness = Vector3F.Dot(Vector3F.Cross(normal, tan1[k]), tan2[k]) < 0.0f ? -1.0f : 1.0f;
                tangentArray[k] = new Vector4F(value, handedness);
                bitangentArray[k] = Vector3F.Cross(normal, (Vector3F)tangentArray[k]) * handedness;
            }
            Tangents = tangentArray;
            BiTangents = bitangentArray;
            Semantic |= VertexSemantic.TangentBiNormal;
        }

        return this;
    }

    public Mesh AssemblePoints(List<int> positionIndices)
    {
        if (positionIndices == null || positionIndices.Count == 0 && Semantic.HasFlag(VertexSemantic.Position))
        {
            return this;
        }

        var assembledPositions = new List<Vector3>();
        for (int i = 0; i < positionIndices.Count; i++)
        {
            assembledPositions.Add(Points[positionIndices[i]]);
        }

        Points = assembledPositions.ToArray();
        IsModified = true;
            
        return this;
    }

    public Mesh AssembleUVs(List<int> uvIndices, int channel)
    {
        if (uvIndices == null || uvIndices.Count == 0)
        {
            return this;
        }

        var assembledUvs = new List<Vector2F>();
        Vector2F[] uv = null;
        switch (channel)
        {
            case 0:
                if (Semantic.HasFlag(VertexSemantic.UV0))
                {
                    uv = UV0;
                }
                break;
            case 1:
                if (Semantic.HasFlag(VertexSemantic.UV1))
                {
                    uv = UV1;
                }
                break;
            case 2:
                if (Semantic.HasFlag(VertexSemantic.UV2))
                {
                    uv = UV2;
                }
                break;
            case 3:
                if (Semantic.HasFlag(VertexSemantic.UV3))
                {
                    uv = UV3;
                }
                break;
        }

        if (uv == null)
        {
            return this;
        }

        for (int i = 0; i < uvIndices.Count; i++)
        {
            assembledUvs.Add(uv[uvIndices[i]]);
        }

        switch (channel)
        {
            case 0:
                UV0 = assembledUvs.ToArray();
                IsModified = true;
                break;
            case 1:
                UV1 = assembledUvs.ToArray();
                IsModified = true;
                break;
            case 2:
                UV2 = assembledUvs.ToArray();
                IsModified = true;
                break;
            case 3:
                UV3 = assembledUvs.ToArray();
                IsModified = true;
                break;
        }
            
        return this;
    }

    public Mesh AssembleBones(List<int> positionIndices)
    {
        if (positionIndices == null || positionIndices.Count == 0 && Semantic.HasFlag(VertexSemantic.JointIndices) && Semantic.HasFlag(VertexSemantic.JointWeights))
        {
            return this;
        }

        var assembledBoneIndices = new List<Vector4F>();
        var assembledBoneWeights = new List<Vector4F>();
        for (int i = 0; i < positionIndices.Count; i++)
        {
            // A skin with fewer weights than the mesh has vertices is a broken file. A vertex without a weight is
            // left unbound, which beats an IndexOutOfRange from deep inside the assembly.
            var index = positionIndices[i];
            var known = index >= 0 && index < JointIndices.Length && index < JointWeights.Length;
            assembledBoneIndices.Add(known ? JointIndices[index] : Vector4F.Zero);
            assembledBoneWeights.Add(known ? JointWeights[index] : Vector4F.Zero);
        }

        JointIndices = assembledBoneIndices.ToArray();
        JointWeights = assembledBoneWeights.ToArray();
        IsModified = true;
            
        return this;
    }

    /// <summary>Reorders the normals the way the face indices name them, as the other Assemble* do. A format that
    /// indexes normals separately from positions says a vertex has DIFFERENT normals in different faces - that is what
    /// a hard edge is - and re-deriving them from the triangles afterwards averages exactly that away.</summary>
    public Mesh AssembleNormals(List<int> normalIndices)
    {
        if (normalIndices == null || normalIndices.Count == 0 || Normals.Length == 0)
        {
            return this;
        }

        var assembled = new Vector3F[normalIndices.Count];
        for (int i = 0; i < normalIndices.Count; i++)
        {
            assembled[i] = Normals[normalIndices[i]];
        }

        Normals = assembled;
        Semantic |= VertexSemantic.Normal;
        IsModified = true;

        return this;
    }

    public Mesh AssembleColors(List<int> colorIndices)
    {
        if (colorIndices == null || colorIndices.Count == 0 && Semantic.HasFlag(VertexSemantic.Color))
        {
            return this;
        }

        var assembledColors = new List<Color>();
        for (int i = 0; i < colorIndices.Count; i++)
        {
            assembledColors.Add(Colors[colorIndices[i]]);
        }

        Colors = assembledColors.ToArray();
        IsModified = true;

        return this;
    }

    public Mesh SplitContoursOnSegments()
    {
        foreach (var contour in Contours)
        {
            contour.SplitOnSegments();
        }
        return this;
    }
    
    public Mesh GenerateBasicIndices()
    {
        if (!Semantic.HasFlag(VertexSemantic.Position))
        {
            return this;
        }

        int indicesCount = Points.Length;
        if (MeshTopology == PrimitiveType.LineStrip || MeshTopology == PrimitiveType.TriangleStrip)
        {
            indicesCount++;
        }

        Indices = new int[indicesCount];
        for (int i = 0; i < Points.Length; i++)
        {
            Indices[i] = i;
        }

        return this;
    }

    /// <summary>Turns every triangle over. A strip has its first point of each run said twice, which turns over every
    /// triangle after it and adds one with no area.</summary>
    public Mesh ReverseWinding()
    {
        Vector3F temp = new Vector3F();
        if (MeshTopology == PrimitiveType.TriangleList && Indices.Length % 3 == 0)
        {
            for (int i = 0; i < Indices.Length; i += 3)
            {
                temp.Y = Indices[i + 1];
                temp.Z = Indices[i + 2];
                Indices[i + 1] = (int)temp.Z;
                Indices[i + 2] = (int)temp.Y;
            }
        }
        else if (MeshTopology == PrimitiveType.TriangleStrip && Indices.Length > 0)
        {
            var reversed = new List<int>(Indices);
            TurnStripOver(reversed, 0);
            Indices = reversed.ToArray();
        }

        return this;
    }

    // Says the first point of each run of the strip from `from` on twice: every triangle after it turns over.
    private static void TurnStripOver(List<int> indices, int from)
    {
        var runStarts = true;
        for (var i = from; i < indices.Count; i++)
        {
            var index = indices[i];
            if (runStarts && index != StripIndicesSeparator)
            {
                indices.Insert(i, index);
                i++;
            }

            runStarts = index == StripIndicesSeparator;
        }
    }

    // The same axis change the points get. A normal is a direction, so it takes the rotation and nothing else - but it
    // must take it, or a mesh that brought its own normals would be lit by the source format's axes.
    private void TurnNormals(System.Func<Vector3F, Vector3F> turn)
    {
        for (int i = 0; i < Normals.Length; i++)
        {
            Normals[i] = turn(Normals[i]);
        }
    }

    public Mesh ChangeCoordinateSystem(UpAxis destinationUpAxis)
    {
        Vector3 position;
        if (UpAxis == destinationUpAxis)
        {
            return this;
        }

        switch (UpAxis)
        {
            case UpAxis.Z_UP:
                switch (destinationUpAxis)
                {
                    case UpAxis.Y_UP_RH:
                        for (int i = 0; i < Points.Length; i++)
                        {
                            position = Points[i];
                            //Меняем позиции вершин
                            var z = -position.Y;
                            position.Y = position.Z;
                            position.Z = z;

                            Points[i] = position;

                            ConvertUVs(i);
                        }
                        TurnNormals(n => new Vector3F(n.X, n.Z, -n.Y));
                        break;

                    case UpAxis.Y_UP_LH:
                        for (int i = 0; i < Points.Length; i++)
                        {
                            position = Points[i];
                            //Меняем позиции вершин
                            (position.Y, position.Z) = (position.Z, position.Y);
                            Points[i] = position;

                            ConvertUVs(i);
                        }
                        TurnNormals(n => new Vector3F(n.X, n.Z, n.Y));
                        break;
                    case UpAxis.Y_DOWN_RH:
                        for (int i = 0; i < Points.Length; i++)
                        {
                            position = Points[i];
                            //Меняем позиции вершин

                            (position.Y, position.Z) = (position.Z, position.Y);
                            Points[i] = position;

                            ConvertUVs(i);
                        }
                        TurnNormals(n => new Vector3F(n.X, n.Z, n.Y));
                        break;
                }
                break;

            case UpAxis.Y_UP_RH:
                switch (destinationUpAxis)
                {
                    case UpAxis.Y_UP_LH:
                        for (int i = 0; i < Points.Length; i++)
                        {
                            position = Points[i];
                            //Меняем позиции вершин
                            position.Z = -position.Z;
                            Points[i] = position;

                            ConvertUVs(i);
                        }
                        TurnNormals(n => new Vector3F(n.X, n.Y, -n.Z));
                        break;
                    case UpAxis.Y_DOWN_RH:
                        for (int i = 0; i < Points.Length; i++)
                        {
                            position = Points[i];
                            //Меняем позиции вершин
                            position.Y = -position.Y;
                            Points[i] = position;

                            ConvertUVs(i);
                        }
                        TurnNormals(n => new Vector3F(n.X, -n.Y, n.Z));
                        break;
                }
                break;
        }

        // Re-wind the faces only when the change MIRRORS the mesh, which negating or swapping an axis does and a
        // plain rotation does not. Every branch used to do it unconditionally, so on Z_UP -> Y_UP_RH - a proper
        // rotation - every triangle came out facing backwards. Nothing caught it while normals were re-derived
        // from that same winding: both were wrong together, and nothing culls back faces here.
        if (AxisConversion.MirrorsHandedness(AxisConversion.Between(UpAxis, destinationUpAxis)))
        {
            ReverseWinding();
        }

        IsModified = true;
        return this;
    }

    private Mesh ConvertUVs(int index)
    {
        Vector2F uv;
        if (Semantic.HasFlag(VertexSemantic.UV0))
        {
            uv = UV0[index];
            uv.Y = 1.0f - uv.Y;
            UV0[index] = uv;
        }

        if (Semantic.HasFlag(VertexSemantic.UV1))
        {
            uv = UV1[index];
            uv.Y = 1.0f - uv.Y;
            UV1[index] = uv;
        }

        if (Semantic.HasFlag(VertexSemantic.UV2))
        {
            uv = UV2[index];
            uv.Y = 1.0f - uv.Y;
            UV2[index] = uv;
        }

        if (Semantic.HasFlag(VertexSemantic.UV3))
        {
            uv = UV3[index];
            uv.Y = 1.0f - uv.Y;
            UV3[index] = uv;
        }

        return this;
    }

    public void UpdateContoursPoints()
    {
        foreach (var contour in Contours)
        {
            contour.UpdatePoints();
        }
    }

    public void ClearContoursSegments()
    {
        foreach (var contour in Contours)
        {
            contour.Segments.Clear();
        }
    }

    public void RemoveSegmentsByRule(bool removeInner)
    {
        foreach (var contour in Contours)
        {
            contour.RemoveSegmentsByRule(removeInner);
        }
    }
}