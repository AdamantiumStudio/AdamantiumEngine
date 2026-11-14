using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Adamantium.MVVM.Tests;

// A type of code loaded to be unloaded again - a game built in an editor - is reached like any other, and what is known
// of it does not keep it, or its assembly, alive once nothing else holds it.
[TestFixture]
public class PropertyAccessorsCollectibleTests
{
    [Test]
    public void ACollectibleType_IsReached_AndLetGo()
    {
        var gone = ReachAndDrop();

        for (var attempt = 0; attempt < 10 && gone.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.That(gone.IsAlive, Is.False, "the type is let go");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReachAndDrop()
    {
        var type = BuildType();
        var instance = Activator.CreateInstance(type);

        PropertyAccessors.Find(type, "Speed").SetValue(instance, 2.5);
        var read = PropertyAccessors.GetValue(instance, "Speed");
        PropertyAccessors.FindDeclared(type, "Speed");

        Assert.That(read, Is.EqualTo(2.5));
        return new WeakReference(type);
    }

    private static Type BuildType()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Collectible.Game"),
            AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("Collectible.Game");
        var type = module.DefineType("Game.Patrol", TypeAttributes.Public | TypeAttributes.Class);
        var storage = type.DefineField("_speed", typeof(double), FieldAttributes.Private);
        var property = type.DefineProperty("Speed", PropertyAttributes.None, typeof(double), Type.EmptyTypes);
        const MethodAttributes accessor = MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        var getter = type.DefineMethod("get_Speed", accessor, typeof(double), Type.EmptyTypes);
        var getCode = getter.GetILGenerator();
        getCode.Emit(OpCodes.Ldarg_0);
        getCode.Emit(OpCodes.Ldfld, storage);
        getCode.Emit(OpCodes.Ret);

        var setter = type.DefineMethod("set_Speed", accessor, null, [typeof(double)]);
        var setCode = setter.GetILGenerator();
        setCode.Emit(OpCodes.Ldarg_0);
        setCode.Emit(OpCodes.Ldarg_1);
        setCode.Emit(OpCodes.Stfld, storage);
        setCode.Emit(OpCodes.Ret);

        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        return type.CreateType();
    }
}
