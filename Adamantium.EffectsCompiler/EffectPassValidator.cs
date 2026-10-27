using System.Collections.Generic;
using System.Linq;
using Adamantium.Core;

namespace Adamantium.EffectsCompiler;

/// <summary>The rules a pass keeps besides the grammar. It names each stage once - misses and callables any number of
/// times - and runs one kind of pipeline, with what that pipeline needs: a graphics pass a vertex shader, and hull and
/// domain shaders together; a mesh pass a mesh shader; a compute pass no pixel shader; a ray-tracing pass one ray
/// generation shader and no pixel shader.</summary>
internal static class EffectPassValidator
{
    /// <summary>Adds what in <paramref name="pass"/> breaks the rules to <paramref name="errors"/>.</summary>
    public static void Validate(EffectPassSource pass, List<EffectCompilerMessage> errors)
    {
        ReportRepeatedStages(pass, errors);
        if (pass.Stages.Count == 0 && pass.HitGroups.Count == 0)
        {
            AddError(errors, pass, "The pass runs no shader.");
            return;
        }

        var kinds = GetPipelineKinds(pass);
        if (kinds.Count > 1)
        {
            var named = string.Join(" and ", kinds.Select(GetDescription));
            AddError(errors, pass, $"A pass runs one kind of pipeline, not {named}.");
            return;
        }

        switch (kinds.Count == 1 ? kinds[0] : EffectPipelineKind.Graphics)
        {
            case EffectPipelineKind.Graphics:
                ValidateGraphics(pass, errors);
                break;
            case EffectPipelineKind.Mesh:
                ValidateMesh(pass, errors);
                break;
            case EffectPipelineKind.Compute:
                ValidateCompute(pass, errors);
                break;
            case EffectPipelineKind.RayTracing:
                ValidateRayTracing(pass, errors);
                break;
        }
    }

    private static void ReportRepeatedStages(EffectPassSource pass, List<EffectCompilerMessage> errors)
    {
        HashSet<EffectStage> named = [];
        HashSet<EffectStage> reported = [];
        foreach (var stage in pass.Stages)
        {
            if (stage.Stage is EffectStage.Miss or EffectStage.Callable)
            {
                continue;
            }

            if (!named.Add(stage.Stage) && reported.Add(stage.Stage))
            {
                errors.Add(new EffectCompilerMessage(LogMessageType.Error,
                    $"The pass names its {stage.Stage} shader twice.", stage.Location));
            }
        }
    }

    private static List<EffectPipelineKind> GetPipelineKinds(EffectPassSource pass)
    {
        List<EffectPipelineKind> kinds = [];
        if (HasAny(pass, EffectStage.Vertex, EffectStage.Hull, EffectStage.Domain))
        {
            kinds.Add(EffectPipelineKind.Graphics);
        }

        if (HasAny(pass, EffectStage.Task, EffectStage.Mesh))
        {
            kinds.Add(EffectPipelineKind.Mesh);
        }

        if (HasAny(pass, EffectStage.Compute))
        {
            kinds.Add(EffectPipelineKind.Compute);
        }

        if (pass.HitGroups.Count > 0 || HasAny(pass, EffectStage.RayGeneration, EffectStage.Miss, EffectStage.Callable))
        {
            kinds.Add(EffectPipelineKind.RayTracing);
        }

        return kinds;
    }

    private static void ValidateGraphics(EffectPassSource pass, List<EffectCompilerMessage> errors)
    {
        if (!HasAny(pass, EffectStage.Vertex))
        {
            AddError(errors, pass, "A graphics pass has a vertex shader.");
        }
        else if (HasAny(pass, EffectStage.Hull) != HasAny(pass, EffectStage.Domain))
        {
            AddError(errors, pass, "Hull and domain shaders go together.");
        }
    }

    private static void ValidateMesh(EffectPassSource pass, List<EffectCompilerMessage> errors)
    {
        if (!HasAny(pass, EffectStage.Mesh))
        {
            AddError(errors, pass, "A mesh pass has a mesh shader.");
        }
    }

    private static void ValidateCompute(EffectPassSource pass, List<EffectCompilerMessage> errors)
    {
        if (HasAny(pass, EffectStage.Pixel))
        {
            AddError(errors, pass, "A compute pass has no pixel shader.");
        }
    }

    private static void ValidateRayTracing(EffectPassSource pass, List<EffectCompilerMessage> errors)
    {
        if (pass.Stages.Count(stage => stage.Stage == EffectStage.RayGeneration) != 1)
        {
            AddError(errors, pass, "A ray-tracing pass has one ray generation shader.");
        }

        if (HasAny(pass, EffectStage.Pixel))
        {
            AddError(errors, pass, "A ray-tracing pass has no pixel shader.");
        }
    }

    private static string GetDescription(EffectPipelineKind kind) => kind switch
    {
        EffectPipelineKind.Graphics => "graphics",
        EffectPipelineKind.Mesh => "mesh",
        EffectPipelineKind.Compute => "compute",
        _ => "ray tracing"
    };

    private static bool HasAny(EffectPassSource pass, params EffectStage[] stages) =>
        pass.Stages.Any(stage => stages.Contains(stage.Stage));

    private static void AddError(List<EffectCompilerMessage> errors, EffectPassSource pass, string text) =>
        errors.Add(new EffectCompilerMessage(LogMessageType.Error, text, pass.Location));
}
