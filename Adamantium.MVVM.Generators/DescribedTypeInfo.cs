namespace Adamantium.MVVM.Generators;

internal sealed record DescribedTypeInfo(string Type, string Namespace, string Name,
    EquatableArray<DescribedPropertyInfo> Properties);
