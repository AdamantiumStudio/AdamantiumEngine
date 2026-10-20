namespace Adamantium.MVVM.Generators;

internal sealed record DescribedPropertyInfo(string Name, string Type, bool CanGet, bool CanSet, bool IsInit);
