using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unityエディタ上でMarkdown文字列を解析し、EditorGUILayoutを用いて描画する静的クラス。
/// </summary>
/// <remarks>
/// サポートする主な構文:
/// - 見出し (#, ##, ###)
/// - 順序なしリスト (-, *)
/// - 太字 (**, __)
/// - 斜体 (*)
/// - インラインコード (`)
/// - リンク ([text](url))
/// - 水平線 (---)
/// </remarks>
public static class MarkdownRenderer
{
    private static readonly Dictionary<string, GUIStyle> Styles = new Dictionary<string, GUIStyle>();

    // ブロック要素の正規表現
    private static readonly Regex HeaderRegex = new Regex(@"^(#+)\s(.*)");
    private static readonly Regex UnorderedListRegex = new Regex(@"^[-*]\s(.*)");
    private static readonly Regex LinkRegex = new Regex(@"\[([^\]]+)\]\(([^)]+)\)");

    // インライン要素のスタイルをリッチテキストに変換する正規表現 (変更箇所)
    private static readonly Regex InlineCodeRegex = new Regex(@"`([^`]+)`");
    private static readonly Regex BoldRegex = new Regex(@"\*\*(.*?)\*\*");
    private static readonly Regex BoldUnderscoreRegex = new Regex(@"__(.*?)__"); //  __bold__ 対応を追加
    private static readonly Regex ItalicRegex = new Regex(@"\*(.*?)\*");

    /// <summary>
    /// 指定されたMarkdown文字列を現在のGUIに描画します。
    /// </summary>
    /// <param name="markdownText">描画するMarkdown形式の文字列。</param>
    public static void Draw(string markdownText)
    {
        if (string.IsNullOrEmpty(markdownText))
        {
            return;
        }

        var lines = markdownText.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            RenderLine(line);
        }
    }

    private static void RenderLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            EditorGUILayout.Space();
            return;
        }

        if (TryParseHorizontalRule(line)) return;

        var headerMatch = HeaderRegex.Match(line);
        if (headerMatch.Success)
        {
            var level = headerMatch.Groups[1].Value.Length;
            var content = headerMatch.Groups[2].Value;
            RenderHeader(content, level);
            return;
        }

        var listMatch = UnorderedListRegex.Match(line);
        if (listMatch.Success)
        {
            var content = listMatch.Groups[1].Value;
            RenderListItem(content);
            return;
        }

        RenderParagraph(line);
    }

    private static bool TryParseHorizontalRule(string line)
    {
        if (line.Trim() == "---")
        {
            EditorGUILayout.Space(5);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            rect.x = 0;
            rect.width = EditorGUIUtility.currentViewWidth;
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 1));
            EditorGUILayout.Space(5);
            return true;
        }
        return false;
    }

    private static void RenderHeader(string content, int level)
    {
        var style = GetCachedStyle($"Header{level}");
        style.fontStyle = FontStyle.Bold;
        style.richText = true;

        switch (level)
        {
            case 1:
                style.fontSize = 20;
                break;
            case 2:
                style.fontSize = 16;
                break;
            default:
                style.fontSize = 13;
                break;
        }

        EditorGUILayout.LabelField(ApplyInlineFormatting(content), style);
        EditorGUILayout.Space();
    }

    private static void RenderListItem(string content)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(15); // インデント
        EditorGUILayout.LabelField("•", GUILayout.Width(10));
        RenderParagraph(content, useSpace: false);
        EditorGUILayout.EndHorizontal();
    }

    private static void RenderParagraph(string line, bool useSpace = true)
    {
        var style = GetCachedStyle("Paragraph");
        style.wordWrap = true;
        style.richText = true;

        var matches = LinkRegex.Matches(line);

        if (matches.Count == 0)
        {
            EditorGUILayout.LabelField(ApplyInlineFormatting(line), style);
        }
        else
        {
            RenderRichTextWithLinks(line, matches, style);
        }

        if (useSpace)
        {
            EditorGUILayout.Space();
        }
    }

    private static void RenderRichTextWithLinks(string line, MatchCollection matches, GUIStyle style)
    {
        EditorGUILayout.BeginHorizontal();
        var lastIndex = 0;

        foreach (Match match in matches)
        {
            var precedingText = line.Substring(lastIndex, match.Index - lastIndex);
            GUILayout.Label(ApplyInlineFormatting(precedingText), style);

            var linkText = match.Groups[1].Value;
            var url = match.Groups[2].Value;
            RenderLink(linkText, url);

            lastIndex = match.Index + match.Length;
        }

        var remainingText = line.Substring(lastIndex);
        GUILayout.Label(ApplyInlineFormatting(remainingText), style);
        
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private static void RenderLink(string text, string url)
    {
        var style = GetCachedStyle("Link");
        style.normal.textColor = new Color(0.5f, 0.7f, 1f);
        style.hover.textColor = Color.cyan;
        style.richText = true;

        if (GUILayout.Button(ApplyInlineFormatting(text), style))
        {
            Application.OpenURL(url);
        }
    }
    
    /// <summary>
    /// インラインの書式設定をリッチテキストタグに変換します。
    /// </summary>
    /// <param name="text">変換対象の文字列。</param>
    /// <returns>リッチテキストに変換された文字列。</returns>
    private static string ApplyInlineFormatting(string text)
    {
        // 処理順序が重要。より具体的なものから先に処理する。
        // `code` -> <color=#a0c0e0><i>code</i></color> (ライトブルー系のイタリック)
        text = InlineCodeRegex.Replace(text, "<color=#a0c0e0><i>$1</i></color>");
        
        // **bold** -> <b>bold</b>
        text = BoldRegex.Replace(text, "<b>$1</b>");
        
        // __bold__ -> <b>bold</b>
        text = BoldUnderscoreRegex.Replace(text, "<b>$1</b>");
        
        // *italic* -> <i>italic</i> (他の記法と競合しないよう最後に処理)
        text = ItalicRegex.Replace(text, "<i>$1</i>");
        
        return text;
    }

    private static GUIStyle GetCachedStyle(string name)
    {
        if (!Styles.ContainsKey(name))
        {
            Styles[name] = new GUIStyle(EditorStyles.label) {
                padding = new RectOffset(0,0,0,0)
            };
        }
        return Styles[name];
    }
}