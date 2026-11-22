using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Adamantium.Localization.Generators;

internal static class LanguageTableEmitter
{
    private const string Localization = "global::Adamantium.Localization.";
    private const string TableBase = "Adamantium.Localization.LocalizedStrings";
    private const string TableAttribute = "Adamantium.Localization.LanguageTableAttribute";
    private const string PluralRulesType = "global::Adamantium.Localization.PluralRules";
    private const string PluralFormType = "global::Adamantium.Localization.PluralForm";
    private const string PhraseCasesType = "global::Adamantium.Localization.PhraseCases";

    private static readonly string[] Reserved =
    [
        "Current", "Localized", "TryText", "Equals", "GetHashCode", "ToString", "GetType", "MemberwiseClone", "Finalize",
        "ReferenceEquals",
    ];

    public static void Emit(SourceProductionContext output, ImmutableArray<LanguageFile> files, Compilation compilation,
        LanguageSettings settings)
    {
        if (files.IsDefaultOrEmpty)
        {
            return;
        }

        if (string.IsNullOrEmpty(settings.RootNamespace))
        {
            output.ReportDiagnostic(Create("AUL000",
                LanguageMessages.NoRootNamespace(), Location.None, true));
            return;
        }

        foreach (var file in files)
        {
            foreach (var problem in file.Problems)
            {
                output.ReportDiagnostic(Create(problem.Id, problem.Message, At(file.Path, problem.Line, problem.Column), problem.IsError));
            }
        }

        var usable = files.Where(f => f.Table != null && f.Language != null && f.Problems.All(p => !p.IsError)).ToList();
        var declared = new List<string>();
        var registration = new List<string>();

        var groups = usable.GroupBy(f => (Folder: f.Folder.ToLowerInvariant(), f.Table));
        foreach (var group in groups)
        {
            var languages = new List<LanguageFile>();
            foreach (var file in group.OrderBy(f => f.Language, StringComparer.OrdinalIgnoreCase))
            {
                if (languages.Any(l => string.Equals(l.Language, file.Language, StringComparison.OrdinalIgnoreCase)))
                {
                    output.ReportDiagnostic(Create("AUL001", LanguageMessages.LanguageInAnotherFile(file.Language, file.Table),At(file.Path, 1, 1), true));
                    continue;
                }

                languages.Add(file);
            }

            var baseFile = languages.FirstOrDefault(f => string.Equals(f.Language, settings.NeutralLanguage, StringComparison.OrdinalIgnoreCase));
            if (baseFile != null)
            {
                EmitTable(output, settings, baseFile, languages.Where(f => f != baseFile).ToList(), declared);
            }
            else
            {
                EmitTranslations(output, compilation, settings, languages, registration, declared);
            }
        }

        EmitFormats(output, settings, usable, registration);

        if (declared.Count > 0 || registration.Count > 0)
        {
            EmitRegistration(output, settings, declared, registration);
        }
    }

