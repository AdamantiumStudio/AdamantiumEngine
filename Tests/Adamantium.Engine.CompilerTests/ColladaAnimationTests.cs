using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests
{
    /// <summary>
    /// Parsing library_animations over small files. What this mainly covers are the file shapes our own assets do
    /// not have: per-component channels, nested animation elements, one joint's channels spread across elements and
    /// sampled at different times. None of it shows on swordman, because swordman is baked into matrices.
    /// </summary>
    [TestFixture]
    public class ColladaAnimationTests
    {
        private string tempDirectory;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "adamantium-anim-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
        }

        [Test]
        public void Animation_ReadsPerComponentChannels()
        {
            // This is how Blender and Maya write unbaked animation. Only float4x4 used to be read, so such a file
            // imported "successfully": time stamps present, no pose, the model standing still.
            var scene = Import(Document(
                Bone("Bone", translate: "1 2 3", rotationZ: 0),
                Channel("Bone/location.X", "0 1", "1 5", stride: 1) +
                Channel("Bone/rotationZ.ANGLE", "0 1", "0 90", stride: 1)));

            var frames = Frames(scene, "Bone");
            Assert.That(frames.Count, Is.EqualTo(2));

            // The file is Y_UP and the engine Y_DOWN, so Y arrives with its sign flipped, exactly as the mesh
            // vertices do (AxisConversionTests owns the basis change itself). Coordinates no channel touches must
            // come from the rest pose rather than fall to zero.
            Assert.That(frames[0].Position, Is.EqualTo(new Vector3F(1, -2, 3)).Using(Vector3Comparer));
            Assert.That(frames[1].Position, Is.EqualTo(new Vector3F(5, -2, 3)).Using(Vector3Comparer));

            // A 90-degree turn about Z, reflected along with the Y axis, sends X to -Y
            var turned = Vector3F.Transform(Vector3F.UnitX, frames[1].Rotation);
            Assert.That(turned, Is.EqualTo(-Vector3F.UnitY).Using(Vector3Comparer), "a 90-degree turn about Z was expected");
        }

        [Test]
        public void TheOneClip_IsKeptOnce_UnderTheModelsName()
        {
            var scene = Import(Document(
                Bone("Bone", translate: "0 0 0"),
                Channel("Bone/location.X", "0 1", "0 4", stride: 1)));

            Assert.That(scene.Clips.Keys, Is.EqualTo(new[] { "model" }));
        }

        [Test]
        public void Animation_MergesChannelsOfOneJointFromSeparateAnimations()
        {
            // Blender puts every channel in its OWN <animation>. The animation dictionary used to take the first one
            // and silently drop the rest, because the key was already taken.
            var scene = Import(Document(
                Bone("Bone", translate: "0 0 0"),
                Animation("a1", Channel("Bone/location.X", "0 1", "0 4", stride: 1)) +
                Animation("a2", Channel("Bone/location.Y", "0 1", "0 8", stride: 1))));

            var frames = Frames(scene, "Bone");
            Assert.That(frames.Count, Is.EqualTo(2));
            //Y with its sign flipped: the file is Y_UP, the engine Y_DOWN
            Assert.That(frames[1].Position, Is.EqualTo(new Vector3F(4, -8, 0)).Using(Vector3Comparer),
                "both channels must land on one and the same joint");
        }

        [Test]
        public void Animation_ResamplesChannelsWithDifferentTimelines()
        {
            // Translation and rotation are often sampled at different times. Taking one of the two would drop the
            // other's keys, so the union is taken and what is missing is interpolated between a channel's own keys.
            var scene = Import(Document(
                Bone("Bone", translate: "0 0 0"),
                Animation("a1", Channel("Bone/location.X", "0 2", "0 4", stride: 1)) +
                Animation("a2", Channel("Bone/location.Y", "0 1 2", "0 1 2", stride: 1))));

            var frames = Frames(scene, "Bone");
            Assert.That(frames.Select(f => f.TimeStamp).ToArray(), Is.EqualTo(new double[] { 0, 1, 2 }));
            Assert.That(frames[1].Position.X, Is.EqualTo(2f).Within(0.001f), "X is linear between its own keys");
            //Y with its sign flipped: the file is Y_UP, the engine Y_DOWN
            Assert.That(frames[1].Position.Y, Is.EqualTo(-1f).Within(0.001f));
        }

        [Test]
        public void Animation_ReadsNestedAnimationElements()
        {
            // The usual way an action is exported: channels wrapped in a parent <animation>. The branch for them was
            // commented out, and the library imported as zero animations.
            var scene = Import(Document(
                Bone("Bone", translate: "0 0 0"),
                $@"<animation id=""outer"">{Animation("inner", Channel("Bone/location.X", "0 1", "0 7", stride: 1))}</animation>"));

            var frames = Frames(scene, "Bone");
            Assert.That(frames.Count, Is.EqualTo(2));
            Assert.That(frames[1].Position.X, Is.EqualTo(7f).Within(0.001f));
        }

        [Test]
        public void Animation_ReadsEverySamplerOfOneAnimation()
        {
            // Maya's grouping: several <sampler>/<channel> pairs inside one <animation>. The positional
            // items[^2]/items[^1] left only the last pair of them.
            var scene = Import(Document(
                Bone("Bone1", translate: "0 0 0") + Bone("Bone2", translate: "0 0 0"),
                Animation("both",
                    Channel("Bone1/location.X", "0 1", "0 3", stride: 1) +
                    Channel("Bone2/location.X", "0 1", "0 6", stride: 1))));

            Assert.That(Frames(scene, "Bone1")[1].Position.X, Is.EqualTo(3f).Within(0.001f));
            Assert.That(Frames(scene, "Bone2")[1].Position.X, Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void Animation_KeepsMatrixChannels()
        {
            // The matrix-baked path is the one swordman uses. Nothing above may break it.
            var moved = "1 0 0 9  0 1 0 0  0 0 1 0  0 0 0 1";
            var scene = Import(Document(
                Bone("Bone", translate: "0 0 0"),
                Channel("Bone/transform", "0 1",
                    "1 0 0 0  0 1 0 0  0 0 1 0  0 0 0 1   " + moved, stride: 16, type: "float4x4")));

            var frames = Frames(scene, "Bone");
            Assert.That(frames.Count, Is.EqualTo(2));
            Assert.That(frames[1].Position, Is.EqualTo(new Vector3F(9, 0, 0)).Using(Vector3Comparer));
        }

        [Test]
        public void Animation_SurvivesTargetWithoutMember()
        {
            // A target without '/' is legal, and Substring(0, -1) threw on it
            var scene = Import(Document(
                Bone("Bone", translate: "0 0 0"),
                Channel("Bone", "0 1", "0 1", stride: 1)));

            Assert.That(scene, Is.Not.Null);
        }

        [Test]
        public void Skin_KeepsFourHeaviestBonesAndRenormalizes()
        {
            // Six bones on a vertex. The extras were dropped without renormalizing - the weights summed to less than
            // one and skinning dragged that vertex towards the origin. Every vertex of the mesh needs a weight; only
            // the first has six bones, and that is the one under test.
            var scene = Import(SkinnedDocument(
                weights: "0.05 0.10 0.40 0.20 0.05 0.20",
                vcount: "6 1 1",
                v: "0 0  1 1  2 2  3 3  4 4  5 5   0 2   0 2",
                vertexWeightCount: 3));

            var controller = scene.Controllers.Values.Single();
            var weights = controller.BoneWeights[0];
            var sum = weights.X + weights.Y + weights.Z + weights.W;

            Assert.That(sum, Is.EqualTo(1f).Within(0.001f), "the weights must sum to one");

            // What should remain is 0.40, 0.20, 0.20, 0.10 - each divided by their total of 0.90
            var kept = new[] { weights.X, weights.Y, weights.Z, weights.W }.OrderByDescending(x => x).ToArray();
            Assert.That(kept[0], Is.EqualTo(0.40f / 0.90f).Within(0.001f));
            Assert.That(kept[3], Is.EqualTo(0.10f / 0.90f).Within(0.001f));
        }

        [Test]
        public void Skin_PutsBonesInTheSameFrameAsTheMesh()
        {
            // AssembleModel moves the meshes into the engine's system, while bones and key frames used to stay in
            // the file's axes. For Y_UP that flipped the skeleton relative to its own model - and nobody said a word.
            var scene = Import(SkinnedDocument(
                weights: "1", vcount: "1 1 1", v: "0 0  0 0  0 0", vertexWeightCount: 3,
                boneOffsetY: 2, positions: "0 0 0  1 2 0  0 2 0"));

            var farthestVertex = scene.Models.Dependencies
                .SelectMany(m => m.Meshes).SelectMany(m => m.Points)
                .Select(p => p.As<float>().Y)
                .OrderByDescending(Math.Abs).First();

            var controller = scene.Controllers.Values.Single();
            Matrix4x4F.Invert(controller.JointMatrices[0]).Decompose(out _, out _, out var bone);

            Assert.That(Math.Sign(bone.Y), Is.EqualTo(Math.Sign(farthestVertex)),
                "the bone and the mesh vertices must point the same way");
            Assert.That(Math.Abs(bone.Y), Is.EqualTo(2f).Within(0.001f));
        }

        #region Building the file

        private static readonly System.Collections.Generic.IComparer<Vector3F> Vector3Comparer =
            new Vector3Tolerance();

        private sealed class Vector3Tolerance : System.Collections.Generic.IComparer<Vector3F>
        {
            public int Compare(Vector3F x, Vector3F y) => (x - y).Length() < 0.001f ? 0 : 1;
        }

        private static SceneData.FrameCollection Frames(SceneData scene, string jointId)
        {
            var frames = scene.Clips.Values.SelectMany(clip => clip.Values).FirstOrDefault(x => x.JointId == jointId);
            Assert.That(frames, Is.Not.Null, $"no animation found for joint '{jointId}'");
            return frames;
        }

        private SceneData Import(string document)
        {
            var path = Path.Combine(tempDirectory, "model.dae");
            File.WriteAllText(path, document);
            return new ModelConverter().ImportFile(path);
        }

        private static int samplerCounter;

        private static string Channel(string target, string times, string values, int stride,
            string type = "float")
        {
            var id = "s" + (++samplerCounter);
            var timeCount = times.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var valueCount = values.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var interpolation = string.Join(" ", Enumerable.Repeat("LINEAR", timeCount));

            return $@"
        <source id=""{id}-in"">
          <float_array id=""{id}-in-array"" count=""{timeCount}"">{times}</float_array>
          <technique_common><accessor source=""#{id}-in-array"" count=""{timeCount}"" stride=""1"">
            <param name=""TIME"" type=""float""/></accessor></technique_common>
        </source>
        <source id=""{id}-out"">
          <float_array id=""{id}-out-array"" count=""{valueCount}"">{values}</float_array>
          <technique_common><accessor source=""#{id}-out-array"" count=""{valueCount / stride}"" stride=""{stride}"">
            <param name=""VALUE"" type=""{type}""/></accessor></technique_common>
        </source>
        <source id=""{id}-interp"">
          <Name_array id=""{id}-interp-array"" count=""{timeCount}"">{interpolation}</Name_array>
          <technique_common><accessor source=""#{id}-interp-array"" count=""{timeCount}"" stride=""1"">
            <param name=""INTERPOLATION"" type=""name""/></accessor></technique_common>
        </source>
        <sampler id=""{id}-sampler"">
          <input semantic=""INPUT"" source=""#{id}-in""/>
          <input semantic=""OUTPUT"" source=""#{id}-out""/>
          <input semantic=""INTERPOLATION"" source=""#{id}-interp""/>
        </sampler>
        <channel source=""#{id}-sampler"" target=""{target}""/>";
        }

        private static string Animation(string id, string body) => $@"<animation id=""{id}"">{body}</animation>";

        private static string Bone(string id, string translate, float? rotationZ = null) => $@"
      <node id=""{id}"" name=""{id}"" sid=""{id}"" type=""JOINT"">
        <translate sid=""location"">{translate}</translate>
        {(rotationZ.HasValue ? $@"<rotate sid=""rotationZ"">0 0 1 {rotationZ.Value}</rotate>" : "")}
        <scale sid=""scale"">1 1 1</scale>
      </node>";

        //Channels not already wrapped in <animation> get wrapped here - it keeps the tests themselves shorter
        private static string Document(string nodes, string animations)
        {
            if (!animations.TrimStart().StartsWith("<animation")) animations = Animation("anim", animations);

            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<COLLADA xmlns=""http://www.collada.org/2005/11/COLLADASchema"" version=""1.4.1"">
  <asset><up_axis>Y_UP</up_axis></asset>
  <library_animations>{animations}</library_animations>
  <library_visual_scenes><visual_scene id=""Scene"" name=""Scene"">{nodes}</visual_scene></library_visual_scenes>
  <scene><instance_visual_scene url=""#Scene""/></scene>
</COLLADA>";
        }

        private static string SkinnedDocument(string weights, string vcount, string v, int vertexWeightCount,
            float boneOffsetY = 0, string positions = "0 0 0  1 0 0  0 1 0")
        {
            var weightCount = weights.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var joints = weightCount >= 6 ? 6 : 1;
            var names = string.Join(" ", Enumerable.Range(0, joints).Select(i => "B" + i));
            // The inverse bind matrix: a bone standing at +Y is written as a -Y translation
            var bind = string.Join(" ", Enumerable.Repeat(
                $"1 0 0 0  0 1 0 {(-boneOffsetY).ToString(CultureInfo.InvariantCulture)}  0 0 1 0  0 0 0 1", joints));
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<COLLADA xmlns=""http://www.collada.org/2005/11/COLLADASchema"" version=""1.4.1"">
  <asset><up_axis>Y_UP</up_axis></asset>
  <library_geometries>
    <geometry id=""body"" name=""body"">
      <mesh>
        <source id=""body-positions"">
          <float_array id=""body-positions-array"" count=""9"">{positions}</float_array>
          <technique_common><accessor source=""#body-positions-array"" count=""3"" stride=""3"">
            <param name=""X"" type=""float""/><param name=""Y"" type=""float""/><param name=""Z"" type=""float""/>
          </accessor></technique_common>
        </source>
        <vertices id=""body-vertices""><input semantic=""POSITION"" source=""#body-positions""/></vertices>
        <polylist count=""1"">
          <input semantic=""VERTEX"" source=""#body-vertices"" offset=""0""/>
          <vcount>3</vcount><p>0 1 2</p>
        </polylist>
      </mesh>
    </geometry>
  </library_geometries>
  <library_controllers>
    <controller id=""skin1"" name=""skin1"">
      <skin source=""#body"">
        <bind_shape_matrix>1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</bind_shape_matrix>
        <source id=""skin1-joints"">
          <Name_array id=""skin1-joints-array"" count=""{joints}"">{names}</Name_array>
          <technique_common><accessor source=""#skin1-joints-array"" count=""{joints}"" stride=""1"">
            <param name=""JOINT"" type=""name""/></accessor></technique_common>
        </source>
        <source id=""skin1-bind"">
          <float_array id=""skin1-bind-array"" count=""{joints * 16}"">{bind}</float_array>
          <technique_common><accessor source=""#skin1-bind-array"" count=""{joints}"" stride=""16"">
            <param name=""TRANSFORM"" type=""float4x4""/></accessor></technique_common>
        </source>
        <source id=""skin1-weights"">
          <float_array id=""skin1-weights-array"" count=""{weightCount}"">{weights}</float_array>
          <technique_common><accessor source=""#skin1-weights-array"" count=""{weightCount}"" stride=""1"">
            <param name=""WEIGHT"" type=""float""/></accessor></technique_common>
        </source>
        <joints>
          <input semantic=""JOINT"" source=""#skin1-joints""/>
          <input semantic=""INV_BIND_MATRIX"" source=""#skin1-bind""/>
        </joints>
        <vertex_weights count=""{vertexWeightCount}"">
          <input semantic=""JOINT"" source=""#skin1-joints"" offset=""0""/>
          <input semantic=""WEIGHT"" source=""#skin1-weights"" offset=""1""/>
          <vcount>{vcount}</vcount>
          <v>{v}</v>
        </vertex_weights>
      </skin>
    </controller>
  </library_controllers>
  <library_visual_scenes><visual_scene id=""Scene"" name=""Scene"">
    <node id=""body-node"" name=""body"" type=""NODE"">
      <matrix sid=""transform"">1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</matrix>
      <instance_controller url=""#skin1""/>
    </node>
  </visual_scene></library_visual_scenes>
  <scene><instance_visual_scene url=""#Scene""/></scene>
</COLLADA>";
        }

        #endregion
    }
}
