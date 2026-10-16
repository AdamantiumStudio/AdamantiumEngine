using Adamantium.Core;
using Adamantium.Mathematics;
using Adamantium.MVVM;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;

namespace Adamantium.ECS.Components
{
    [ViewModel]
    public partial class MeshMetadata : PropertyChangedBase
    {
        [Bindable] private ShapeType _shapeType;
        [Bindable] private int _tessellationFactor;
        [Bindable] private double _width;
        [Bindable] private double _height;
        [Bindable] private double _depth;
        [Bindable] private double _thickness;
        [Bindable] private double _diameter;
        [Bindable] private float _topDiameter;
        [Bindable] private float _bottomDiameter;
        [Bindable] private GeometryType _geometryType;
        [Bindable] private float _radiusX;
        [Bindable] private float _radiusY;
        [Bindable] private float _startAngle;
        [Bindable] private float _stopAngle;
        [Bindable] private bool _isClockwise;
        [Bindable] private EllipseType _ellipseType;
        [Bindable] private Vector3 _lineStart;
        [Bindable] private Vector3 _lineEnd;

        public MeshMetadata()
        { }

        public MeshMetadata(
            MeshMetadata metadata)
        {
            _shapeType = metadata._shapeType;
            _tessellationFactor = metadata.TessellationFactor;
            _width = metadata.Width;
            _height = metadata.Height;
            _depth = metadata.Depth;
            _thickness = metadata.Thickness;
            _diameter = metadata.Diameter;
            _topDiameter = metadata.TopDiameter;
            _bottomDiameter = metadata.BottomDiameter;
            _lineStart = metadata.LineStart;
            _lineEnd = metadata.LineEnd;
        }

        public static MeshMetadata Default()
        {
            var metadata = new MeshMetadata();
            metadata.ShapeType = ShapeType.Unknown;
            metadata.Width = 1;
            metadata.Height = 1;
            metadata.Depth = 1;
            metadata.Diameter = 1;
            metadata.TessellationFactor = GetDefaultTessFactor(metadata.ShapeType);
            metadata.Thickness = 0.1f;
            metadata.TopDiameter = 0;
            metadata.BottomDiameter = 1;
            metadata.LineStart = new Vector3(-0.5f, 0, 0);
            metadata.LineStart = new Vector3(0.5f, 0, 0);
            return metadata;
        }

        private static int GetDefaultTessFactor(ShapeType shapeType)
        {
            switch (shapeType)
            {
                case ShapeType.UVSphere:
                case ShapeType.CubeSphere:
                case ShapeType.Cone:
                case ShapeType.Tube:
                case ShapeType.Cylinder:
                case ShapeType.Torus:
                case ShapeType.Arc:
                case ShapeType.Ellipse:
                case ShapeType.Capsule:
                case ShapeType.Polygon:
                    return 40;
                case ShapeType.GeoSphere:
                case ShapeType.Teapot:
                    return 8;
                case ShapeType.Rectangle:
                    return 20;
                default:
                    return 1;
            }
        }

        public CornerRadius Corners
        {
            get;
            set;
        }
    }
}