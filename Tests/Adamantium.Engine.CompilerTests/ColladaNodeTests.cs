using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Models;
using Adamantium.Graphics.Core.Models;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Every Collada node is a model known by its own ID - not by the geometry it draws - so a clip moves a plain
/// node as it moves a joint, a geometry drawn by several nodes is on each of them with the materials each binds, and
/// nodes of one name stay apart.</summary>
[TestFixture]
public class ColladaNodeTests
{
    private string tempDirectory;

    [SetUp]
    public void SetUp()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "adamantium-nodes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
    }

    [Test]
    public void APlainNode_IsKnownByItsID_AndItsClipMovesIt()
    {
        var scene = Import(Document());

        var cube = Models(scene).Single(model => model.ID == "Cube");

        Assert.Multiple(() =>
        {
            Assert.That(cube.Meshes, Has.Count.EqualTo(2));
            Assert.That(scene.Clips.Values.Single().Keys, Is.EqualTo(new[] { "Cube" }), "the clip's key is the model's ID");
        });
    }

    [Test]
    public void AGeometryDrawnByTwoNodes_IsOnBoth_EachWithTheMaterialsItBinds()
    {
        var scene = Import(Document());

        var cube = Models(scene).Single(model => model.ID == "Cube");
        var copy = Models(scene).Single(model => model.ID == "Copy");

        Assert.Multiple(() =>
        {
            Assert.That(cube.Meshes.Select(mesh => mesh.MaterialID), Is.EqualTo(new[] { "red", "green" }),
                "bound by symbol, not by the order of the binding");
            Assert.That(copy.Meshes.Select(mesh => mesh.MaterialID), Is.EqualTo(new[] { "blue", "green" }));
            Assert.That(copy.Meshes, Has.None.AnyOf(cube.Meshes[0], cube.Meshes[1]), "each node has meshes of its own");
        });
    }

    [Test]
    public void NodesOfOneName_StayApart()
    {
        var scene = Import(Document());

        Assert.That(Models(scene).Where(model => model.Name == "Cube").Select(model => model.ID),
            Is.EquivalentTo(new[] { "Cube", "Copy" }));
    }

    [Test]
    public void ANodeNamedLikeAnother_WithoutAnID_GetsAnIDOfItsOwn()
    {
        var scene = Import(Document(extraNodes: @"
      <node name=""Cube"" type=""NODE""><instance_geometry url=""#cube-mesh""/></node>"));

        var ids = Models(scene).Where(model => model.Name == "Cube").Select(model => model.ID).ToList();

        Assert.That(ids, Has.Count.EqualTo(3).And.Unique);
    }

    [Test]
    public void AGroupDrawingNothing_IsLeftOut_ButNotItsSiblings()
    {
        var scene = Import(Document(extraNodes: @"
      <node id=""Group"" name=""Group"" type=""NODE"">
        <node id=""Empty"" name=""Empty"" type=""NODE""><node id=""Lamp"" name=""Lamp""><instance_light url=""#none""/></node></node>
        <node id=""Kept"" name=""Kept"" type=""NODE""><instance_geometry url=""#cube-mesh""/></node>
      </node>"));

        var group = Models(scene).Single(model => model.ID == "Group");

        Assert.That(group.Dependencies.Select(model => model.ID), Is.EqualTo(new[] { "Kept" }));
    }

    private SceneData Import(string document)
    {
        var path = Path.Combine(tempDirectory, "nodes.dae");
        File.WriteAllText(path, document);
        return new ModelConverter().ImportFile(path);
    }

    private static List<SceneData.Model> Models(SceneData scene)
    {
        var all = new List<SceneData.Model>();
        var stack = new Stack<SceneData.Model>();
        stack.Push(scene.Models);
        while (stack.Count > 0)
        {
            var model = stack.Pop();
            all.Add(model);
            foreach (var child in model.Dependencies)
            {
                stack.Push(child);
            }
        }

        return all;
    }

    // A triangle in two parts, by the symbols "top" and "side"; drawn by Cube, which binds them in the other order and
    // has a clip moving it, and by Copy - of the same name - which binds "top" to another material.
    private static string Document(string extraNodes = "") => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<COLLADA xmlns=""http://www.collada.org/2005/11/COLLADASchema"" version=""1.4.1"">
  <asset><up_axis>Y_UP</up_axis></asset>
  <library_animations>
    <animation id=""slide"">
      <source id=""slide-in""><float_array id=""slide-in-array"" count=""2"">0 1</float_array>
        <technique_common><accessor source=""#slide-in-array"" count=""2"" stride=""1""><param name=""TIME"" type=""float""/></accessor></technique_common>
      </source>
      <source id=""slide-out""><float_array id=""slide-out-array"" count=""2"">0 4</float_array>
        <technique_common><accessor source=""#slide-out-array"" count=""2"" stride=""1""><param name=""X"" type=""float""/></accessor></technique_common>
      </source>
      <sampler id=""slide-sampler""><input semantic=""INPUT"" source=""#slide-in""/><input semantic=""OUTPUT"" source=""#slide-out""/></sampler>
      <channel source=""#slide-sampler"" target=""Cube/location.X""/>
    </animation>
  </library_animations>
  <library_geometries>
    <geometry id=""cube-mesh"" name=""cube"">
      <mesh>
        <source id=""cube-positions"">
          <float_array id=""cube-positions-array"" count=""9"">0 0 0  1 0 0  0 1 0</float_array>
          <technique_common><accessor source=""#cube-positions-array"" count=""3"" stride=""3"">
            <param name=""X"" type=""float""/><param name=""Y"" type=""float""/><param name=""Z"" type=""float""/>
          </accessor></technique_common>
        </source>
        <vertices id=""cube-vertices""><input semantic=""POSITION"" source=""#cube-positions""/></vertices>
        <polylist count=""1"" material=""top""><input semantic=""VERTEX"" source=""#cube-vertices"" offset=""0""/><vcount>3</vcount><p>0 1 2</p></polylist>
        <polylist count=""1"" material=""side""><input semantic=""VERTEX"" source=""#cube-vertices"" offset=""0""/><vcount>3</vcount><p>2 1 0</p></polylist>
      </mesh>
    </geometry>
  </library_geometries>
  <library_visual_scenes><visual_scene id=""Scene"" name=""Scene"">
      <node id=""Cube"" name=""Cube"" type=""NODE"">
        <translate sid=""location"">0 0 0</translate>
        <instance_geometry url=""#cube-mesh"">
          <bind_material><technique_common>
            <instance_material symbol=""side"" target=""#green""/>
            <instance_material symbol=""top"" target=""#red""/>
          </technique_common></bind_material>
        </instance_geometry>
      </node>
      <node id=""Copy"" name=""Cube"" type=""NODE"">
        <instance_geometry url=""#cube-mesh"">
          <bind_material><technique_common>
            <instance_material symbol=""top"" target=""#blue""/>
            <instance_material symbol=""side"" target=""#green""/>
          </technique_common></bind_material>
        </instance_geometry>
      </node>{extraNodes}
  </visual_scene></library_visual_scenes>
  <scene><instance_visual_scene url=""#Scene""/></scene>
</COLLADA>";
}
