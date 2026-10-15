using Adamantium.MVVM;

namespace Adamantium.MVVM.Tests;

/// <summary>[Bindable] on a partial property (instead of a field): the generator fills in the implementation using
/// the <c>field</c> keyword — no dangling backing field — and still emits the change hook.</summary>
public partial class PartialPropertyViewModel : AdamantiumViewModel
{
    [Bindable] public partial string Title { get; set; }

    [Bindable, Affects(nameof(Caption))] public partial string Subtitle { get; set; }

    public string Caption => Subtitle;

    public string LastChanged;

    partial void OnTitleChanged(string value) => LastChanged = value;
}
