namespace Adamantium.Core.Shared
{
    /// <summary>
    /// Describes a log message.
    /// </summary>
    public class LogMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LogMessage" /> class.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="text">The text.</param>
        public LogMessage(LogMessageType type, string text)
        {
            Type = type;
            Text = text;
        }

        /// <summary>
        /// Type of message.
        /// </summary>
        public readonly LogMessageType Type;

        /// <summary>
        /// Text of the message.
        /// </summary>
        public readonly string Text;

        /// <inheritdoc/>
        public override string ToString()
        {
            return string.Format("{0} :{1}", Type, Text);
        }
    }

    public class LogMessageRaw : LogMessage
    {
        public LogMessageRaw(LogMessageType type, string text)
           : base(type, text)
        {
        }

        public override string ToString()
        {
            return Text;
        }
    }
}
