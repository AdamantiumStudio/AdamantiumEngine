using Adamantium.Core.Events;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;

namespace Adamantium.EngineTests;

/// <summary>An output with no surface: the test says where the focus and the pointer are and feeds it input.</summary>
public class TestOutput : UniverseOutput
{
    private readonly GamepadHub gamepads;

    public TestOutput(IGamepadBackend backend = null) : base(new EventAggregator())
    {
        gamepads = new GamepadHub(backend ?? new NoGamepadBackend(), EventAggregator);
        Input = new InputWormhole(this, gamepads);
        Description = new UniverseOutputDescription(PresenterType.RenderTarget) { Width = 800, Height = 600 };
        OutputContext = new OutputContext(this);
    }

    public bool KeyboardFocused { get; set; } = true;

    public bool PointerOver { get; set; } = true;

    public Vector2F Pointer { get; set; }

    public override UniverseOutputDescription Description { get; protected set; }

    public override OutputCursor Cursor { get; set; }

    public override object NativeWindow => null;

    public override OutputState State => OutputState.Shown;

    public override bool IsKeyboardFocused => KeyboardFocused;

    public override bool IsPointerOver => PointerOver;

    public override Vector2F PointerScreenPosition => Pointer;

    public void Key(Keys key, InputType type)
    {
        OnKeyInput(new KeyboardInput { Key = key, InputType = type });
    }

    public void Type(string text)
    {
        OnTextInput(text);
    }

    public void Move(float x, float y)
    {
        OnMouseInput(new MouseInput { InputType = InputType.RawDelta, Delta = new Vector2F(x, y) });
    }

    public void Wheel(int delta)
    {
        OnMouseInput(new MouseInput { InputType = InputType.Wheel, WheelDelta = delta });
    }

    public void Gesture(Vector2F translation, float scale, float rotation)
    {
        OnGestureInput(new GestureInput { Translation = translation, Scale = scale, Rotation = rotation });
    }

    public void Button(MouseButton button, InputType type, int clickCount = 1)
    {
        OnMouseInput(new MouseInput { Button = button, InputType = type, ClickCount = clickCount });
    }

    public void Frame()
    {
        gamepads.Update();
        Input.Update(default);
    }

    public override Vector2F PointToSurface(Vector2F absolute)
    {
        return absolute;
    }

    public override void HoldPointer(bool hold, Vector2F origin)
    {
    }

    protected override bool CanHandle(OutputContext context)
    {
        return false;
    }

    protected internal override void SwitchContext(OutputContext context)
    {
    }

    protected override void Initialize(OutputContext context)
    {
    }

    protected override void Initialize(
        OutputContext context,
        SurfaceFormat pixelFormat,
        DepthFormat depthFormat = DepthFormat.Depth32Stencil8X24,
        MSAALevel msaaLevel = MSAALevel.X4)
    {
    }
}
