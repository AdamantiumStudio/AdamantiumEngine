using System;
using System.Runtime.InteropServices;
using Adamantium.MacOS.Foundation;
using Adamantium.MacOS.ObjectiveC;

namespace Adamantium.MacOS.AppKit;

/// <summary>The system's spelling: the dictionaries of the languages it knows and the user's own words.</summary>
public static unsafe class NSSpellChecker
{
    private static readonly IntPtr Class = ObjCFrameworks.GetClass("NSSpellChecker");
    private static readonly IntPtr SharedSelector = ObjCRuntime.RegisterSelector("sharedSpellChecker");
    private static readonly IntPtr LanguagesSelector = ObjCRuntime.RegisterSelector("availableLanguages");
    private static readonly IntPtr NewTagSelector = ObjCRuntime.RegisterSelector("uniqueSpellDocumentTag");
    private static readonly IntPtr CheckSelector = ObjCRuntime.RegisterSelector(
        "checkSpellingOfString:startingAt:language:wrap:inSpellDocumentWithTag:wordCount:");
    private static readonly IntPtr GuessesSelector = ObjCRuntime.RegisterSelector(
        "guessesForWordRange:inString:language:inSpellDocumentWithTag:");
    private static readonly IntPtr CompletionsSelector = ObjCRuntime.RegisterSelector(
        "completionsForPartialWordRange:inString:language:inSpellDocumentWithTag:");
    private static readonly IntPtr IgnoreSelector = ObjCRuntime.RegisterSelector("ignoreWord:inSpellDocumentWithTag:");
    private static readonly IntPtr LearnSelector = ObjCRuntime.RegisterSelector("learnWord:");

    /// <summary>The languages it checks, as the system names them (en_GB).</summary>
    public static string[] Languages => NSArray.ToStrings(NSObject.Send(Shared, LanguagesSelector));

    /// <summary>A number of a document, whose ignored words are its own.</summary>
    public static nint CreateDocument() => NSObject.SendForInteger(Class, NewTagSelector);

    /// <summary>The next misspelled word of <paramref name="text"/> from <paramref name="start"/> on; its length is 0 when
    /// there is none.</summary>
    public static (int Start, int Length) FindMisspelled(string text, int start, string language, nint document)
    {
        var nsText = NSString.Create(text);
        var nsLanguage = NSString.Create(language);
        nint words;
        var found = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint, IntPtr, byte, nint, nint*, Range>)
            ObjCRuntime.MessageSend)(Shared, CheckSelector, nsText, start, nsLanguage, 0, document, &words);
        NSObject.Release(nsText);
        NSObject.Release(nsLanguage);
        return found.Length == 0 || found.Location > (nuint)text.Length ? (0, 0) : ((int)found.Location, (int)found.Length);
    }

    /// <summary>What <paramref name="word"/> may have been meant as, the likeliest first.</summary>
    public static string[] Guess(string word, string language, nint document) =>
        Ask(GuessesSelector, word, language, document);

    /// <summary>The words <paramref name="start"/> may go on to, the likeliest first.</summary>
    public static string[] Complete(string start, string language, nint document) =>
        Ask(CompletionsSelector, start, language, document);

    /// <summary>Takes <paramref name="word"/> as spelled right in a document.</summary>
    public static void Ignore(string word, nint document)
    {
        var nsWord = NSString.Create(word);
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint, void>)ObjCRuntime.MessageSend)(Shared, IgnoreSelector, nsWord,
            document);
        NSObject.Release(nsWord);
    }

    /// <summary>Adds <paramref name="word"/> to the user's dictionary.</summary>
    public static void Learn(string word)
    {
        var nsWord = NSString.Create(word);
        NSObject.Send(Shared, LearnSelector, nsWord);
        NSObject.Release(nsWord);
    }

    private static IntPtr Shared => NSObject.Send(Class, SharedSelector);

    private static string[] Ask(IntPtr selector, string word, string language, nint document)
    {
        var nsWord = NSString.Create(word);
        var nsLanguage = NSString.Create(language);
        var answer = ((delegate* unmanaged<IntPtr, IntPtr, Range, IntPtr, IntPtr, nint, IntPtr>)ObjCRuntime.MessageSend)(
            Shared, selector, new Range(0, (nuint)word.Length), nsWord, nsLanguage, document);
        NSObject.Release(nsWord);
        NSObject.Release(nsLanguage);
        return NSArray.ToStrings(answer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Range(nuint Location, nuint Length);
}
