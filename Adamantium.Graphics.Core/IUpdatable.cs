using Adamantium.Core;

namespace Adamantium.Graphics.Core;

/// <summary>
/// An interface for a component that is called to update a frame.
/// </summary>
public interface IUpdatable
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
   /// Gets the update order relative to other components. Lower values are updated first.
   /// </summary>
   /// <value>The update order.</value>
   /// <remarks>This property is valid on if <see cref="ExecutionType"/> is <see cref="ExecutionType.Sync"/>. Otherwise priority will bw ignored</remarks>
   int UpdatePriority { get; set; }

   /// <summary>
   /// Gets or sets the way how this system will be processed in Update phase
   /// </summary>
   ExecutionType UpdateExecutionType { get; set; }
}