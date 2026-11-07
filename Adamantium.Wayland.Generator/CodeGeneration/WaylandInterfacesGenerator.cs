using System.Text;
using QuantumBinding.Generator;
using QuantumBinding.Generator.AST;

namespace Adamantium.Wayland.Generator.CodeGeneration;

/// <summary>Writes WaylandInterfaces.cs: the wl_interface of every interface - names, versions and the signatures
/// libwayland marshals requests and reads events by.</summary>
public sealed class WaylandInterfacesGenerator : WaylandCodeGenerator
{
    public const string FileName = "WaylandInterfaces.cs";

    public WaylandInterfacesGenerator(ProcessingContext context, TranslationUnit[] units, IReadOnlyList<Protocol> protocols)
        : base(context, units, protocols)
    {
    }

    public override string GetFileName(TranslationUnit unit)
    {
        return FileName;
    }

    public override void Run()
    {
        Header(null, null);
        WriteLine($"namespace {Namespace};");
        NewLine();
        WriteLine("/// <summary>The wl_interface of every interface of the generated protocols: names, versions and the");
        WriteLine("/// signatures libwayland marshals requests and reads events by.</summary>");
        WriteLine("public static unsafe partial class WaylandInterfaces");
        WriteOpenBraceAndIndent();

        var all = Protocols.SelectMany(p => p.Interfaces).ToList();
        foreach (var i in all)
        {
            WriteLine($"public static readonly WlInterface* {Naming.Pascal(i.Name)} = WaylandInterfaceBuilder.Allocate();");
        }

        NewLine();
        WriteLine("static WaylandInterfaces()");
        WriteOpenBraceAndIndent();
        foreach (var i in all)
        {
            WriteLine($"WaylandInterfaceBuilder.Fill({Naming.Pascal(i.Name)}, \"{i.Name}\", {i.Version},");
            PushIndent();
            Messages(i.Requests, ",");
            Messages(i.Events, ");");
            PopIndent();
        }

        UnindentAndWriteCloseBrace();
        UnindentAndWriteCloseBrace();
    }

    private void Messages(IReadOnlyList<Message> messages, string end)
    {
        if (messages.Count == 0)
        {
            WriteLine("[]" + end);
            return;
        }

        WriteLine("[");
        PushIndent();
        foreach (var m in messages)
        {
            List<string> types = [];
            foreach (var a in m.Arguments)
            {
                if (a.Type == ArgumentType.NewId && a.Interface == null)
                {
                    types.AddRange(["null", "null", "null"]);
                }
                else if (a.Type is ArgumentType.Object or ArgumentType.NewId && a.Interface != null)
                {
                    RequireInterface(a.Interface);
                    types.Add(Naming.Pascal(a.Interface));
                }
                else
                {
                    types.Add("null");
                }
            }

            WriteLine($"new(\"{m.Name}\", \"{Signature(m)}\", [{string.Join(", ", types)}]),");
        }

        PopIndent();
        WriteLine("]" + end);
    }

    private static string Signature(Message m)
    {
        var builder = new StringBuilder();
        if (m.Since > 1)
        {
            builder.Append(m.Since);
        }

        foreach (var a in m.Arguments)
        {
            if (a.AllowNull)
            {
                builder.Append('?');
            }

            builder.Append(a.Type switch
            {
                ArgumentType.Int => "i",
                ArgumentType.Uint => "u",
                ArgumentType.Fixed => "f",
                ArgumentType.String => "s",
                ArgumentType.Object => "o",
                ArgumentType.NewId => a.Interface == null ? "sun" : "n",
                ArgumentType.Array => "a",
                ArgumentType.Fd => "h",
                _ => throw new ArgumentOutOfRangeException(),
            });
        }

        return builder.ToString();
    }
}
