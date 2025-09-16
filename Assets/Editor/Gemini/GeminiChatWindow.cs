using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Gemini.Api;
using Gemini.Core;
using Tool = Gemini.Api.Tool;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;

namespace Gemini.Editor
{
    #region Serializable State Objects
    // 対話履歴をファイルに保存するための、シリアライズ可能なデータ構造

    [Serializable]
    public class SerializableVector2
    {
        public float x { get; set; }
        public float y { get; set; }

        public SerializableVector2() {}

        public SerializableVector2(Vector2 v)
        {
            x = v.x;
            y = v.y;
        }

        public Vector2 ToVector2()
        {
            return new Vector2(x, y);
        }
    }

    [Serializable]
    public class SerializableFunctionCall
    {
        public string name { get; set; }
        public string argsJson { get; set; }
    }

    [Serializable]
    public class SerializableFunctionResponse
    {
        public string name { get; set; }
        public string responseJson { get; set; }
    }

    [Serializable]
    public class SerializablePart
    {
        public string text { get; set; }
        public SerializableFunctionCall functionCall { get; set; }
        public SerializableFunctionResponse functionResponse { get; set; }
    }

    [Serializable]
    public class SerializableContent
    {
        public string role { get; set; }
        public List<SerializablePart> parts { get; set; } = new();
    }

    [Serializable]
    public class ChatState
    {
        public List<SerializableContent> history { get; set; } = new();
        public string userInput { get; set; }
        public List<SerializableFunctionCall> remainFunctionCalls { get; set; } = new();
        public SerializableVector2 scrollPosition { get; set; }
        public bool isProcessing { get; set; }
        public int looped { get; set; }
    }

    #endregion

    public class GeminiChatWindow : EditorWindow
    {
        private const string ApiKeyConfigKey = "GeminiChatWindow_ApiKey";
        private const string ModelConfigKey = "GeminiChatWindow_Model";
        private const string SystemInstructionFileConfigKey = "GeminiChat_InstructionFile";
        private const string MaxResponseLoopConfigKey = "GeminiChatMaxResponseLoop";
        private const string ChatStateTempFileConfigKey = "GeminiChatStateFile";

        private string _apiKey = "";
        private Content _systemInstruction = null;
        private string _chatStateTempFile = "";
        private string _modelName = "gemini-1.5-flash";
        private string _userInput = "";
        private Vector2 _scrollPosition;
        private bool _isProcessing;
        private int _looped;
        private Stack<FunctionCall> _remainFunctionCalls = new();
        private int maxLoop;
        private bool modified;
        private List<Tool> _tools;
        private GenerationConfig generationConfig;

        private GenAI _genAI;
        private GenerativeModel _model;
        private List<Content> _chatHistory = new();

        private GUIStyle _userMessageStyle;
        private GUIStyle _modelMessageStyle;
        private GUIStyle _toolCallStyle;
        private GUIStyle _toolResponseStyle;
        private bool _styleInitialized;

        [MenuItem("Tools/Gemini Chat")]
        public static void ShowWindow()
        {
            GetWindow<GeminiChatWindow>("Gemini Chat");
        }

        #region Initialization and State Management

        private void OnEnable()
        {
            modified = false;
            LoadConfiguration();
            InitializeModels();
            RestoreState();
        }

