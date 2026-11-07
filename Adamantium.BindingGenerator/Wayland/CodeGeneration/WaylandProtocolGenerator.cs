using QuantumBinding.Generator;
using QuantumBinding.Generator.AST;

namespace Adamantium.BindingGenerator.Wayland.CodeGeneration;

/// <summary>Writes one protocol's file: a class per interface with its enums, requests and events.</summary>
public sealed class WaylandProtocolGenerator : WaylandCodeGenerator
{
    private readonly Protocol protocol;

    public WaylandProtocolGenerator(ProcessingContext context, TranslationUnit[] units, IReadOnlyList<Protocol> protocols,
        Protocol protocol) : base(context, units, protocols)
    {
        this.protocol = protocol;
    }

    public override string GetFileName(TranslationUnit unit)
    {
        return Naming.FileName(protocol.FileName);
    }

    public override void Run()
    {
        Header(protocol.FileName, protocol.Copyright);
        WriteLine("using System;");
        NewLine();
        WriteLine($"namespace {Namespace};");

        foreach (var @interface in protocol.Interfaces)
        {
            NewLine();
            Interface(@interface);
        }
    }

    private void Interface(Interface i)
    {
        var className = Naming.Pascal(i.Name);
        var names = new MemberNames(className, i);

        Summary(i.Summary);
        WriteLine($"public sealed unsafe partial class {className} : WaylandProxy, IWaylandInterface<{className}>");
        WriteOpenBraceAndIndent();
        WriteLine($"public const string InterfaceName = \"{i.Name}\";");
        WriteLine($"public const uint InterfaceVersion = {i.Version};");
        NewLine();
        WriteLine($"private {className}(nint handle, uint version) : base(handle, version)");
        WriteOpenBraceAndIndent();
        UnindentAndWriteCloseBrace();
        NewLine();
        WriteLine($"public static WlInterface* Interface => WaylandInterfaces.{className};");
        NewLine();
        WriteLine($"static {className} IWaylandInterface<{className}>.Create(nint handle, uint version) => new(handle, version);");

        foreach (var e in i.Enums)
        {
            NewLine();
            Enum(e);
        }

        for (var opcode = 0; opcode < i.Requests.Count; opcode++)
        {
            NewLine();
            Request(i, i.Requests[opcode], opcode, names.Requests[opcode]);
        }

        for (var opcode = 0; opcode < i.Events.Count; opcode++)
        {
            NewLine();
            EventDeclaration(i, i.Events[opcode], names.Events[opcode]);
        }

        NewLine();
        WriteLine("protected override void DispatchEvent(uint opcode, WlArgument* args)");
        WriteOpenBraceAndIndent();
        if (i.Events.Count > 0)
        {
            WriteLine("switch (opcode)");
            WriteOpenBraceAndIndent();
            for (var opcode = 0; opcode < i.Events.Count; opcode++)
            {
                WriteLine($"case {opcode}:");
                WriteOpenBraceAndIndent();
                EventDispatch(i, i.Events[opcode], names.Events[opcode]);
                WriteLine("break;");
                UnindentAndWriteCloseBrace();
            }

            UnindentAndWriteCloseBrace();
        }

        UnindentAndWriteCloseBrace();
        UnindentAndWriteCloseBrace();
    }

    private void Enum(WaylandEnum e)
    {
        var name = Naming.Pascal(e.Name);
        Summary(e.Summary);
        if (e.IsBitfield)
        {
            WriteLine("[Flags]");
        }

        WriteLine($"public enum {name} : uint");
        WriteOpenBraceAndIndent();
        foreach (var entry in e.Entries)
        {
            Summary(entry.Summary);
            WriteLine($"{Naming.Pascal(entry.Name, name)} = {entry.Value},");
        }

        UnindentAndWriteCloseBrace();
    }

