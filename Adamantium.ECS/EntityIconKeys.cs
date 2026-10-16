namespace Adamantium.ECS;

/// <summary>The resource keys of the icons an editor shows for entities, set into <see cref="Entity.IconKey"/> by
/// whatever makes an entity of that kind. One place for them, so a key is never typed twice.</summary>
public static class EntityIconKeys
{
    public const string Entity = "EntityIcon.Entity";
    public const string Model = "EntityIcon.Model";
    public const string Mesh = "EntityIcon.Mesh";
    public const string Camera = "EntityIcon.Camera";
    public const string PointLight = "EntityIcon.PointLight";
    public const string SpotLight = "EntityIcon.SpotLight";
    public const string DirectionalLight = "EntityIcon.DirectionalLight";
}
