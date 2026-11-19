using Adamantium.MVVM;

namespace Adamantium.MVVM.Tests;

/// <summary>[Bindable] on a partial property (instead of a field): the generator fills in the implementation using
/// the <c>field</c> keyword — no dangling backing field — and still emits the change hook.</summary>
public partial class PartialPropertyViewModel : AdamantiumViewModel
{
    [Bindable] public partial string Title { get; set; }

    [Bindable, Affects(nameof(Caption))] public partial string Subtitle { get; set; }

    public string Caption => Subtitle;

    [Bindable] public partial int Count { get; private set; }

    [Bindable] public partial string Kind { get; protected set; }

    [Bindable] internal partial string Note { get; set; }

    [Bindable] public virtual partial string Label { get; set; }

    [Bindable] public partial string Origin { get; init; }

    public void CountUp() => Count++;

    public void Classify(string kind) => Kind = kind;

    public string LastChanged;

    partial void OnTitleChanged(string value) => LastChanged = value;
}