    private static void EmitTable(SourceProductionContext output, LanguageSettings settings, LanguageFile baseFile,
        List<LanguageFile> translations, List<string> declared)
    {
        var table = baseFile.Table;
        var classes = new[] { baseFile }.Concat(translations).Select(f => ClassOf(f.Language)).ToList();
        var keys = new List<LanguageEntry>();
        var orders = new List<List<string>>();
        foreach (var entry in baseFile.Entries)
        {
            if (Reserved.Contains(entry.Key) || entry.Key == table || classes.Contains(entry.Key) ||
                entry.Key.StartsWith("__", StringComparison.Ordinal))
            {
                output.ReportDiagnostic(Create("AUL003", LanguageMessages.KeyIsTableMember(entry.Key, table),At(baseFile.Path, entry.Line, entry.Column), true));
                continue;
            }

            if (!Placeholders.TryRead(entry, out var names, out var error))
            {
                output.ReportDiagnostic(Create("AUL006", $"{entry.Key}: {error}", At(baseFile.Path, entry.Line, entry.Column), true));
                continue;
            }

            if (!CanChoose(output, baseFile.Path, entry))
            {
                continue;
            }

            keys.Add(entry);
            orders.Add(names);
        }

        // Each language's strings by key, as stored: the base has every key, a translation the ones it gives.
        var spoken = new List<(LanguageFile File, Dictionary<string, Said> Strings)>
        {
            (baseFile, keys.Select((k, i) => (k.Key, Said: Said.Of(k, orders[i]))).ToDictionary(s => s.Key, s => s.Said)),
        };

        // The placeholder whose value chooses each string's case, in whichever language it has cases - one for all of them.
        var choosers = keys.Where(k => k.HasCases).ToDictionary(k => k.Key, k => k.Chooser, StringComparer.Ordinal);

        foreach (var translation in translations)
        {
            var strings = new Dictionary<string, Said>(StringComparer.Ordinal);
            var given = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in translation.Entries.Where(IsTranslated))
            {
                var index = keys.FindIndex(k => k.Key == entry.Key);
                if (index < 0)
                {
                    output.ReportDiagnostic(Create("AUL005", LanguageMessages.KeyNotInBase(entry.Key, $"{table}.{baseFile.Language}{LanguageFileParser.Extension}"),At(translation.Path, entry.Line, entry.Column), true));
                    continue;
                }

                if (!SamePlaceholders(output, translation.Path, entry, orders[index]) || !CanChoose(output, translation.Path, entry))
                {
                    continue;
                }

                if (entry.HasCases && choosers.TryGetValue(entry.Key, out var chooser) && chooser != entry.Chooser)
                {
                    output.ReportDiagnostic(Create("AUL010", LanguageMessages.ChooserDiffers(entry.Key, entry.Chooser, chooser),At(translation.Path, entry.Line, entry.Column), true));
                    continue;
                }

                if (entry.HasCases)
                {
                    choosers[entry.Key] = entry.Chooser;
                    ReportLackingCases(output, translation.Path, entry, keys[index]);
                }

                strings[entry.Key] = Said.Of(entry, orders[index]);
                given.Add(entry.Key);
            }

            ReportMissing(output, translation, keys.Select(k => k.Key).Where(k => !given.Contains(k)).ToList(), table, baseFile.Language);
            spoken.Add((translation, strings));
        }

        foreach (var (file, _) in spoken)
        {
            Declare(declared, file.Language);
        }

        var @namespace = LanguageFileParser.TableNamespace(baseFile.Folder, settings.RootNamespace);
        var languages = string.Join(", ", spoken.Select(s => Literal(s.File.Language)));

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable disable");
        source.AppendLine($"namespace {@namespace};");
        source.AppendLine();
        source.AppendLine($"/// <summary>The {table} strings in the application's language.</summary>");
        source.AppendLine($"[{Localization}LanguageTable({languages})]");
        source.AppendLine($"public sealed partial class {table} : {Localization}LocalizedStrings, {Localization}ILanguageTable");
        source.AppendLine("{");
        source.AppendLine($"    private {table}()");
        source.AppendLine("    {");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    /// <summary>The table itself, what <c>{{Localize}}</c> binds to: it tells its bindings when the language changes.</summary>");
        source.AppendLine($"    public static {table} Current {{ get; }} = new();");

        for (var i = 0; i < keys.Count; i++)
        {
            source.AppendLine();
            source.AppendLine($"    /// <summary>{DocText(keys[i].Value)}</summary>");
            if (orders[i].Count == 0)
            {
                source.AppendLine($"    public static string {keys[i].Key} => Current.Localized(nameof({keys[i].Key}));");
            }
            else
            {
                var parameters = string.Join(", ", orders[i].Select(n => "object " + Placeholders.ParameterName(n)));
                var arguments = string.Join(", ", orders[i].Select(Placeholders.ParameterName));
                source.AppendLine($"    public static string {keys[i].Key}({parameters}) => Current.Localized(nameof({keys[i].Key}), {arguments});");
            }
        }

        // The table read by key: explicit, so none of these names is taken from the keys.
        const string list = "global::System.Collections.Generic.IReadOnlyList<string>";
        source.AppendLine();
        source.AppendLine($"    {list} {Localization}ILanguageTable.Languages {{ get; }} = [{languages}];");
        source.AppendLine();
        source.AppendLine($"    {list} {Localization}ILanguageTable.Keys {{ get; }} = [{string.Join(", ", keys.Select(k => $"nameof({k.Key})"))}];");
        source.AppendLine();

