using System;
using System.Collections.Generic;

namespace Adamantium.Core.Shared
{
   public class Logger
   {
      /// <summary>
      /// An action to log a message.
      /// </summary>
      /// <param name="logger">The logger.</param>
      /// <param name="message">The message.</param>
      public delegate void LogAction(Logger logger, LogMessage message);

      /// <summary>
      /// Initializes a new instance of the <see cref="Logger" /> class.
      /// </summary>
      public Logger()
      {
         Messages = new List<LogMessage>();
      }

      /// <summary>
      /// List of logged messages.
      /// </summary>
      public readonly List<LogMessage> Messages;

      /// <summary>
      /// Gets a value indicating whether this instance has errors.
      /// </summary>
      /// <value><c>true</c> if this instance has errors; otherwise, <c>false</c>.</value>
      public bool HasErrors { get; set; }

      /// <summary>
      /// Occurs when a new message is logged.
      /// </summary>
      public event LogAction NewMessageLogged;

      /// <summary>
      /// Logs an Error with the specified error message.
      /// </summary>
      /// <param name="errorMessage">The error message.</param>
      public void Error(string errorMessage)
      {
         LogMessage(new LogMessage(LogMessageType.Error, errorMessage));
      }

      /// <summary>
      /// Logs an Error with the specified error message.
      /// </summary>
      /// <param name="errorMessage">The error message.</param>
      /// <param name="parameters">The parameters.</param>
      /// <exception cref="System.ArgumentNullException"></exception>
      public void Error(string errorMessage, params object[] parameters)
      {
         if (parameters == null) throw new ArgumentNullException(nameof(parameters));
         Error(string.Format(errorMessage, parameters));
      }

      /// <summary>
      /// Logs a warning with the specified warning message.
      /// </summary>
      /// <param name="warningMessage">The warning message.</param>
      public void Warning(string warningMessage)
      {
         LogMessage(new LogMessage(LogMessageType.Warning, warningMessage));
      }

      /// <summary>
      /// Logs a warning with the specified warning message.
      /// </summary>
      /// <param name="warningMessage">The warning message.</param>
      /// <param name="parameters">The parameters.</param>
      /// <exception cref="System.ArgumentNullException"></exception>
      public void Warning(string warningMessage, params object[] parameters)
      {
         if (parameters == null) throw new ArgumentNullException(nameof(parameters));
         Warning(string.Format(warningMessage, parameters));
      }

      /// <summary>
      /// Logs a info with the specified info message.
      /// </summary>
      /// <param name="infoMessage">The info message.</param>
      public void Info(string infoMessage)
      {
         LogMessage(new LogMessage(LogMessageType.Info, infoMessage));
      }

      /// <summary>
      /// Logs a warning with the specified info message.
      /// </summary>
      /// <param name="infoMessage">The info message.</param>
      /// <param name="parameters">The parameters.</param>
      /// <exception cref="System.ArgumentNullException"></exception>
      public void Info(string infoMessage, params object[] parameters)
      {
         if (parameters == null) throw new ArgumentNullException(nameof(parameters));
         Info(string.Format(infoMessage, parameters));
      }

      /// <summary>
      /// Logs the message.
      /// </summary>
      /// <param name="message">The message.</param>
      public virtual void LogMessage(LogMessage message)
      {
         if (IsIdentity(message))
         {
            if (message.Type == LogMessageType.Error)
               HasErrors = true;

            Messages.Add(message);

            var handler = NewMessageLogged;
            handler?.Invoke(this, message);
         }
      }

      private bool IsIdentity(LogMessage message)
      {
         foreach (var mess in Messages)
         {
            if (mess.Text == message.Text)
            {
               return false;
            }
         }

         return true;
      }
   }
}
