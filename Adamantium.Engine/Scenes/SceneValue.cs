namespace Adamantium.Engine.Scenes;

/// <summary>A field's value of a <see cref="SceneBehavior"/>, as the scene file writes it.</summary>
public readonly record struct SceneValue(string Name, string Text);