        var placed = keys.Select((k, i) => (k.Key, Names: orders[i])).Where(p => p.Names.Count > 0).ToList();
        if (placed.Count == 0)
        {
            source.AppendLine($"    {list} {Localization}ILanguageTable.PlaceholdersOf(string key) => [];");
        }
        else
        {
            source.AppendLine($"    {list} {Localization}ILanguageTable.PlaceholdersOf(string key) => key switch");
            source.AppendLine("    {");
            foreach (var (key, names) in placed)
            {
                source.AppendLine($"        nameof({key}) => [{string.Join(", ", names.Select(Literal))}],");
            }

            source.AppendLine("        _ => [],");
            source.AppendLine("    };");
        }

        source.AppendLine();
        if (choosers.Count == 0)
        {
            source.AppendLine($"    string {Localization}ILanguageTable.ChooserOf(string key) => null;");
        }
        else
        {
            source.AppendLine($"    string {Localization}ILanguageTable.ChooserOf(string key) => key switch");
            source.AppendLine("    {");
            foreach (var key in keys.Select(k => k.Key).Where(choosers.ContainsKey))
            {
                source.AppendLine($"        nameof({key}) => {Literal(choosers[key])},");
            }

            source.AppendLine("        _ => null,");
            source.AppendLine("    };");
        }

        // The one place a language is chosen: each string from the class of its language, the base language's for one a
        // translation does not give - and for one with cases, the case its value takes in that language.
        source.AppendLine();
        source.AppendLine($"    string {Localization}ILanguageTable.Find(string key, string language, object choice) => key switch");
        source.AppendLine("    {");
        var baseSaid = spoken[0].Strings;
        foreach (var key in keys.Select(k => k.Key))
        {
            var given = spoken.Skip(1).Where(s => s.Strings.ContainsKey(key)).ToList();
            if (given.Count == 0)
            {
                source.AppendLine($"        nameof({key}) => {Choice(baseFile.Language, key, baseSaid[key], "        ")},");
                continue;
            }

            source.AppendLine($"        nameof({key}) => language switch");
            source.AppendLine("        {");
            foreach (var (file, strings) in given)
            {
                source.AppendLine($"            {Literal(file.Language)} => {Choice(file.Language, key, strings[key], "            ")},");
            }

            source.AppendLine($"            _ => {Choice(baseFile.Language, key, baseSaid[key], "            ")},");
            source.AppendLine("        },");
        }

        source.AppendLine("        _ => null,");
        source.AppendLine("    };");
        source.AppendLine("}");
        output.AddSource($"{@namespace}.{table}.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));

        foreach (var (file, strings) in spoken.Where(s => s.Strings.Count > 0))
        {
            EmitLanguage(output, @namespace, table, file, keys.Select(k => k.Key), strings, file == baseFile);
        }
    }

    // The strings of one language: a class of constants beside the table, in a file of its own, in the base's order. A
    // string with cases is a class of them.
    private static void EmitLanguage(SourceProductionContext output, string @namespace, string table, LanguageFile file,
        IEnumerable<string> order, Dictionary<string, Said> strings, bool isBase)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable disable");
        source.AppendLine($"namespace {@namespace};");
        source.AppendLine();
        source.AppendLine($"partial class {table}");
        source.AppendLine("{");
        source.AppendLine($"    /// <summary>The {table} strings in {NameOf(file.Language)}{(isBase ? ", the base language" : string.Empty)}.</summary>");
        source.AppendLine($"    private static class {ClassOf(file.Language)}");
        source.AppendLine("    {");
        foreach (var key in order.Where(strings.ContainsKey))
        {
            var said = strings[key];
            if (said.Cases == null)
            {
                source.AppendLine($"        public const string {key} = {Literal(said.Text)};");
                continue;
            }

            source.AppendLine($"        public static class {key}");
            source.AppendLine("        {");
            foreach (var (name, text) in said.Cases)
            {
                source.AppendLine($"            public const string {name} = {Literal(text)};");
            }

            source.AppendLine("        }");
        }

