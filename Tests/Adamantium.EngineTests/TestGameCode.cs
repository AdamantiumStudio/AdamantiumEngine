using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Adamantium.ECS;
using Adamantium.ECS.Components;

namespace Adamantium.EngineTests;

/// <summary>Builds an assembly standing for one build of the game's code: components of the game, each a
/// <see cref="Behavior"/> with public fields, in a collectible context of its own - as the editor loads the game.</summary>
internal sealed class TestGameCode
{
    private readonly ModuleBuilder _module;
    private readonly AssemblyBuilder _assembly;

    public TestGameCode(string name)
    {
        _assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.RunAndCollect);
        _module = _assembly.DefineDynamicModule(name);
    }

    /// <summary>The assembly as its types have it, with every component added so far; null before the first.</summary>
    public Assembly Assembly { get; private set; }

    /// <summary>Adds a component named <paramref name="fullName"/> with <paramref name="fields"/>.</summary>
    public TestGameCode AddComponent(string fullName, params TestGameField[] fields)
    {
        var type = DefineComponent(fullName, fields);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        Assembly = type.CreateType().Assembly;
        return this;
    }

    /// <summary>Adds a component named <paramref name="fullName"/> with <paramref name="fields"/> that says it was named
    /// <paramref name="formerName"/> before.</summary>
    public TestGameCode AddMovedComponent(string fullName, string formerName, params TestGameField[] fields)
    {
        var type = DefineComponent(fullName, fields);
        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(MovedFromAttribute).GetConstructor([typeof(string)])!, [formerName]));
        type.DefineDefaultConstructor(MethodAttributes.Public);
        Assembly = type.CreateType().Assembly;
        return this;
    }

    /// <summary>Adds a component named <paramref name="fullName"/> whose constructor throws.</summary>
    public TestGameCode AddThrowingComponent(string fullName)
    {
        var type = DefineComponent(fullName, []);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var code = constructor.GetILGenerator();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Call, typeof(Behavior).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, Type.EmptyTypes));
        code.Emit(OpCodes.Ldstr, "The component's own code failed.");
        code.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)]));
        code.Emit(OpCodes.Throw);
        Assembly = type.CreateType().Assembly;
        return this;
    }

    private TypeBuilder DefineComponent(string fullName, IReadOnlyList<TestGameField> fields)
    {
        var type = _module.DefineType(fullName, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            typeof(Behavior));
        foreach (var field in fields)
        {
            DefineProperty(type, field);
        }

        return type;
    }

    private static void DefineProperty(TypeBuilder type, TestGameField field)
    {
        var storage = type.DefineField("_" + field.Name, field.Type, FieldAttributes.Private);
        var property = type.DefineProperty(field.Name, PropertyAttributes.None, field.Type, Type.EmptyTypes);
        const MethodAttributes accessor = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        var getter = type.DefineMethod("get_" + field.Name, accessor, field.Type, Type.EmptyTypes);
        var getCode = getter.GetILGenerator();
        getCode.Emit(OpCodes.Ldarg_0);
        getCode.Emit(OpCodes.Ldfld, storage);
        getCode.Emit(OpCodes.Ret);

        var setter = type.DefineMethod("set_" + field.Name, accessor, null, [field.Type]);
        var setCode = setter.GetILGenerator();
        setCode.Emit(OpCodes.Ldarg_0);
        setCode.Emit(OpCodes.Ldarg_1);
        setCode.Emit(OpCodes.Stfld, storage);
        setCode.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
    }
}
