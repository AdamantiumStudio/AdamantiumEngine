using System;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>A Collada skin comes in as glTF's does: its joints are nodes of the model, the skin names them by their IDs,
/// the bind shape is taken into the inverse bind matrices, and the clips move the joints' nodes.</summary>
[TestFixture]
public class ColladaSkinTests
{
    private string tempDirectory;

    [SetUp]
    public void SetUp()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "adamantium-skin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
    }

    [Test]
    public void Joints_AreNodesOfTheModel_UnderTheirArmature()
    {
        var scene = Import(LeggedDocument());

        var armature = scene.Models.Dependencies.Single(m => m.Name == "Armature");
        var hip = armature.Dependencies.Single();
        var knee = hip.Dependencies.Single();

        Assert.Multiple(() =>
        {
            Assert.That(hip.ID, Is.EqualTo("Armature_Hip"));
            Assert.That(knee.ID, Is.EqualTo("Armature_Knee"));
            Assert.That(knee.Position.Length(), Is.EqualTo(2f).Within(1e-5f), "a joint keeps its own place");
        });
    }

    [Test]
    public void TheSkin_NamesItsJointsByTheirNodes_FoundBySidUnderItsSkeleton()
    {
        var scene = Import(LeggedDocument());

        var skin = scene.Controllers.Values.Single();

        Assert.That(skin.JointNames, Is.EqualTo(new[] { "Armature_Hip", "Armature_Knee" }));
    }

    [Test]
    public void TheBindShape_IsTakenIntoEachJointsMatrix_BeforeIt()
    {
        // At the bind pose a joint stands where its inverse bind matrix undoes, so the two cancel and the mesh is left
        // in its bind shape. The bind shape turns and moves, so the two orders give different matrices.
        var scene = Import(LeggedDocument());

        var skin = scene.Controllers.Values.Single();
        var armature = scene.Models.Dependencies.Single(m => m.Name == "Armature");
        var hip = armature.Dependencies.Single();
        var knee = hip.Dependencies.Single();
        var kneeWorld = Local(knee) * Local(hip) * Local(armature);
        var bindShape = Matrix4x4F.Transpose(new Matrix4x4F([0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 3, 0, 0, 0, 1]));
        var expected = AxisConversion.Convert(bindShape, AxisConversion.Between(UpAxis.Y_UP_RH, UpAxis.Y_DOWN_RH));

        Assert.That(Matrix4x4F.NearEqual(skin.JointMatrices[1] * kneeWorld, expected), Is.True);
    }

    [Test]
    public void ANodeWithNoTransform_StandsAsItIs_NotAsTheNodeReadBeforeIt()
    {
        var scene = Import(LeggedDocument(boxFirst: true));

        var armature = scene.Models.Dependencies.Single(m => m.Name == "Armature");

        Assert.That(armature.Position, Is.EqualTo(Vector3F.Zero));
    }

    [Test]
    public void AJointNothingNames_IsANodeOfItsOwn()
    {
        var scene = Import(LeggedDocument(kneeContent: @"<node type=""JOINT""/><node type=""JOINT""/>"));

        var knee = scene.Models.Dependencies.Single(m => m.Name == "Armature").Dependencies.Single().Dependencies.Single();

        Assert.That(knee.Dependencies.Select(m => m.ID).Distinct().Count(), Is.EqualTo(2));
    }

    [Test]
    public void AMeshOnAJoint_HangsFromIt()
    {
        var scene = Import(LeggedDocument(kneeContent: @"<instance_geometry url=""#leg""/>"));

        var knee = scene.Models.Dependencies.Single(m => m.Name == "Armature").Dependencies.Single().Dependencies.Single();

        Assert.That(knee.Dependencies.Select(m => m.ID), Is.EqualTo(new[] { "leg" }));
    }

    [Test]
    public void AMorph_IsReportedAndTheRestImported()
    {
        var converter = new ModelConverter();
        var scene = Import(LeggedDocument(morph: true), converter);

        Assert.Multiple(() =>
        {
            Assert.That(scene.Controllers.Values.Single().ControllerId, Is.EqualTo("leg-skin"));
            Assert.That(converter.UnsupportedFeatures, Has.Some.Contains("morph"));
        });
    }

    [Test]
    public void AClip_MovesTheJointsNode_ByItsID()
    {
        var scene = Import(LeggedDocument());

        var clip = scene.Clips.Values.Single();

        Assert.That(clip.Keys, Is.EqualTo(new[] { "Armature_Knee" }));
    }

    [Test]
    public void AJointTheSceneLacks_IsReportedAndKept()
    {
        var scene = Import(LeggedDocument(jointNames: "Hip Ankle"));

        var skin = scene.Controllers.Values.Single();

        Assert.That(skin.JointNames, Is.EqualTo(new[] { "Armature_Hip", "Ankle" }));
    }

    private SceneData Import(string document, ModelConverter converter = null)
    {
        var path = Path.Combine(tempDirectory, "legged.dae");
        File.WriteAllText(path, document);
        return (converter ?? new ModelConverter()).ImportFile(path);
    }

    private static Matrix4x4F Local(SceneData.Model model) =>
        Matrix4x4.CreateScale<float>(model.Scale) * Matrix4x4.CreateFromQuaternion<float>(model.Rotation) *
        Matrix4x4.CreateTranslation<float>(model.Position);

    // A leg: an armature holding a hip with a knee 2 below it, joints whose ids are not their sids; a triangle skinned
    // to both, its bind shape turned a quarter round Z and moved 3 along it; a clip turning the knee.
    private static string LeggedDocument(string jointNames = "Hip Knee", bool boxFirst = false, string kneeContent = "",
        bool morph = false) => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<COLLADA xmlns=""http://www.collada.org/2005/11/COLLADASchema"" version=""1.4.1"">
  <asset><up_axis>Y_UP</up_axis></asset>
  <library_animations>
    <animation id=""knee-turn"">
      <source id=""knee-in""><float_array id=""knee-in-array"" count=""2"">0 1</float_array>
        <technique_common><accessor source=""#knee-in-array"" count=""2"" stride=""1""><param name=""TIME"" type=""float""/></accessor></technique_common>
      </source>
      <source id=""knee-out""><float_array id=""knee-out-array"" count=""2"">0 45</float_array>
        <technique_common><accessor source=""#knee-out-array"" count=""2"" stride=""1""><param name=""ANGLE"" type=""float""/></accessor></technique_common>
      </source>
      <sampler id=""knee-sampler""><input semantic=""INPUT"" source=""#knee-in""/><input semantic=""OUTPUT"" source=""#knee-out""/></sampler>
      <channel source=""#knee-sampler"" target=""Armature_Knee/rotationZ.ANGLE""/>
    </animation>
  </library_animations>
  <library_geometries>
    <geometry id=""leg"" name=""leg"">
      <mesh>
        <source id=""leg-positions"">
          <float_array id=""leg-positions-array"" count=""9"">0 0 0  1 0 0  0 1 0</float_array>
          <technique_common><accessor source=""#leg-positions-array"" count=""3"" stride=""3"">
            <param name=""X"" type=""float""/><param name=""Y"" type=""float""/><param name=""Z"" type=""float""/>
          </accessor></technique_common>
        </source>
        <vertices id=""leg-vertices""><input semantic=""POSITION"" source=""#leg-positions""/></vertices>
        <polylist count=""1""><input semantic=""VERTEX"" source=""#leg-vertices"" offset=""0""/><vcount>3</vcount><p>0 1 2</p></polylist>
      </mesh>
    </geometry>
  </library_geometries>
  <library_controllers>
    <controller id=""leg-skin"" name=""leg-skin"">
      <skin source=""#leg"">
        <bind_shape_matrix>0 -1 0 0  1 0 0 0  0 0 1 3  0 0 0 1</bind_shape_matrix>
        <source id=""leg-skin-joints"">
          <Name_array id=""leg-skin-joints-array"" count=""2"">{jointNames}</Name_array>
          <technique_common><accessor source=""#leg-skin-joints-array"" count=""2"" stride=""1""><param name=""JOINT"" type=""name""/></accessor></technique_common>
        </source>
        <source id=""leg-skin-bind"">
          <float_array id=""leg-skin-bind-array"" count=""32"">1 0 0 0  0 1 0 0  0 0 1 0  0 0 0 1  1 0 0 0  0 1 0 2  0 0 1 0  0 0 0 1</float_array>
          <technique_common><accessor source=""#leg-skin-bind-array"" count=""2"" stride=""16""><param name=""TRANSFORM"" type=""float4x4""/></accessor></technique_common>
        </source>
        <source id=""leg-skin-weights"">
          <float_array id=""leg-skin-weights-array"" count=""1"">1</float_array>
          <technique_common><accessor source=""#leg-skin-weights-array"" count=""1"" stride=""1""><param name=""WEIGHT"" type=""float""/></accessor></technique_common>
        </source>
        <joints>
          <input semantic=""JOINT"" source=""#leg-skin-joints""/>
          <input semantic=""INV_BIND_MATRIX"" source=""#leg-skin-bind""/>
        </joints>
        <vertex_weights count=""3"">
          <input semantic=""JOINT"" source=""#leg-skin-joints"" offset=""0""/>
          <input semantic=""WEIGHT"" source=""#leg-skin-weights"" offset=""1""/>
          <vcount>1 1 1</vcount>
          <v>0 0  1 0  1 0</v>
        </vertex_weights>
      </skin>
    </controller>{(morph ? @"
    <controller id=""leg-morph"">
      <morph source=""#leg"" method=""NORMALIZED"">
        <source id=""leg-morph-targets""><IDREF_array id=""leg-morph-targets-array"" count=""1"">leg</IDREF_array></source>
        <source id=""leg-morph-weights""><float_array id=""leg-morph-weights-array"" count=""1"">0</float_array></source>
        <targets>
          <input semantic=""MORPH_TARGET"" source=""#leg-morph-targets""/>
          <input semantic=""MORPH_WEIGHT"" source=""#leg-morph-weights""/>
        </targets>
      </morph>
    </controller>" : "")}
  </library_controllers>
  <library_visual_scenes><visual_scene id=""Scene"" name=""Scene"">{(boxFirst ? @"
    <node id=""Box"" name=""Box"" type=""NODE"">
      <matrix sid=""transform"">1 0 0 7  0 1 0 8  0 0 1 9  0 0 0 1</matrix>
      <instance_geometry url=""#leg""/>
    </node>" : "")}
    <node id=""Armature"" name=""Armature"" type=""NODE"">
      <node id=""Armature_Hip"" name=""Hip"" sid=""Hip"" type=""JOINT"">
        <translate sid=""location"">0 0 0</translate>
        <node id=""Armature_Knee"" name=""Knee"" sid=""Knee"" type=""JOINT"">
          <translate sid=""location"">0 -2 0</translate>
          <rotate sid=""rotationZ"">0 0 1 0</rotate>
          {kneeContent}
        </node>
      </node>
    </node>
    <node id=""leg-node"" name=""leg"" type=""NODE"">
      <instance_controller url=""#leg-skin""><skeleton>#Armature_Hip</skeleton></instance_controller>
    </node>
  </visual_scene></library_visual_scenes>
  <scene><instance_visual_scene url=""#Scene""/></scene>
</COLLADA>";
}