    private void Request(Interface i, Message m, int opcode, string name)
    {
        var newId = m.NewId;
        var untyped = m.HasUntypedNewId;
        var parameters = m.Arguments.Where(a => a.Type != ArgumentType.NewId)
            .Select(a => $"{RequestType(i, a)} {Naming.Camel(a.Name)}").ToList();

        string returnType;
        if (untyped)
        {
            if (m.Arguments.Any(a => Naming.Camel(a.Name) == "version"))
            {
                throw new InvalidDataException($"{i.Name}.{m.Name}: an argument named version meets the bound version");
            }

            parameters.Add("uint version");
            returnType = "T";
        }
        else if (newId != null)
        {
            RequireInterface(newId.Interface);
            returnType = Naming.Pascal(newId.Interface);
        }
        else
        {
            returnType = "void";
        }

        Summary(m.Summary);
        var generic = untyped ? "<T>" : "";
        WriteLine($"public {returnType} {name}{generic}({string.Join(", ", parameters)})");
        if (untyped)
        {
            PushIndent();
            WriteLine("where T : WaylandProxy, IWaylandInterface<T>");
            PopIndent();
        }

        WriteOpenBraceAndIndent();

        var slots = m.Arguments.Sum(a => a.Type == ArgumentType.NewId && a.Interface == null ? 3 : 1);

        var closers = 0;
        foreach (var a in m.Arguments)
        {
            var arg = Naming.Camel(a.Name);
            if (a.Type == ArgumentType.String)
            {
                WriteLine($"using var {Naming.Local(arg, "Utf8")} = new WaylandString({arg});");
            }
            else if (a.Type == ArgumentType.Array)
            {
                WriteLine($"fixed (byte* {Naming.Local(arg, "Data")} = {arg})");
                WriteOpenBraceAndIndent();
                WriteLine($"var {Naming.Local(arg, "Array")} = new WlArray({Naming.Local(arg, "Data")}, {arg}.Length);");
                closers++;
            }
        }

        WriteLine($"var wlArgs = stackalloc WlArgument[{Math.Max(slots, 1)}];");
        var slot = 0;
        foreach (var a in m.Arguments)
        {
            var arg = Naming.Camel(a.Name);
            switch (a.Type)
            {
                case ArgumentType.Int:
                    WriteLine($"wlArgs[{slot++}].i = {(a.Enum != null ? $"(int){arg}" : arg)};");
                    break;
                case ArgumentType.Uint:
                    WriteLine($"wlArgs[{slot++}].u = {(a.Enum != null ? $"(uint){arg}" : arg)};");
                    break;
                case ArgumentType.Fixed:
                    WriteLine($"wlArgs[{slot++}].f = WlFixed.FromDouble({arg});");
                    break;
                case ArgumentType.String:
                    WriteLine($"wlArgs[{slot++}].s = {Naming.Local(arg, "Utf8")}.Pointer;");
                    break;
                case ArgumentType.Object:
                    WriteLine(a.AllowNull
                        ? $"wlArgs[{slot++}].o = {arg}?.Handle ?? 0;"
                        : $"wlArgs[{slot++}].o = {arg}.Handle;");
                    break;
                case ArgumentType.NewId when a.Interface == null:
                    WriteLine($"wlArgs[{slot++}].s = T.Interface->name;");
                    WriteLine($"wlArgs[{slot++}].u = version;");
                    WriteLine($"wlArgs[{slot++}].n = 0;");
                    break;
                case ArgumentType.NewId:
                    WriteLine($"wlArgs[{slot++}].n = 0;");
                    break;
                case ArgumentType.Array:
                    WriteLine($"wlArgs[{slot++}].a = &{Naming.Local(arg, "Array")};");
                    break;
                case ArgumentType.Fd:
                    WriteLine($"wlArgs[{slot++}].h = {arg};");
                    break;
            }
        }

        if (untyped)
        {
            WriteLine($"return Create<T>(Marshal({opcode}, T.Interface, version, wlArgs), version);");
        }
        else if (newId != null)
        {
            WriteLine($"return Create<{returnType}>(Marshal({opcode}, {returnType}.Interface, Version, wlArgs), Version);");
        }
        else if (m.IsDestructor)
        {
            WriteLine($"MarshalDestroy({opcode}, wlArgs);");
        }
        else
        {
            WriteLine($"Marshal({opcode}, null, Version, wlArgs);");
        }

        for (var c = 0; c < closers; c++)
        {
            UnindentAndWriteCloseBrace();
        }

        UnindentAndWriteCloseBrace();
    }

