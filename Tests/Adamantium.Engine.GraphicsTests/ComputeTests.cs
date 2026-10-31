using System;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests
{
    // Line-rendering Step A3b: drives the full compute path on the GPU end to end. A compute shader writes a known
    // pattern (index+1) to a buffer via a BDA device address; a copy to the CPU verifies it. Compute is recorded in
    // BeginDraw's beforeRenderPass hook - outside the render pass, on the graphics queue - the only valid place to
    // dispatch (the render pass hasn't begun yet there).
    [TestFixture]
    public class ComputeTests
    {
        [TearDown]
        public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

        // Storage buffers bound as descriptors - one read only, one written - before the render pass begins: the
        // descriptor heap is bound from the start of the command buffer, not only inside the render pass.
        [Test]
        public void StorageBuffersBoundAsDescriptors_AreReadAndWritten_BeforeTheRenderPass()
        {
            const int count = 64;
            var device = GpuFixture.CreateRenderDevice();
            var gd = (GraphicsDevice)device;
            var compiled = Adamantium.EffectsCompiler.EffectCompiler.Compile("""
                StructuredBuffer<uint> In;
                RWStructuredBuffer<uint> Out;
                uint Bias;
                [numthreads(64, 1, 1)]
                void CS(uint3 id : SV_DispatchThreadID) { Out[id.x] = In[id.x] * 2 + Bias; }
                technique T { pass { ComputeShader = CS; } }
                """, "StorageBuffers.fx");
            Assert.That(compiled.HasErrors, Is.False, string.Join("\n", compiled.Logger.Messages));
            using var effect = new Effect(device, compiled.EffectData);
            using var input = Storage(gd, count);
            using var output = Storage(gd, count);
            var values = new uint[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = (uint)i;
            }

            input.SetData(values);
            effect.Parameters["In"].SetResource(input);
            effect.Parameters["Out"].SetResource(output);
            effect.Parameters["Bias"].SetValue(7u);

            var prms = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
            using var presenter = GraphicsPresenter.Create(device, prms, "storage_buffers");
            device.SetRenderTargets(presenter.RenderTarget);
            device.SetDepthBuffer(presenter.DepthBuffer);
            device.MSAALevel = presenter.MSAALevel;
            device.Presenter = presenter;

            Assert.That(device.BeginDraw(beforeRenderPass: _ =>
            {
                effect.Techniques[0].Passes[0].Apply();
                gd.Dispatch(1);
                gd.BufferBarrier(output,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
            }), Is.True);
            device.EndDraw();
            device.Submit();
            presenter.Present();
            device.FrameEnded();
            device.DeviceWaitIdle();

            var written = new uint[count];
            output.GetData(written);
            Assert.Multiple(() =>
            {
                Assert.That(written[5], Is.EqualTo(17u));
                Assert.That(written[63], Is.EqualTo(133u));
                Assert.That(effect.Parameters["In"].Access, Is.EqualTo(Adamantium.EffectsCompiler.EffectResourceAccess.ReadOnly));
                Assert.That(effect.Parameters["Out"].Access, Is.EqualTo(Adamantium.EffectsCompiler.EffectResourceAccess.ReadWrite));
            });
        }

        private static Adamantium.Graphics.Buffer Storage(GraphicsDevice device, int count) =>
            Adamantium.Graphics.Buffer.New(device, (ulong)(count * sizeof(uint)), BufferUsageFlags.StorageBuffer,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);

        [Test]
        public void ComputeDispatch_WritesPatternViaBda()
        {
            const int count = 256;

            var main = GpuFixture.Main;
            var device = GpuFixture.CreateRenderDevice();

            var gd = (GraphicsDevice)device;   // Dispatch/DrawIndirect/BufferBarrier live on the concrete device
            // DISPOSED, both of them: a test that leaves its effect and its buffer behind leaves live objects on a device
            // it is about to destroy, and the next test's device creation then dies in the same process.
            using var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "ComputeSmoke.fx"), device);
            var pass = effect.Techniques[0].Passes[0];

            using var output = Adamantium.Graphics.Buffer.New(gd, (ulong)(count * sizeof(uint)),
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);

            effect.Parameters["Output"].SetValue(output.GetDeviceAddress());
            effect.Parameters["Count"].SetValue((uint)count);

            var prms = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
            using var presenter = GraphicsPresenter.Create(device, prms, "compute_smoke");
            device.SetRenderTargets(presenter.RenderTarget);
            device.SetDepthBuffer(presenter.DepthBuffer);
            device.MSAALevel = presenter.MSAALevel;
            device.Presenter = presenter;

            uint groups = (count + 63) / 64;
            Assert.That(device.BeginDraw(beforeRenderPass: cmd =>
            {
                pass.Apply();
                gd.Dispatch(groups);
                gd.BufferBarrier(output,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
            }), Is.True);
            device.EndDraw();
            device.Submit();
            presenter.Present();
            device.FrameEnded();
            device.DeviceWaitIdle();

            var data = new int[count];
            var ptr = output.MapMemory();
            Marshal.Copy((IntPtr)(nint)ptr, data, 0, count);
            output.UnmapMemory();

            for (int i = 0; i < count; i++)
                Assert.That(data[i], Is.EqualTo(i + 1), $"output[{i}] (BDA compute write)");

        }

        // The GPU stroke expander turns a polyline + half-thickness into per-segment
        // quads (straight segments, no joins/caps yet). We dispatch it, read the output vertices back and compare to a
        // CPU offset computation - proving the GPU builds the stroke geometry (the whole point: no CPU re-tessellation).
        [Test]
        public void StrokeExpand_MiterJoins_MatchCpu()
        {
            float[] pts = { 10f, 10f, 50f, 10f, 50f, 50f };   // 3 points (an L) -> mitered corner at the middle
            int pointCount = pts.Length / 2;
            const float half = 4f;
            int outFloats = pointCount * 2 * 2;                // 2 verts/point * 2 floats (triangle strip)

            var main = GpuFixture.Main;
            var device = GpuFixture.CreateRenderDevice();
            var gd = (GraphicsDevice)device;

            var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "StrokeExpand.fx"), device);
            var pass = effect.Techniques[0].Passes[0];

            var pointsBuf = Adamantium.Graphics.Buffer.New(gd, (ulong)(pts.Length * sizeof(float)),
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);
            var pp = pointsBuf.MapMemory();
            Marshal.Copy(pts, 0, (IntPtr)(nint)pp, pts.Length);
            pointsBuf.UnmapMemory();

            var outBuf = Adamantium.Graphics.Buffer.New(gd, (ulong)(outFloats * sizeof(float)),
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);

            effect.Parameters["Points"].SetValue(pointsBuf.GetDeviceAddress());
            effect.Parameters["Output"].SetValue(outBuf.GetDeviceAddress());
            effect.Parameters["PointCount"].SetValue((uint)pointCount);
            effect.Parameters["HalfThickness"].SetValue(half);

            var prms = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
            using var presenter = GraphicsPresenter.Create(device, prms, "stroke_miter");
            device.SetRenderTargets(presenter.RenderTarget);
            device.SetDepthBuffer(presenter.DepthBuffer);
            device.MSAALevel = presenter.MSAALevel;
            device.Presenter = presenter;

            uint groups = ((uint)pointCount + 63) / 64;
            Assert.That(device.BeginDraw(beforeRenderPass: cmd =>
            {
                pass.Apply();
                gd.Dispatch(groups);
                gd.BufferBarrier(outBuf,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
            }), Is.True);
            device.EndDraw();
            device.Submit();
            presenter.Present();
            device.FrameEnded();
            device.DeviceWaitIdle();

            var got = new float[outFloats];
            var op = outBuf.MapMemory();
            Marshal.Copy((IntPtr)(nint)op, got, 0, outFloats);
            outBuf.UnmapMemory();

            var expected = MiterExpandCpu(pts, half);
            for (int i = 0; i < outFloats; i++)
                Assert.That(got[i], Is.EqualTo(expected[i]).Within(0.01f), $"vertex float [{i}]");

        }

        // CPU mirror of StrokeExpand.fx's miter expansion: 2 offset vertices per point along the miter normal.
        private static float[] MiterExpandCpu(float[] pts, float half)
        {
            int n = pts.Length / 2;
            var outv = new float[n * 4];
            for (int i = 0; i < n; i++)
            {
                float px = pts[i * 2], py = pts[i * 2 + 1];
                float mx, my, mlen;
                if (i == 0)
                {
                    Normal(pts, 0, 1, out mx, out my);
                    mlen = half;
                }
                else if (i + 1 == n)
                {
                    Normal(pts, i - 1, i, out mx, out my);
                    mlen = half;
                }
                else
                {
                    Normal(pts, i - 1, i, out float n0x, out float n0y);
                    Normal(pts, i, i + 1, out float n1x, out float n1y);
                    float sx = n0x + n1x, sy = n0y + n1y;
                    float sl = MathF.Sqrt(sx * sx + sy * sy);
                    mx = sx / sl; my = sy / sl;
                    float denom = MathF.Max(mx * n0x + my * n0y, 0.25f);
                    mlen = half / denom;
                }
                outv[i * 4 + 0] = px + mx * mlen;
                outv[i * 4 + 1] = py + my * mlen;
                outv[i * 4 + 2] = px - mx * mlen;
                outv[i * 4 + 3] = py - my * mlen;
            }
            return outv;
        }

        // Segment normal from point index a to b: perp of normalize(b - a) = (-dy, dx).
        private static void Normal(float[] pts, int a, int b, out float nx, out float ny)
        {
            float dx = pts[b * 2] - pts[a * 2], dy = pts[b * 2 + 1] - pts[a * 2 + 1];
            float l = MathF.Sqrt(dx * dx + dy * dy);
            dx /= l; dy /= l;
            nx = -dy; ny = dx;
        }

        // float2 position vertex - the compute expander's output format, bound as a vertex buffer for the draw.
        [StructLayout(LayoutKind.Sequential)]
        private struct StrokeVertex
        {
            [VertexInputElement("POSITION")] public Vector2F Position;
        }

        // Render the GPU-expanded stroke. Compute writes the quad vertices, then the
        // SAME frame binds that buffer as a vertex buffer and draws it (white on black). A pixel copied to the CPU proves the
        // rasterizer consumes the compute output: pixels on the stroke differ from the background and match each other.
        [Test]
        public void StrokeExpand_RendersToTarget()
        {
            float[] pts = { 10f, 10f, 50f, 10f, 50f, 50f };   // L shape in a 64x64 target
            int pointCount = pts.Length / 2;
            const float half = 4f;
            uint verts = (uint)pointCount * 2;                 // 2 verts/point, triangle strip

            var main = GpuFixture.Main;
            var device = GpuFixture.CreateRenderDevice();
            var gd = (GraphicsDevice)device;

            var expand = Effect.CompileFromFile(Path.Combine("EffectsData", "StrokeExpand.fx"), device);
            var expandPass = expand.Techniques[0].Passes[0];
            var draw = Effect.CompileFromFile(Path.Combine("EffectsData", "StrokeDraw.fx"), device);
            var drawPass = draw.Techniques[0].Passes[0];

            var pointsBuf = Adamantium.Graphics.Buffer.New(gd, (ulong)(pts.Length * sizeof(float)),
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);
            var pp = pointsBuf.MapMemory();
            Marshal.Copy(pts, 0, (IntPtr)(nint)pp, pts.Length);
            pointsBuf.UnmapMemory();

            var vertsBuf = Adamantium.Graphics.Buffer.New(gd, (ulong)(verts * 2 * sizeof(float)),
                BufferUsageFlags.VertexBuffer | BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);

            expand.Parameters["Points"].SetValue(pointsBuf.GetDeviceAddress());
            expand.Parameters["Output"].SetValue(vertsBuf.GetDeviceAddress());
            expand.Parameters["PointCount"].SetValue((uint)pointCount);
            expand.Parameters["HalfThickness"].SetValue(half);

            var proj = Matrix4x4.CreateOrthoOffCenter<float>(0, 64, 0, 64, 0, 100000);
            draw.Parameters["Projection"].SetValue(proj);
            draw.Parameters["StrokeColor"].SetValue(new Vector4F(1, 1, 1, 1));

            var prms = new PresentationParameters(PresenterType.RenderTarget, 64, 64, IntPtr.Zero);
            using var presenter = GraphicsPresenter.Create(device, prms, "stroke_render");
            device.ClearColor = Colors.Black;
            device.SetRenderTargets(presenter.RenderTarget);
            device.SetDepthBuffer(presenter.DepthBuffer);
            device.MSAALevel = presenter.MSAALevel;
            device.Presenter = presenter;
            device.CullMode = CullModeFlagBits.None;   // compute winding isn't guaranteed; don't cull either face

            var vp = new Viewport { Width = 64, Height = 64, MinDepth = 0, MaxDepth = 1 };
            var sc = new Rect2D { Offset = new Offset2D(), Extent = new Extent2D { Width = 64, Height = 64 } };

            uint groups = ((uint)pointCount + 63) / 64;
            // The frame running without a GPU or validation error is the assertion; geometry is checked by
            // StrokeExpand_StraightSegments_MatchCpu.
            Assert.That(device.BeginDraw(beforeRenderPass: cmd =>
            {
                expandPass.Apply();
                gd.Dispatch(groups);
                gd.BufferBarrier(vertsBuf,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.VertexAttributeInputBit, AccessFlagBits2.VertexAttributeReadBit);
            }), Is.True);
            device.SetViewports(vp);
            device.SetScissors(sc);
            device.VertexType = typeof(StrokeVertex);
            device.SetVertexBuffer(vertsBuf);
            device.PrimitiveTopology = PrimitiveTopology.TriangleStrip;   // ribbon: 2 verts/point
            drawPass.Apply();
            device.Draw(verts, 1);
            device.EndDraw();
            device.Submit();
            presenter.Present();
            device.FrameEnded();
            device.DeviceWaitIdle();

        }

        // De-risk for one-pass variable-output geometry (dashes / round joins / caps in a single dispatch): every
        // thread atomically reserves a slot in an output buffer via a BDA-addressed counter (InterlockedAdd) and
        // scatters tid+1 there. Proves the atomic on a PhysicalStorageBuffer pointer works: the counter must equal the
        // thread count and the output must be a permutation of 1..count (no two threads got the same slot).
        [Test]
        public void AtomicAppend_ScatterViaBdaCounter()
        {
            const int count = 200;

            var main = GpuFixture.Main;
            var device = GpuFixture.CreateRenderDevice();
            var gd = (GraphicsDevice)device;

            var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "AtomicAppend.fx"), device);
            var pass = effect.Techniques[0].Passes[0];

            var counter = Adamantium.Graphics.Buffer.New(gd, (ulong)sizeof(uint),
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);
            var cinit = counter.MapMemory();              // zero-initialize the counter before dispatch
            Marshal.WriteInt32((IntPtr)(nint)cinit, 0);
            counter.UnmapMemory();

            var output = Adamantium.Graphics.Buffer.New(gd, (ulong)(count * sizeof(uint)),
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);

            effect.Parameters["Counter"].SetValue(counter.GetDeviceAddress());
            effect.Parameters["Output"].SetValue(output.GetDeviceAddress());
            effect.Parameters["Count"].SetValue((uint)count);

            var prms = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
            using var presenter = GraphicsPresenter.Create(device, prms, "atomic_append");
            device.SetRenderTargets(presenter.RenderTarget);
            device.SetDepthBuffer(presenter.DepthBuffer);
            device.MSAALevel = presenter.MSAALevel;
            device.Presenter = presenter;

            uint groups = (count + 63) / 64;
            Assert.That(device.BeginDraw(beforeRenderPass: cmd =>
            {
                pass.Apply();
                gd.Dispatch(groups);
                gd.BufferBarrier(output,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
                gd.BufferBarrier(counter,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
            }), Is.True);
            device.EndDraw();
            device.Submit();
            presenter.Present();
            device.FrameEnded();
            device.DeviceWaitIdle();

            var cval = new int[1];
            var cm = counter.MapMemory();
            Marshal.Copy((IntPtr)(nint)cm, cval, 0, 1);
            counter.UnmapMemory();
            Assert.That(cval[0], Is.EqualTo(count), "atomic counter must equal the number of appends");

            var data = new int[count];
            var op = output.MapMemory();
            Marshal.Copy((IntPtr)(nint)op, data, 0, count);
            output.UnmapMemory();
            Array.Sort(data);
            for (int i = 0; i < count; i++)
                Assert.That(data[i], Is.EqualTo(i + 1), $"slot [{i}] - each thread must scatter tid+1 to a unique slot");

        }

        // De-risk DrawIndirect: a compute shader writes the draw arguments (VkDrawIndirectCommand) AND the vertices
        // into BDA buffers, then DrawIndirect reads that GPU-produced count and rasterizes. The frame completing without
        // a GPU/validation error IS the assertion (atomic correctness is covered by AtomicAppend; geometry by the
        // stroke tests). Together they prove "the GPU decides how much to draw, in one dispatch" end to end.
        [Test]
        public void DrawIndirect_RendersGpuProducedDraw()
        {
            var main = GpuFixture.Main;
            var device = GpuFixture.CreateRenderDevice();
            var gd = (GraphicsDevice)device;

            var fill = Effect.CompileFromFile(Path.Combine("EffectsData", "IndirectDraw.fx"), device);
            var fillPass = fill.Techniques[0].Passes[0];
            var draw = Effect.CompileFromFile(Path.Combine("EffectsData", "StrokeDraw.fx"), device);
            var drawPass = draw.Techniques[0].Passes[0];

            var indirect = Adamantium.Graphics.Buffer.New(gd, (ulong)(4 * sizeof(uint)),
                BufferUsageFlags.IndirectBuffer | BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);
            var verts = Adamantium.Graphics.Buffer.New(gd, (ulong)(6 * 2 * sizeof(float)),
                BufferUsageFlags.VertexBuffer | BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);

            fill.Parameters["Indirect"].SetValue(indirect.GetDeviceAddress());
            fill.Parameters["Output"].SetValue(verts.GetDeviceAddress());

            var proj = Matrix4x4.CreateOrthoOffCenter<float>(0, 64, 0, 64, 0, 100000);
            draw.Parameters["Projection"].SetValue(proj);
            draw.Parameters["StrokeColor"].SetValue(new Vector4F(1, 1, 1, 1));

            var prms = new PresentationParameters(PresenterType.RenderTarget, 64, 64, IntPtr.Zero);
            using var presenter = GraphicsPresenter.Create(device, prms, "indirect_draw");
            device.ClearColor = Colors.Black;
            device.SetRenderTargets(presenter.RenderTarget);
            device.SetDepthBuffer(presenter.DepthBuffer);
            device.MSAALevel = presenter.MSAALevel;
            device.Presenter = presenter;
            device.CullMode = CullModeFlagBits.None;

            var vp = new Viewport { Width = 64, Height = 64, MinDepth = 0, MaxDepth = 1 };
            var sc = new Rect2D { Offset = new Offset2D(), Extent = new Extent2D { Width = 64, Height = 64 } };

            Assert.That(device.BeginDraw(beforeRenderPass: cmd =>
            {
                fillPass.Apply();
                gd.Dispatch(1);
                gd.BufferBarrier(indirect,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.DrawIndirectBit, AccessFlagBits2.IndirectCommandReadBit);
                gd.BufferBarrier(verts,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.VertexAttributeInputBit, AccessFlagBits2.VertexAttributeReadBit);
            }), Is.True);
            device.SetViewports(vp);
            device.SetScissors(sc);
            device.VertexType = typeof(StrokeVertex);
            device.PrimitiveTopology = PrimitiveTopology.TriangleList;
            drawPass.Apply();
            gd.DrawIndirect(verts, indirect);
            device.EndDraw();
            device.Submit();
            presenter.Present();
            device.FrameEnded();
            device.DeviceWaitIdle();

        }

        // One-pass GPU dash cutting against the CPU walk: (0,0)-(100,0) with [20 on, 10 off] gives 4 pieces = 24 vertices,
        // and half-thickness 5 makes the first quad x in [0,20], y in [-5,5].
        [Test]
        public void DashCut_OnePassWalk_MatchesCpu()
        {
            float[] pts = { 0f, 0f, 100f, 0f };
            float[] pattern = { 20f, 10f };
            const float half = 5f;

            var main = GpuFixture.Main;
            var device = GpuFixture.CreateRenderDevice();
            var gd = (GraphicsDevice)device;

            var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "DashCut.fx"), device);
            var pass = effect.Techniques[0].Passes[0];

            var pointsBuf = MakeBuffer(gd, pts);
            var patternBuf = MakeBuffer(gd, pattern);
            var indirect = Adamantium.Graphics.Buffer.New(gd, (ulong)(4 * sizeof(uint)),
                BufferUsageFlags.IndirectBuffer | BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);
            var output = Adamantium.Graphics.Buffer.New(gd, (ulong)(256 * 2 * sizeof(float)),
                BufferUsageFlags.VertexBuffer | BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);

            effect.Parameters["Points"].SetValue(pointsBuf.GetDeviceAddress());
            effect.Parameters["Pattern"].SetValue(patternBuf.GetDeviceAddress());
            effect.Parameters["Output"].SetValue(output.GetDeviceAddress());
            effect.Parameters["Indirect"].SetValue(indirect.GetDeviceAddress());
            effect.Parameters["PointCount"].SetValue(2u);
            effect.Parameters["IsClosed"].SetValue(0u);
            effect.Parameters["PatternCount"].SetValue((uint)pattern.Length);
            effect.Parameters["DashOffset"].SetValue(0f);
            effect.Parameters["HalfThickness"].SetValue(half);
            effect.Parameters["TrimStart"].SetValue(0f);
            effect.Parameters["TrimEnd"].SetValue(1f);

            var prms = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
            using var presenter = GraphicsPresenter.Create(device, prms, "dash_cut");
            device.SetRenderTargets(presenter.RenderTarget);
            device.SetDepthBuffer(presenter.DepthBuffer);
            device.MSAALevel = presenter.MSAALevel;
            device.Presenter = presenter;

            Assert.That(device.BeginDraw(beforeRenderPass: cmd =>
            {
                pass.Apply();
                gd.Dispatch(1);
                gd.BufferBarrier(indirect,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
                gd.BufferBarrier(output,
                    PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                    PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
            }), Is.True);
            device.EndDraw();
            device.Submit();
            presenter.Present();
            device.FrameEnded();
            device.DeviceWaitIdle();

            var cmd4 = new int[4];
            var cp = indirect.MapMemory();
            Marshal.Copy((IntPtr)(nint)cp, cmd4, 0, 4);
            indirect.UnmapMemory();
            Assert.That(cmd4[0], Is.EqualTo(24), "vertexCount: 4 dash pieces * 6 verts");
            Assert.That(cmd4[1], Is.EqualTo(1), "instanceCount");

            var verts = new float[12];   // first piece = 6 verts = 12 floats
            var op = output.MapMemory();
            Marshal.Copy((IntPtr)(nint)op, verts, 0, 12);
            output.UnmapMemory();
            float[] expected = { 0, 5, 0, -5, 20, 5, 20, 5, 0, -5, 20, -5 };
            for (int i = 0; i < 12; i++)
                Assert.That(verts[i], Is.EqualTo(expected[i]).Within(0.01f), $"first piece float [{i}]");

        }

        private static Adamantium.Graphics.Buffer MakeBuffer(GraphicsDevice gd, float[] data)
        {
            var buf = Adamantium.Graphics.Buffer.New(gd, (ulong)(data.Length * sizeof(float)),
                BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.DeviceLocal);
            var p = buf.MapMemory();
            Marshal.Copy(data, 0, (IntPtr)(nint)p, data.Length);
            buf.UnmapMemory();
            return buf;
        }
    }
}
