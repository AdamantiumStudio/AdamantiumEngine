namespace Adamantium.Localization;

/// <summary>The forms a phrase that changes with a number can take, as Unicode CLDR names them. A language uses some of
/// them (English One and Other, Russian One, Few, Many and Other); <see cref="PluralRules"/> says which, and which one a
/// number takes.</summary>
public enum PluralForm
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other
}
