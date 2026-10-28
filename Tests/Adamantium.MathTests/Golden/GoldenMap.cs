using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Adamantium.MathTests.Golden;

/// <summary>
/// Which call of the new types answers each recorded operation. An operation taking arguments by reference needs no
/// entry of its own: it is answered by the one taking them by value - with the arguments left as they were, or with
/// the last one, written by the call, holding the answer.
/// </summary>
public sealed class GoldenMap
{
    private readonly Dictionary<string, List<Func<double[], double[]>>> calls = [];
    private readonly Dictionary<string, string> reasons = [];
    private readonly List<(Func<string, bool> Applies, string Reason)> reasonRules = [];
    private readonly List<(Func<string, bool> Applies, string Reason)> answersWhereThrew = [];
    private readonly List<(Func<string, bool> Applies, Func<double[], bool> Changed, string Reason)> changedCases = [];
    private readonly List<(Func<string, bool> Applies, double Tolerance, string Reason)> tolerances = [];

    /// <summary>Answers <paramref name="key"/> with what <paramref name="call"/> returns; a static operation.</summary>
    public void Returns(string key, Func<GoldenInputs, object> call) => Calls(key, inputs => [call(inputs)]);

    /// <summary>Answers <paramref name="key"/> with the values <paramref name="call"/> lists, in the order recorded:
    /// the result, then each argument by reference, then the instance.</summary>
    public void Calls(string key, Func<GoldenInputs, object[]> call)
    {
        if (!calls.TryGetValue(key, out var list))
        {
            list = [];
            calls.Add(key, list);
        }

        list.Add(numbers => Flatten(call(new GoldenInputs(numbers))));
    }

    /// <summary>Records why <paramref name="key"/> is not checked.</summary>
    public void NotMapped(string key, string reason) => reasons.Add(key, reason);

    /// <summary>Records why every key <paramref name="applies"/> to is not checked.</summary>
    public void NotMapped(Func<string, bool> applies, string reason) => reasonRules.Add((applies, reason));

    /// <summary>Records that every key <paramref name="applies"/> to now answers where the old one threw, and
    /// why.</summary>
    public void AnswersWhereOldThrew(Func<string, bool> applies, string reason) => answersWhereThrew.Add((applies, reason));

    /// <summary>Records that every key <paramref name="applies"/> to answers differently from the old one, and why, in
    /// the cases <paramref name="changed"/> picks out by their recorded inputs; its other cases are still checked.</summary>
    public void ChangedWhere(Func<string, bool> applies, Func<double[], bool> changed, string reason) =>
        changedCases.Add((applies, changed, reason));

    /// <summary>Whether the answer to <paramref name="key"/> on <paramref name="inputs"/> is known to differ from the old
    /// one.</summary>
    public bool IsChanged(string key, double[] inputs) => changedCases.Exists(rule => rule.Applies(key) && rule.Changed(inputs));

    /// <summary>Records that every key <paramref name="applies"/> to is checked within <paramref name="tolerance"/> of the
    /// answer's size instead of the usual, and why: an operation whose float answer neither the old nor the new can give
    /// closer.</summary>
    public void Tolerance(Func<string, bool> applies, double tolerance, string reason) => tolerances.Add((applies, tolerance, reason));

    /// <summary>The tolerance recorded for <paramref name="key"/>, or null for the usual one.</summary>
    public double? ToleranceFor(string key) => tolerances.Find(rule => rule.Applies(key)) is { Reason: not null } rule ? rule.Tolerance : null;

    /// <summary>Whether <paramref name="key"/> may answer where the old one threw.</summary>
    public bool MayAnswerWhereOldThrew(string key) => answersWhereThrew.Exists(rule => rule.Applies(key));

    /// <summary>Why <paramref name="key"/> is not checked, or null when it is.</summary>
    public string ReasonNotMapped(string key)
    {
        if (reasons.TryGetValue(key, out var reason))
        {
            return reason;
        }

        foreach (var rule in reasonRules)
        {
            if (rule.Applies(key))
            {
                return rule.Reason;
            }
        }

        return null;
    }

    /// <summary>The calls that may answer <paramref name="key"/> - several for a conversion recorded once per target
    /// type - each taking the recorded inputs and giving the answer; empty when the key is not mapped.</summary>
    public IReadOnlyList<Func<double[], double[]>> Resolve(string key)
    {
        if (calls.TryGetValue(key, out var direct))
        {
            return direct;
        }

        if (!key.Contains('&'))
        {
            return [];
        }

        var signature = GoldenSignature.Parse(key);
        if (!signature.IsSized)
        {
            return [];
        }

        var byValue = signature.ByValue();
        if (calls.TryGetValue(byValue.ToString(), out var sameArguments))
        {
            return sameArguments.Select(call => KeepingReferences(signature, call)).ToList();
        }

        if (!signature.IsInstance && signature.Parameters[^1].EndsWith('&')
            && calls.TryGetValue(byValue.WithoutLast().ToString(), out var writingLast))
        {
            return writingLast.Select(call => WritingLast(signature, call)).ToList();
        }

        return [];
    }

    private static Func<double[], double[]> KeepingReferences(GoldenSignature signature, Func<double[], double[]> call) =>
        inputs =>
        {
            var answer = call(inputs);
            var resultSize = answer.Length - signature.InstanceSize;
            return answer.Take(resultSize).Concat(signature.ReferenceInputs(inputs, signature.Parameters.Count))
                .Concat(answer.Skip(resultSize)).ToArray();
        };

    private static Func<double[], double[]> WritingLast(GoldenSignature signature, Func<double[], double[]> call) =>
        inputs => signature.ReferenceInputs(inputs, signature.Parameters.Count - 1).Concat(call(inputs)).ToArray();

    private static double[] Flatten(object[] values)
    {
        var numbers = new List<double>();
        foreach (var value in values)
        {
            Flatten(value, numbers);
        }

        return numbers.ToArray();
    }

    private static void Flatten(object value, List<double> into)
    {
        switch (value)
        {
            case bool b:
                into.Add(b ? 1 : 0);
                return;
            case float f:
                into.Add(f);
                return;
            case double d:
                into.Add(d);
                return;
            case int i:
                into.Add(i);
                return;
        }

        foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Flatten(field.GetValue(value), into);
        }
    }
}