        source.AppendLine("    }");
        source.AppendLine("}");
        output.AddSource($"{@namespace}.{table}.{file.Language}.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    // How Find reaches a string of one language: the constant, or the case the value takes - for a number the form that
    // language's rules pick, for any other value the case named after it. Other for the rest, else the first case.
    private static string Choice(string language, string key, Said said, string indent)
    {
        var at = $"{ClassOf(language)}.{key}";
        if (said.Cases == null)
        {
            return at;
        }

        var choice = new StringBuilder();
        choice.AppendLine(said.ByNumber
            ? $"{PluralRulesType}.FormOf({Literal(language)}, choice) switch"
            : $"{PhraseCasesType}.Of(choice) switch");
        choice.AppendLine($"{indent}{{");
        foreach (var (name, _) in said.Cases.Where(c => c.Case != LanguageFileParser.OtherCase))
        {
            choice.AppendLine(said.ByNumber
                ? $"{indent}    {PluralFormType}.{name} => {at}.{name},"
                : $"{indent}    {Literal(name)} => {at}.{name},");
        }

        var rest = said.Cases.Any(c => c.Case == LanguageFileParser.OtherCase) ? LanguageFileParser.OtherCase : said.Cases[0].Case;
        choice.AppendLine($"{indent}    _ => {at}.{rest},");
        choice.Append($"{indent}}}");
        return choice.ToString();
    }

    // A phrase with cases becomes a class of them, and a member cannot be named as its class is.
    private static bool CanChoose(SourceProductionContext output, string path, LanguageEntry entry)
    {
        if (!entry.HasCases || entry.Cases.All(c => c.Case != entry.Key))
        {
            return true;
        }

        output.ReportDiagnostic(Create("AUL003", LanguageMessages.KeyIsCaseName(entry.Key),At(path, entry.Line, entry.Column), true));
        return false;
    }

    // A translation's cases, against the base's: a case a value has a text for in the base and not here is said as Other.
    private static void ReportLackingCases(SourceProductionContext output, string path, LanguageEntry entry, LanguageEntry baseEntry)
    {
        if (entry.ByNumber || !baseEntry.HasCases || baseEntry.ByNumber)
        {
            return;
        }

        var lacking = baseEntry.Cases.Select(c => c.Case).Where(name => entry.Cases.All(c => c.Case != name)).ToList();
        if (lacking.Count > 0)
        {
            output.ReportDiagnostic(Create("AUL010", LanguageMessages.LacksCases(entry.Key, lacking),At(path, entry.Line, entry.Column), false));
        }
    }

    // One language's text of a string, as stored: placeholders as indexes - one text, or one per case.
    private sealed class Said
    {
        public string Text { get; private set; }

        public IReadOnlyList<(string Case, string Text)> Cases { get; private set; }

        public bool ByNumber { get; private set; }

        public static Said Of(LanguageEntry entry, List<string> order) => entry.HasCases
            ? new Said { Cases = entry.Cases.Select(c => (c.Case, Stored(c.Text, order))).ToList(), ByNumber = entry.ByNumber }
            : new Said { Text = Stored(entry.Value, order) };
    }

    // "en" -> En, "pt-BR" -> PtBr.
    private static string ClassOf(string language) => string.Concat(language
        .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant()));

