using System;

namespace Gemini.Editor.Attributes
{
    /// <summary>
    /// Geminiに公開するツール関数であることを示します
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class ToolFunctionAttribute : Attribute
    {
        public string Description { get; }

        public ToolFunctionAttribute(string description)
        {
            Description = description;
        }
    }

    /// <summary>
    /// ツール関数のパラメータに関する情報を提供します
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class ToolParameterAttribute : Attribute
    {
        public string Description { get; }

        public ToolParameterAttribute(string description)
        {
            Description = description;
        }
    }
}