        private void OnDisable()
        {
            if (modified)
            {
                SaveState();
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                _apiKey = Config.Get(ApiKeyConfigKey);
                try
                {
                    string instructionFilePath = Config.Get(SystemInstructionFileConfigKey);
                    if (!string.IsNullOrWhiteSpace(instructionFilePath))
                    {
                        var fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, instructionFilePath));
                        if (File.Exists(fullPath))
                        {
                            var sysinst = File.ReadAllText(fullPath);
                            _systemInstruction = new Content { Role = "system", Parts = new[] { new Part { Text = sysinst } } };
                            Debug.Log($"Loaded system instruction from: {fullPath}");
                        }
                        else
                        {
                            Debug.LogWarning($"System instruction file not found: {fullPath}");
                        }
                    }
                }
                catch (KeyNotFoundException) { /* Not required, use default */ }
                try
                {
                    maxLoop = Config.Get(MaxResponseLoopConfigKey, v => v.GetInt32());
                }
                catch (KeyNotFoundException) { maxLoop = 5; /* default */ }
                try
                {
                    _chatStateTempFile = Path.Combine(Application.dataPath, Config.Get(ChatStateTempFileConfigKey));
                }
                catch (KeyNotFoundException) { _chatStateTempFile = Path.Combine(Application.dataPath, "Temp/Gemini/chat_state.json"); /* default */ }
                try { _modelName = Config.Get(ModelConfigKey); } catch (KeyNotFoundException) { /* Not required, use default */ }
            }
            catch (Exception ex)
            {
                _apiKey = "";
                Debug.LogError($"Failed to load configuration: {ex.Message}");
            }
        }

        private void InitializeModels()
        {
            if (!string.IsNullOrWhiteSpace(_apiKey))
            {
                _genAI = new GenAI(_apiKey);
                _model = _genAI.GetModel(_modelName);

                generationConfig = new GenerationConfig
                {
                    Temperature = 0.1f,
                };
            }
            else
            {
                _model = null;
            }
        }

        private void SaveState()
        {
            var state = new ChatState
            {
                userInput = _userInput,
                scrollPosition = new SerializableVector2(_scrollPosition),
                history = _chatHistory.Select(content => new SerializableContent
                {
                    role = content.Role,
                    parts = content.Parts?.Select(part => new SerializablePart
                    {
                        text = part.Text,
                        functionCall = part.FunctionCall != null ? new SerializableFunctionCall
                        {
                            name = part.FunctionCall.Name,
                            argsJson = JsonSerializer.Serialize(part.FunctionCall.Args)
                        } : null,
                        functionResponse = part.FunctionResponse != null ? new SerializableFunctionResponse
                        {
                            name = part.FunctionResponse.Name,
                            responseJson = JsonSerializer.Serialize(part.FunctionResponse.Response)
                        } : null
                    }).ToList() ?? new List<SerializablePart>()
                }).ToList(),
                remainFunctionCalls = _remainFunctionCalls.Select(
                    fc => new SerializableFunctionCall
                    {
                        name = fc.Name,
                        argsJson = JsonSerializer.Serialize(fc.Args)
                    }
                ).ToList(),
                isProcessing = _isProcessing,
                looped = _looped
            };

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var jsonState = JsonSerializer.Serialize(state, options);
                var directory = Path.GetDirectoryName(_chatStateTempFile);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(_chatStateTempFile, jsonState);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save chat state: {ex.Message}");
            }
        }

        private void RestoreState()
        {
            if (!File.Exists(_chatStateTempFile)) return;

            try
            {
                var jsonState = File.ReadAllText(_chatStateTempFile);
                if (string.IsNullOrEmpty(jsonState)) return;

                var state = JsonSerializer.Deserialize<ChatState>(jsonState);
                if (state == null)
                {
                     Debug.LogWarning("Failed to deserialize chat state.");
                     return;
                }

                _userInput = state.userInput;
                _scrollPosition = state.scrollPosition?.ToVector2() ?? Vector2.zero;
                _chatHistory = state.history.Select(sc => new Content
                {
                    Role = sc.role,
                    Parts = sc.parts.Select(sp => new Part
                    {
                        Text = sp.text,
                        FunctionCall = sp.functionCall != null ? new FunctionCall
                        {
                            Name = sp.functionCall.name,
                            Args = JsonDocument.Parse(sp.functionCall.argsJson).RootElement
                        } : null,
                        FunctionResponse = sp.functionResponse != null ? new FunctionResponse
                        {
                            Name = sp.functionResponse.name,
                            Response = JsonSerializer.Deserialize<object>(sp.functionResponse.responseJson)
                        } : null
                    }).ToList()
                }).ToList();
                _looped = state.looped;
                _remainFunctionCalls = new Stack<FunctionCall>(state.remainFunctionCalls.Select(fc => new FunctionCall
                {
                    Name = fc.name,
                    Args = JsonDocument.Parse(fc.argsJson).RootElement
                }).ToArray());
                ResolveFunctionCalls();
                if (state.isProcessing)
                {
                    _isProcessing = false;
                    SendMessageAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not restore chat state. It might be corrupted. {ex.Message}");
            }
        }

        private void ClearState()
        {
            _userInput = "";
            _scrollPosition = Vector2.zero;
            _chatHistory.Clear();
            _isProcessing = false;
            _looped = 0;
            _remainFunctionCalls.Clear();
            modified = true;
        }

        #endregion

        #region GUI Drawing

        private void OnGUI()
        {
            if (!IsApiConfigured())
            {
                var helpText = $"API Key not found. Please set '{ApiKeyConfigKey}' in:\nAssets/Settings/Gemini/secrets_gemini_config.json";
                EditorGUILayout.HelpBox(helpText, MessageType.Info);
                return;
            }
            if (!_styleInitialized)
            {
                InitializeStyles();
            }
            DrawGUIButtons();
            DrawChatHistory();
            DrawUserInput();
        }

        private void DrawGUIButtons()
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(30)))
            {
                if (GUILayout.Button("Refresh", GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true)))
                {
                    _looped = maxLoop;
                    _isProcessing = false;
                    ClearState();
                    SaveState();
                }
                using (new EditorGUI.DisabledScope(!(_isProcessing && IsApiConfigured())))
                {
                    if (GUILayout.Button("Stop", GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true)))
                    {
                        _looped = maxLoop;
                        _isProcessing = false;
                    }
                }
            }
        }

        private void DrawChatHistory()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            foreach (var message in _chatHistory)
            {
                switch (message.Role)
                {
                    case "user":
                        DrawMessage("You", message.Parts.FirstOrDefault()?.Text, _userMessageStyle, EditorStyles.boldLabel);
                        break;
                    case "model":
                        DrawModelMessage(message);
                        break;
                    case "tool":
                        DrawToolResponseMessage(message);
                        break;
                }
                EditorGUILayout.Space(10);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawModelMessage(Content message)
        {
            if (message.Parts == null) return;

            foreach (var part in message.Parts)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    DrawMessage("Gemini", part.Text, _modelMessageStyle, EditorStyles.boldLabel);
                    EditorGUILayout.Space(5);
                }

                if (part.FunctionCall != null)
                {
                    var fc = part.FunctionCall;
                    var argsDict = fc.Args.ValueKind == JsonValueKind.Object ? fc.Args.Deserialize<Dictionary<string, object>>() : new Dictionary<string, object>();
                    var argsStr = argsDict.Any()
                        ? string.Join("\n  ", argsDict.Select(kvp => $"{kvp.Key}: {JsonSerializer.Serialize(kvp.Value)}"))
                        : "None";
                    DrawMessage("Tool Call", $"Function: {fc.Name}\nArguments:\n  {argsStr}", _toolCallStyle, EditorStyles.boldLabel);
                    EditorGUILayout.Space(5);
                }
            }
        }
        private void DrawToolResponseMessage(Content message)
        {
            if (message.Parts == null) return;

            foreach (var part in message.Parts)
            {
                if (part.FunctionResponse == null || part.FunctionResponse.Response == null) continue;

                string formattedResponse;
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                try
                {
                    var rootNode = JsonSerializer.SerializeToNode(part.FunctionResponse.Response);
                    if (rootNode != null)
                    {
                        // 再帰関数を呼び出して、ネストされたJSON文字列をすべて展開する
                        var fullyParsedNode = DeepParseJsonStrings(rootNode);
                        formattedResponse = JsonSerializer.Serialize(fullyParsedNode, options).Replace("\\n","\n");
                    }
                    else
                    {
                        formattedResponse = "Response is null or invalid.";
                    }
                }
                catch (Exception ex)
                {
                    formattedResponse = $"[JSON Format Error: {ex.Message}]\n---\n{part.FunctionResponse.Response}";
                }
                DrawMessage($"Tool Response ({part.FunctionResponse.Name})", formattedResponse, _toolResponseStyle, EditorStyles.boldLabel);
                EditorGUILayout.Space(5);
            }
        }

        /// <summary>
        /// JsonNode内のすべての階層を再帰的に探索し、
        /// 値がJSONオブジェクト/配列形式の文字列であればパースして置き換えます。
        /// それ以外の文字列は、標準的なエスケープシーケンスを解除します。
        /// </summary>
        /// <param name="node">探索対象のJsonNode</param>
        /// <returns>変換後のJsonNode</returns>
        private JsonNode DeepParseJsonStrings(JsonNode node)
        {
            // オブジェクトの場合
            if (node is JsonObject jsonObject)
            {
                foreach (var key in jsonObject.Select(kvp => kvp.Key).ToList())
                {
                    var childNode = jsonObject[key];
                    if (childNode != null && childNode.GetValue<JsonElement>().ValueKind == JsonValueKind.String)
                    {
                        string stringValue = childNode.GetValue<string>();
                        string trimmedValue = stringValue.Trim();

                        // 先頭と末尾が {} または [] かどうかをチェック
                        bool isStructuralJson = (trimmedValue.StartsWith("{") && trimmedValue.EndsWith("}")) ||
                                                (trimmedValue.StartsWith("[") && trimmedValue.EndsWith("]"));

                        if (isStructuralJson)
                        {
                            try
                            {
                                // JSONオブジェクト/配列としてパースを試みる
                                jsonObject[key] = JsonNode.Parse(stringValue);
                            }
                            catch (JsonException)
                            {
                                // パースに失敗した場合は、フォールバックしてUnescapeのみ行う
                                jsonObject[key] = JsonValue.Create(Regex.Unescape(stringValue));
                            }
                        }
                        else
                        {
                            // 構造化されたJSONでなければ、Unescapeのみ行う
                            jsonObject[key] = JsonValue.Create(Regex.Unescape(stringValue));
                        }
                    }

                    if (jsonObject[key] != null)
                    {
                        DeepParseJsonStrings(jsonObject[key]);
                    }
                }
            }
            // 配列の場合
            else if (node is JsonArray jsonArray)
            {
                for (int i = 0; i < jsonArray.Count; i++)
                {
                    var elementNode = jsonArray[i];
                    if (elementNode != null && elementNode.GetValue<JsonElement>().ValueKind == JsonValueKind.String)
                    {
                        string stringValue = elementNode.GetValue<string>();
                        string trimmedValue = stringValue.Trim();

                        bool isStructuralJson = (trimmedValue.StartsWith("{") && trimmedValue.EndsWith("}")) ||
                                                (trimmedValue.StartsWith("[") && trimmedValue.EndsWith("]"));
                        
                        if (isStructuralJson)
                        {
                            try
                            {
                                jsonArray[i] = JsonNode.Parse(stringValue);
                            }
                            catch (JsonException)
                            {
                                jsonArray[i] = JsonValue.Create(Regex.Unescape(stringValue));
                            }
                        }
                        else
                        {
                            jsonArray[i] = JsonValue.Create(Regex.Unescape(stringValue));
                        }
                    }
                    
                    if (jsonArray[i] != null)
                    {
                        DeepParseJsonStrings(jsonArray[i]);
                    }
                }
            }

            return node;
        }

        private void DrawMessage(string label, string text, GUIStyle style, GUIStyle labelStyle)
        {
            if (string.IsNullOrEmpty(text)) return;

            EditorGUILayout.LabelField(label, labelStyle);
            //float height = style.CalcHeight(new GUIContent(text), position.width - 20);
            //EditorGUILayout.SelectableLabel(text, style, GUILayout.Height(height));
            MarkdownRenderer.Draw(text);
        }

        private void DrawUserInput()
        {
            if (_isProcessing)
            {
                EditorGUILayout.LabelField("Gemini is thinking...", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(40));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _userInput = EditorGUILayout.TextArea(_userInput, new GUIStyle(EditorStyles.textArea) { wordWrap = true }, GUILayout.ExpandWidth(true), GUILayout.Height(40));

                using (new EditorGUI.DisabledScope(_isProcessing || !IsApiConfigured() || string.IsNullOrWhiteSpace(_userInput)))
                {
                    if (GUILayout.Button("Send", GUILayout.Width(60), GUILayout.Height(40)) || (Event.current.isKey && Event.current.keyCode == KeyCode.Return && Event.current.shift))
                    {
                        var userMessage = _userInput;
                        _userInput = "";
                        _chatHistory.Add(Content.ForUser(userMessage));
                        Repaint();
                        _looped = 0;
                        SendMessageAsync();
                        GUI.FocusControl(null);
                    }
                }
            }
        }
        #endregion

        #region Logic

        private async void SendMessageAsync()
        {
            if (_isProcessing) return;
            _isProcessing = true;
            Repaint();
            _tools ??= new List<Tool> { UnityToolSet.GetToolDefinition() };
            modified = true;
            try
            {
                while (_isProcessing)
                {
                    var request = new GenerateContentRequest
                    {
                        Contents = _chatHistory,
                        GenerationConfig = generationConfig,
                        Tools = _tools,
                        SystemInstruction = _systemInstruction
                    };

                    var response = await _model.GenerateContentAsync(request);
                    Debug.Log(response);
                    _looped += 1;
                    _isProcessing = _looped < maxLoop;

                    var firstCandidate = response.Candidates?.FirstOrDefault();
                    if (firstCandidate?.Content?.Parts == null)
                    {
                        _chatHistory.Add(Content.ForModel("Error: The model did not return a valid response."));
                        break;
                    }

                    _chatHistory.Add(firstCandidate.Content);
                    Repaint();

                    var functionCalls = firstCandidate.Content.Parts
                        .Where(p => p.FunctionCall != null)
                        .Select(p => p.FunctionCall!)
                        .ToList();

                    if (functionCalls.Any())
                    {
                        foreach (var fc in functionCalls)
                        {
                            Debug.Log(fc);
                            _remainFunctionCalls.Push(fc);
                        }
                        ResolveFunctionCalls();
                    }
                    else
                    {
                        var lastPart = firstCandidate.Content.Parts.LastOrDefault();
                        if (lastPart != null)
                        {
                            if (string.IsNullOrEmpty(lastPart.Text))
                            {
                                if (lastPart.Text.IndexOf("[CONTINUE]") != -1)
                                {
                                    continue;
                                }
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Gemini API Error: {ex}");
                _chatHistory.Add(Content.ForModel($"Error: {ex.Message}"));
            }
            finally
            {
                _isProcessing = false;
                _scrollPosition.y = float.MaxValue;
                _looped = 0;
                Repaint();
            }
        }

        void ResolveFunctionCalls()
        {
            if (_remainFunctionCalls.Count == 0) return;
            var toolResponseParts = new List<Part>();
            var needsReimport = ExecuteFunctionCalls(ref toolResponseParts);
            if (toolResponseParts.Count > 0)
            {
                _chatHistory.Add(new Content { Role = "tool", Parts = toolResponseParts });
                Repaint();
            }
            if (needsReimport)
            {
                Debug.Log("AssetDatabase refresh requested by a tool. Refreshing...");
                SaveState();
                AssetDatabase.Refresh();
            }
        }

        bool ExecuteFunctionCalls(ref List<Part> toolResponseParts)
        {
            Debug.Log("Execute Function Calls");
            while (_remainFunctionCalls.TryPop(out FunctionCall functionCall))
            {
                var functionResultJson = UnityToolSet.Execute(functionCall);
                toolResponseParts.Add(new Part { FunctionResponse = new FunctionResponse { Name = functionCall.Name, Response = new { content = functionResultJson } } });
                Debug.Log(functionResultJson);
                try
                {
                    using (var doc = JsonDocument.Parse(functionResultJson))
                    {
                        Debug.Log(doc);
                        if (doc.RootElement.TryGetProperty("requires_reload", out var reloadProp) && reloadProp.GetBoolean())
                        {
                            return true;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Ignore if the result is not a valid JSON.
                }
            }
            return false;
        }

        #endregion

        #region Helpers
        private bool IsApiConfigured() => _model != null;

        private void InitializeStyles()
        {
            _userMessageStyle = CreateStyle(new Color(0.2f, 0.4f, 0.7f));
            _modelMessageStyle = CreateStyle(new Color(0.3f, 0.3f, 0.3f));
            _toolCallStyle = CreateStyle(new Color(0.5f, 0.2f, 0.5f), FontStyle.Italic);
            _toolResponseStyle = CreateStyle(new Color(0.2f, 0.5f, 0.2f), FontStyle.Normal, 11);
            _styleInitialized = true;
        }

        private GUIStyle CreateStyle(Color backgroundColor, FontStyle fontStyle = FontStyle.Normal, int fontSize = 0)
        {
            var style = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                padding = new RectOffset(10, 10, 10, 10),
                richText = true,
                normal = { textColor = Color.white, background = CreateTexture(backgroundColor) },
                fontStyle = fontStyle
            };
            if (fontSize > 0) style.fontSize = fontSize;
            return style;
        }

        private Texture2D CreateTexture(Color color)
        {
            var pix = new[] { color };
            var result = new Texture2D(1, 1);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
        #endregion
    }
}