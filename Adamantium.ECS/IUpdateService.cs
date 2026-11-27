using Adamantium.Core;
using Adamantium.Graphics.Core;

namespace Adamantium.ECS;

public interface IUpdateService : IContentable
{
    /// <summary>
    /// This method is called when this application component is updated.
    /// </summary>
    /// <param name="appTime">The current timing.</param>
    void Update(AppTime appTime);

    /// <summary>
    /// Gets a value indicating whether <see cref="Update"/> should be called.
    /// </summary>
    /// <value><c>true</c> if update is enabled; otherwise, <c>false</c>.</value>
    bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the way how this system will be processed in Update phase
    /// </summary>
    ExecutionType UpdateExecutionType { get; set; }
}