    private void EventDeclaration(Interface i, Message m, string name)
    {
        var parameters = m.Arguments.Select(a => $"{EventType(i, a)} {Naming.Camel(a.Name)}");
        Summary(m.Summary);
        WriteLine($"public delegate void {name}Handler({string.Join(", ", parameters)});");
        NewLine();
        Summary(m.Summary);
        WriteLine($"public event {name}Handler {name};");
    }

    private void EventDispatch(Interface i, Message m, string name)
    {
        List<string> values = [];
        for (var index = 0; index < m.Arguments.Count; index++)
        {
            var a = m.Arguments[index];
            var arg = $"args[{index}]";
            switch (a.Type)
            {
                case ArgumentType.Int:
                    values.Add(a.Enum != null ? $"({EnumType(i, a.Enum)}){arg}.i" : $"{arg}.i");
                    break;
                case ArgumentType.Uint:
                    values.Add(a.Enum != null ? $"({EnumType(i, a.Enum)}){arg}.u" : $"{arg}.u");
                    break;
                case ArgumentType.Fixed:
                    values.Add($"WlFixed.ToDouble({arg}.f)");
                    break;
                case ArgumentType.String:
                    values.Add(a.AllowNull ? $"WaylandString.Read({arg}.s)" : $"WaylandString.Read({arg}.s) ?? string.Empty");
                    break;
                case ArgumentType.Object:
                    values.Add($"Find<{ObjectType(a)}>({arg}.o)");
                    break;
                case ArgumentType.NewId:
                    var local = Naming.Local(Naming.Camel(a.Name), "Created");
                    RequireInterface(a.Interface);
                    WriteLine($"var {local} = Adopt<{Naming.Pascal(a.Interface)}>({arg}.o, Version);");
                    values.Add(local);
                    break;
                case ArgumentType.Array:
                    values.Add($"{arg}.a->AsSpan()");
                    break;
                case ArgumentType.Fd:
                    values.Add($"{arg}.h");
                    break;
            }
        }

        WriteLine($"{name}?.Invoke({string.Join(", ", values)});");
        if (m.IsDestructor)
        {
            WriteLine("DestroyProxy();");
        }
    }

    private string RequestType(Interface i, Argument a)
    {
        return a.Type switch
        {
            ArgumentType.Int or ArgumentType.Uint when a.Enum != null => EnumType(i, a.Enum),
            ArgumentType.Int => "int",
            ArgumentType.Uint => "uint",
            ArgumentType.Fixed => "double",
            ArgumentType.String => "string",
            ArgumentType.Object => ObjectType(a),
            ArgumentType.Array => "ReadOnlySpan<byte>",
            ArgumentType.Fd => "int",
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private string EventType(Interface i, Argument a)
    {
        return a.Type switch
        {
            ArgumentType.Int or ArgumentType.Uint when a.Enum != null => EnumType(i, a.Enum),
            ArgumentType.Int => "int",
            ArgumentType.Uint => "uint",
            ArgumentType.Fixed => "double",
            ArgumentType.String => "string",
            ArgumentType.Object => ObjectType(a),
            ArgumentType.NewId => Naming.Pascal(a.Interface),
            ArgumentType.Array => "ReadOnlySpan<byte>",
            ArgumentType.Fd => "int",
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private string ObjectType(Argument a)
    {
        if (a.Interface == null)
        {
            return "WaylandProxy";
        }

        RequireInterface(a.Interface);
        return Naming.Pascal(a.Interface);
    }

    private string EnumType(Interface i, string reference)
    {
        var dot = reference.IndexOf('.');
        var owner = dot < 0 ? i : Interfaces.GetValueOrDefault(reference[..dot]);
        var name = dot < 0 ? reference : reference[(dot + 1)..];

        if (owner == null || owner.Enums.All(e => e.Name != name))
        {
            throw new InvalidDataException($"{i.Name}: enum '{reference}' is not in the protocols read");
        }

        var enumName = Naming.Pascal(name);
        return owner == i ? enumName : $"{Naming.Pascal(owner.Name)}.{enumName}";
    }
}
