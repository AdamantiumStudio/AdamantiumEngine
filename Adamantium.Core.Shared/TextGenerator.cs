using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Adamantium.Core.Shared
{
    public class TextGenerator
    {
        public TextGenerator()
        {
            indentationStack = new Stack<uint>();
            stringBuilder = new StringBuilder();
        }

        public const uint DefaultIndentation = 4;

        public Stack<uint> indentationStack;

        private readonly StringBuilder stringBuilder;
        protected bool StartNewLine;
        protected bool TrimLines;

        public bool HasText => stringBuilder.Length > 0;

        public uint Indent => (uint)indentationStack.Sum(x => x);

        public void Clear()
        {
            stringBuilder.Clear();
        }

        public void Write(string text)
        {
            var lines = text.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            if (string.IsNullOrEmpty(lines.Last()))
            {
                lines = lines.Take(lines.Length - 1).ToArray();
            }
            int lineIndex = 0;
            foreach (var line in lines)
            {
                var currentLine = line;
                if (TrimLines)
                {
                    currentLine = line.TrimStart();
                }

                if (StartNewLine && !string.IsNullOrEmpty(currentLine))
                {
                    stringBuilder.Append(' ', (int)Indent);
                }

                stringBuilder.Append(currentLine);

                if (lineIndex < lines.Length - 1)
                {
                    NewLine();
                }
                else
                {
                    StartNewLine = false;
                }

                lineIndex++;
            }

            TrimLines = false;
        }

        public void WriteLine(string text)
        {
            StartNewLine = true;
            Write(text);
            NewLine();
        }

        public void WriteLineWithTrim(string text)
        {
            TrimLines = true;
            WriteLine(text);
        }

        public void WriteLineWithIndent(string text)
        {
            WriteLine(text);
            PushIndent();
        }

        public void WriteOpenBraceAndIndent()
        {
            WriteLine("{");
            PushIndent();
        }

        public void UnindentAndWriteCloseBrace()
        {
            PopIndent();
            WriteLine("}");
        }

        public void NewLine()
        {
            stringBuilder.Append(Environment.NewLine);
            StartNewLine = true;
        }

        public void PushIndent(uint indentation = DefaultIndentation)
        {
            indentationStack.Push(indentation);
        }

        public void PopIndent()
        {
            indentationStack.Pop();
        }

        public override string ToString()
        {
            return stringBuilder.ToString();
        }

        public static implicit operator string(TextGenerator text)
        {
            return text.ToString();
        }
    }
}