    private static string NameOf(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return language;
        }
    }

    private static void EmitTranslations(SourceProductionContext output, Compilation compilation, LanguageSettings settings,
        List<LanguageFile> files, List<string> registration, List<string> declared)
    {
        var table = files[0].Table;
        var candidates = FindTables(compilation, table);
        if (candidates.Count != 1)
        {
            var message = candidates.Count == 0
                ? LanguageMessages.TableNotFound(table, $"{table}.{settings.NeutralLanguage}{LanguageFileParser.Extension}",
                    settings.NeutralLanguage)
                : LanguageMessages.TableAmbiguous(table, string.Join(", ", candidates.Select(c => c.ToDisplayString())));
            foreach (var file in files)
            {
                output.ReportDiagnostic(Create("AUL008", message, At(file.Path, 1, 1), true));
            }

            return;
        }

        var type = candidates[0];
        var tableLanguages = type.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == TableAttribute)
            .SelectMany(a => a.ConstructorArguments.SelectMany(c => c.Kind == TypedConstantKind.Array ? c.Values : [c]))
            .Select(v => v.Value as string)
            .Where(v => v != null)
            .ToList();
        var baseLanguage = tableLanguages.FirstOrDefault() ?? settings.NeutralLanguage;

        var members = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var member in type.GetMembers().Where(m => m.DeclaredAccessibility == Accessibility.Public && m.IsStatic))
        {
            if (member is IPropertySymbol { Type.SpecialType: SpecialType.System_String, Parameters.Length: 0 } property)
            {
                members[property.Name] = [];
            }
            else if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary, ReturnType.SpecialType: SpecialType.System_String } method)
            {
                members[method.Name] = method.Parameters.Select(p => p.Name).ToList();
            }
        }

        foreach (var file in files)
        {
            var strings = new List<string>();
            var given = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in file.Entries.Where(IsTranslated))
            {
                if (!members.TryGetValue(entry.Key, out var order))
                {
                    output.ReportDiagnostic(Create("AUL005", LanguageMessages.TableHasNoKey(type.ToDisplayString(), entry.Key),At(file.Path, entry.Line, entry.Column), true));
                    continue;
                }

                if (!SamePlaceholders(output, file.Path, entry, order))
                {
                    continue;
                }

                if (entry.HasCases)
                {
                    // What chooses the case, under Count or Select as the file says it, then each case.
                    var by = entry.ByNumber ? LanguageFileParser.CountAttribute : LanguageFileParser.SelectAttribute;
                    strings.Add($"[{Literal($"{entry.Key}.{by}")}] = {Literal(entry.Chooser)}");
                    strings.AddRange(entry.Cases.Select(c => $"[{Literal($"{entry.Key}.{c.Case}")}] = {Literal(Stored(c.Text, order))}"));
                }
                else
                {
                    strings.Add($"[{Literal(entry.Key)}] = {Literal(Stored(entry.Value, order))}");
                }

                given.Add(entry.Key);
            }

            if (!tableLanguages.Contains(file.Language, StringComparer.OrdinalIgnoreCase))
            {
                ReportMissing(output, file, members.Keys.Where(k => !given.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList(), table, baseLanguage);
            }

            registration.Add($"{Localization}Languages.AddTranslation(typeof({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), " +
                             $"{Literal(file.Language)}, new global::System.Collections.Generic.Dictionary<string, string> {{ {string.Join(", ", strings)} }});");
            Declare(declared, file.Language);
        }
    }

    private static void EmitFormats(SourceProductionContext output, LanguageSettings settings, List<LanguageFile> files,
        List<string> registration)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(f => f.FormatLine != 0))
        {
            var at = At(file.Path, file.FormatLine, 1);
            if (!settings.IsApplication)
            {
                output.ReportDiagnostic(Create("AUL009", LanguageMessages.FormatsInLibrary(), at, true));
                continue;
            }

            if (!set.Add(file.Language))
            {
                output.ReportDiagnostic(Create("AUL009", LanguageMessages.FormatsTwice(file.Language), at, true));
                continue;
            }

            var values = file.Format.Select(f => f.Name == "FirstDayOfWeek"
                ? $"FirstDayOfWeek = global::System.DayOfWeek.{f.Value}"
                : $"{f.Name} = {Literal(f.Value)}");
            registration.Add($"{Localization}Languages.SetFormat({Literal(file.Language)}, new {Localization}LanguageFormat {{ {string.Join(", ", values)} }});");
        }
    }

    private static void EmitRegistration(SourceProductionContext output, LanguageSettings settings, List<string> declared,
        List<string> registration)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#pragma warning disable CA2255");
        source.AppendLine($"namespace {settings.RootNamespace};");
        source.AppendLine();
        source.AppendLine("internal static class AdamantiumLanguages");
        source.AppendLine("{");
        source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("    internal static void Register()");
        source.AppendLine("    {");
        if (declared.Count > 0)
        {
            source.AppendLine($"        {Localization}Languages.Declare(typeof(AdamantiumLanguages).Assembly, {string.Join(", ", declared.Select(Literal))});");
        }

        foreach (var statement in registration)
        {
            source.AppendLine($"        {statement}");
        }

        source.AppendLine("    }");
        source.AppendLine("}");
        output.AddSource($"{settings.RootNamespace}.AdamantiumLanguages.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static bool SamePlaceholders(SourceProductionContext output, string path, LanguageEntry entry, List<string> order)
    {
        if (!Placeholders.TryRead(entry, out var names, out var error))
        {
            output.ReportDiagnostic(Create("AUL006", $"{entry.Key}: {error}", At(path, entry.Line, entry.Column), true));
            return false;
        }

        var extra = names.Where(n => !order.Contains(n)).ToList();
        var lacking = order.Where(n => !names.Contains(n)).ToList();
        if (extra.Count == 0 && lacking.Count == 0)
        {
            return true;
        }

        var problems = new List<string>();
        if (extra.Count > 0)
        {
            problems.Add(LanguageMessages.PlaceholdersExtra(string.Join(", ", extra.Select(n => "{" + n + "}"))));
        }

        if (lacking.Count > 0)
        {
            problems.Add(LanguageMessages.PlaceholdersLacking(string.Join(", ", lacking.Select(n => "{" + n + "}"))));
        }

        output.ReportDiagnostic(Create("AUL006", LanguageMessages.PlaceholderProblems(entry.Key, problems), At(path, entry.Line, entry.Column), true));
        return false;
    }

    private static void ReportMissing(SourceProductionContext output, LanguageFile file, List<string> missing, string table,
        string baseLanguage)
    {
        if (missing.Count == 0)
        {
            return;
        }

        var shown = string.Join(", ", missing.Take(5));
        if (missing.Count > 5)
        {
            shown = LanguageMessages.AndMore(shown, missing.Count - 5);
        }

        output.ReportDiagnostic(Create("AUL007",
            LanguageMessages.LanguageLacksStrings($"{table}.{file.Language}{LanguageFileParser.Extension}", missing.Count, table, shown,
                baseLanguage),
            At(file.Path, 1, 1), false));
    }

    private static List<INamedTypeSymbol> FindTables(Compilation compilation, string name)
    {
        var result = new List<INamedTypeSymbol>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            var assemblyName = assembly.Identity.Name;
            if (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                assemblyName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                assemblyName is "mscorlib" or "netstandard")
            {
                continue;
            }

            Collect(assembly.GlobalNamespace, name, result);
        }

        return result;
    }

    private static void Collect(INamespaceSymbol @namespace, string name, List<INamedTypeSymbol> result)
    {
        foreach (var type in @namespace.GetTypeMembers(name))
        {
            if (type.DeclaredAccessibility == Accessibility.Public && DerivesFromTable(type))
            {
                result.Add(type);
            }
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            Collect(child, name, result);
        }
    }

    private static bool DerivesFromTable(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() == TableBase)
            {
                return true;
            }
        }

        return false;
    }

    // An empty string of a translation is one not translated yet: it shows the base text and counts as missing.
    private static bool IsTranslated(LanguageEntry entry) =>
        entry.HasCases ? entry.Cases.Count > 0 : !string.IsNullOrWhiteSpace(entry.Value);

    private static void Declare(List<string> declared, string language)
    {
        if (!declared.Contains(language, StringComparer.OrdinalIgnoreCase))
        {
            declared.Add(language);
        }
    }

    private static string Stored(string value, List<string> order) =>
        order.Count == 0 ? Placeholders.Unescape(value) : Placeholders.ToIndexed(value, order);

    private static string Literal(string value) =>
        value == null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

    private static string DocText(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", " ").Replace("\n", " ");

    private static Location At(string path, int line, int column)
    {
        var position = new LinePosition(Math.Max(0, line - 1), Math.Max(0, column - 1));
        return Location.Create(path, new TextSpan(0, 0), new LinePositionSpan(position, position));
    }

    private static Diagnostic Create(string id, string message, Location location, bool isError) =>
        Diagnostic.Create(id, "Localization", message,
            isError ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            isError ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            true, isError ? 0 : 1, location: location);
}
