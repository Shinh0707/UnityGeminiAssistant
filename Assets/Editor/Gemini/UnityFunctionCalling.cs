using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Gemini.Api;
using Gemini.Core;
using Gemini.Editor.Attributes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Tool = Gemini.Api.Tool;

namespace Gemini.Editor
{
    /// <summary>
    /// Defines and executes a set of Unity-related tools for use with Gemini's Function Calling feature.
    /// It dynamically generates tool definitions from methods marked with attributes using reflection.
    /// </summary>
    public static class UnityToolSet
    {
        private static readonly Lazy<Tool> _toolDefinition = new(GenerateToolDefinition);
        private static readonly Lazy<Dictionary<string, MethodInfo>> _executorMap = new(BuildExecutorMap);

        public static Tool GetToolDefinition() => _toolDefinition.Value;

        public static string Execute(FunctionCall functionCall)
        {
            var methodName = ToPascalCase(functionCall.Name);
            if (!_executorMap.Value.TryGetValue(methodName, out var method))
            {
                return CreateErrorResponse($"Function '{methodName}' is not defined.");
            }
            try
            {
                var argsDict = functionCall.Args.ValueKind == JsonValueKind.Object
                    ? functionCall.Args.Deserialize<Dictionary<string, JsonElement>>()
                    : new Dictionary<string, JsonElement>();

                var parameters = method.GetParameters();
                var args = new object[parameters.Length];
                for (var i = 0; i < parameters.Length; i++)
                {
                    var paramNameSnakeCase = ToSnakeCase(parameters[i].Name);
                    if (argsDict != null && argsDict.TryGetValue(paramNameSnakeCase, out var value))
                    {
                        args[i] = value.Deserialize(parameters[i].ParameterType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    else if (parameters[i].HasDefaultValue)
                    {
                        args[i] = parameters[i].DefaultValue;
                    }
                    else
                    {
                        throw new ArgumentException($"Missing required argument: {paramNameSnakeCase}");
                    }
                }
                var result = method.Invoke(null, args);
                return result as string ?? CreateErrorResponse("Function returned null.");
            }
            catch (Exception ex)
            {
                var innerEx = ex.InnerException ?? ex;
                Debug.LogError($"Error executing function '{methodName}': {innerEx.Message}\n{innerEx.StackTrace}");
                return CreateErrorResponse(innerEx.Message);
            }
        }

        #region Response Helpers

        private static string CreateSuccessResponse(string message, object result = null, bool requiresReload = false)
        {
            var response = new Dictionary<string, object>
            {
                ["status"] = "success",
                ["message"] = message
            };
            if (result != null) response["result"] = result;
            if (requiresReload) response["requires_reload"] = true;
            return JsonSerializer.Serialize(response, new JsonSerializerOptions{ PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }

        private static string CreateErrorResponse(string message)
        {
            var response = new 
            {
                status = "error",
                message = message
            };
            return JsonSerializer.Serialize(response);
        }

        #endregion
        
        #region Reflection Utilities

        private static Tool GenerateToolDefinition()
        {
            var declarations = typeof(UnityToolSet)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<ToolFunctionAttribute>() != null)
                .Select(CreateFunctionDeclaration)
                .ToList();
            return new Tool { FunctionDeclarations = declarations };
        }

        private static Dictionary<string, MethodInfo> BuildExecutorMap() =>
            typeof(UnityToolSet)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<ToolFunctionAttribute>() != null)
                .ToDictionary(m => m.Name, m => m);

        private static FunctionDeclaration CreateFunctionDeclaration(MethodInfo methodInfo)
        {
            var toolAttr = methodInfo.GetCustomAttribute<ToolFunctionAttribute>();
            var parameters = methodInfo.GetParameters();
            var properties = new Dictionary<string, Schema>();
            var required = parameters.Where(p => !p.HasDefaultValue)
                                     .Select(p => ToSnakeCase(p.Name))
                                     .ToList();

            foreach (var param in parameters)
            {
                var paramAttr = param.GetCustomAttribute<ToolParameterAttribute>();
                Schema property;

                if (param.ParameterType.IsArray)
                {
                    var elementType = param.ParameterType.GetElementType();
                    property = new Schema
                    {
                        Type = SchemaType.ARRAY,
                        Description = paramAttr?.Description ?? string.Empty,
                        Items = new Schema { Type = MapToSchemaType(elementType) }
                    };
                }
                else
                {
                    property = new Schema
                    {
                        Type = MapToSchemaType(param.ParameterType),
                        Description = paramAttr?.Description ?? string.Empty
                    };
                }
                properties[ToSnakeCase(param.Name)] = property;
            }

            return new FunctionDeclaration
            {
                Name = ToSnakeCase(methodInfo.Name),
                Description = toolAttr.Description,
                Parameters = new Schema
                {
                    Type = SchemaType.OBJECT,
                    Properties = properties,
                    Required = required
                }
            };
        }

        private static SchemaType MapToSchemaType(Type type)
        {
            if (type.IsArray) return SchemaType.ARRAY;

            return Type.GetTypeCode(type) switch
            {
                TypeCode.String => SchemaType.STRING,
                TypeCode.Int32 or TypeCode.Int64 or TypeCode.UInt32 or TypeCode.UInt64 => SchemaType.INTEGER,
                TypeCode.Single or TypeCode.Double or TypeCode.Decimal => SchemaType.NUMBER,
                TypeCode.Boolean => SchemaType.BOOLEAN,
                _ => SchemaType.STRING,
            };
        }

        private static string ToSnakeCase(string text) => Regex.Replace(text, "(?<=.)([A-Z])", "_$1", RegexOptions.Compiled).ToLower();
        private static string ToPascalCase(string text) => string.Concat(text.Split('_').Select(s => char.ToUpper(s[0]) + s.Substring(1)));

        #endregion

        #region Function Implementations with Attributes

        [ToolFunction("Get the current state of the scene hierarchy as a tree structure.")]
        public static string GetHierarchy()
        {
            var rootObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.InstanceID).Where(go => go.transform.parent == null);
            var builder = new StringBuilder();
            foreach (var root in rootObjects) AppendGameObject(builder, root, 0);
            return CreateSuccessResponse("Hierarchy retrieved successfully.", builder.ToString());

            void AppendGameObject(StringBuilder sb, GameObject go, int indent)
            {
                sb.Append(' ', indent * 2).AppendLine(go.name);
                foreach (Transform child in go.transform) AppendGameObject(sb, child.gameObject, indent + 1);
            }
        }

        [ToolFunction("Creates a new GameObject, optionally as a primitive, and places it in the hierarchy.")]
        public static string CreateGameObject(
            [ToolParameter("The name for the new GameObject.")]
            string gameObjectName,
            [ToolParameter("Optional. The name of the parent GameObject. If null, creates the object at the root.")]
            string parentName = null,
            [ToolParameter("Optional. Creates a primitive object ('Cube', 'Sphere', 'Capsule', 'Cylinder', 'Plane', 'Quad'). Overrides component_names.")]
            string primitiveType = null,
            [ToolParameter("Optional. An array of component names to add if not creating a primitive.")]
            string[] componentNames = null,
            [ToolParameter("Optional. The local position, formatted as 'x,y,z'.")]
            string position = null,
            [ToolParameter("Optional. The local rotation (Euler angles), formatted as 'x,y,z'.")]
            string rotation = null,
            [ToolParameter("Optional. The local scale, formatted as 'x,y,z'.")]
            string scale = null)
        {
            GameObject newObject = null;
            try
            {
                if (!string.IsNullOrEmpty(primitiveType))
                {
                    if (!Enum.TryParse<PrimitiveType>(primitiveType, true, out var parsedPrimitiveType))
                    {
                        return CreateErrorResponse($"Invalid primitive type '{primitiveType}'. Valid types are: Cube, Sphere, Capsule, Cylinder, Plane, Quad.");
                    }
                    newObject = GameObject.CreatePrimitive(parsedPrimitiveType);
                    newObject.name = gameObjectName;
                }
                else
                {
                    var componentTypes = (componentNames ?? Array.Empty<string>())
                        .Select(FindTypeByName)
                        .ToArray();

                    if (componentTypes.Any(t => t == null))
                    {
                        var notFound = componentNames.Where((name, i) => componentTypes[i] == null);
                        return CreateErrorResponse($"Could not find component type(s): {string.Join(", ", notFound)}.");
                    }
                    newObject = new GameObject(gameObjectName, componentTypes);
                }

                if (!string.IsNullOrEmpty(parentName))
                {
                    var parentObject = GameObject.Find(parentName);
                    if (parentObject == null)
                    {
                        Object.DestroyImmediate(newObject);
                        return CreateErrorResponse($"Parent GameObject '{parentName}' not found.");
                    }
                    newObject.transform.SetParent(parentObject.transform, worldPositionStays: false);
                }

                if (TryParseVector3(position, out var pos)) newObject.transform.localPosition = pos;
                if (TryParseVector3(rotation, out var rot)) newObject.transform.localEulerAngles = rot;
                if (TryParseVector3(scale, out var scl)) newObject.transform.localScale = scl;

                Undo.RegisterCreatedObjectUndo(newObject, $"Create {gameObjectName}");
                Selection.activeGameObject = newObject;

                return CreateSuccessResponse($"Successfully created GameObject '{gameObjectName}'.", new { name = newObject.name, instanceId = newObject.GetInstanceID() });
            }
            catch (Exception ex)
            {
                if (newObject != null) Object.DestroyImmediate(newObject);
                return CreateErrorResponse($"An unexpected error occurred while creating the GameObject: {ex.Message}");
            }
        }
        
        [ToolFunction("Deletes a GameObject from the scene.")]
        public static string DestroyGameObject(
            [ToolParameter("The exact name of the GameObject to destroy.")] string gameObjectName)
        {
            var go = GameObject.Find(gameObjectName);
            if (go == null)
            {
                return CreateErrorResponse($"GameObject '{gameObjectName}' not found.");
            }

            Undo.DestroyObjectImmediate(go);
            return CreateSuccessResponse($"Successfully destroyed GameObject '{gameObjectName}'.");
        }

        [ToolFunction("Adds a new component to a specified GameObject.")]
        public static string AddComponent(
            [ToolParameter("The exact name of the GameObject to add the component to.")] string gameObjectName,
            [ToolParameter("The full name of the component type to add (e.g., 'Rigidbody' (unnecessary 'UnityEngine.'), 'MyPlayerScript').")] string componentName)
        {
            var go = GameObject.Find(gameObjectName);
            if (go == null) return CreateErrorResponse($"GameObject '{gameObjectName}' not found.");
            
            var componentType = FindTypeByName(componentName);
            if (componentType == null) return CreateErrorResponse($"Component type '{componentName}' not found.");
            
            if (!typeof(Component).IsAssignableFrom(componentType)) return CreateErrorResponse($"Type '{componentName}' is not a valid Component.");
            
            var component = go.AddComponent(componentType);
            Undo.RegisterCreatedObjectUndo(component, $"Add {componentName} to {gameObjectName}");

            return CreateSuccessResponse($"Successfully added component '{componentName}' to GameObject '{gameObjectName}'.");
        }

        [ToolFunction("Removes a component from a specified GameObject.")]
        public static string RemoveComponent(
            [ToolParameter("The exact name of the GameObject to remove the component from.")] string gameObjectName,
            [ToolParameter("The exact name of the component to remove (e.g., 'Rigidbody' (unnecessary 'UnityEngine.'), 'MyPlayerScript').")] string componentName)
        {
            var go = GameObject.Find(gameObjectName);
            if (go == null) return CreateErrorResponse($"GameObject '{gameObjectName}' not found.");
            
            var component = go.GetComponent(componentName);
            if (component == null) return CreateErrorResponse($"Component '{componentName}' not found on '{gameObjectName}'.");
            
            Undo.DestroyObjectImmediate(component);
            return CreateSuccessResponse($"Successfully removed component '{componentName}' from '{gameObjectName}'.");
        }

        [ToolFunction("Get a list of components attached to a specific GameObject.")]
        public static string GetComponents(
            [ToolParameter("The exact name of the GameObject.")] string gameObjectName)
        {
            var go = GameObject.Find(gameObjectName);
            if (go == null) return CreateErrorResponse($"GameObject '{gameObjectName}' not found.");
            var componentNames = go.GetComponents<Component>().Select(c => c.GetType().Name);
            return CreateSuccessResponse("Components retrieved successfully.", componentNames);
        }

        [ToolFunction("Get the public parameters (fields and properties) of a specific component on a GameObject.")]
        public static string GetComponentParameters(
            [ToolParameter("The exact name of the GameObject.")] string gameObjectName,
            [ToolParameter("The exact name of the Component (e.g., 'Transform', 'MyScript').")] string componentName)
        {
            var go = GameObject.Find(gameObjectName);
            if (go == null) return CreateErrorResponse($"GameObject '{gameObjectName}' not found.");
            var component = go.GetComponent(componentName);
            if (component == null) return CreateErrorResponse($"Component '{componentName}' not found on '{gameObjectName}'.");

            var members = component.GetType()
                .GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.MemberType == MemberTypes.Field || (m.MemberType == MemberTypes.Property && ((PropertyInfo)m).CanRead))
                .Select(m => new { Name = m.Name, Value = GetMemberValue(m, component)?.ToString() ?? "null" })
                .ToDictionary(m => m.Name, m => m.Value);
            return CreateSuccessResponse("Component parameters retrieved successfully.", members);

            object GetMemberValue(MemberInfo member, object target)
            {
                try { return member.MemberType == MemberTypes.Field ? ((FieldInfo)member).GetValue(target) : ((PropertyInfo)member).GetValue(target); }
                catch { return "Error: Could not read value."; }
            }
        }
        
        [ToolFunction("Sets the value of a public field or property on a component.")]
        public static string SetComponentParameter(
            [ToolParameter("The exact name of the GameObject.")] string gameObjectName,
            [ToolParameter("The exact name of the Component.")] string componentName,
            [ToolParameter("The name of the public field or property to modify.")] string parameterName,
            [ToolParameter("The new value to set, as a string. For Vector3, use 'x,y,z'.")] string value)
        {
            var go = GameObject.Find(gameObjectName);
            if (go == null) return CreateErrorResponse($"GameObject '{gameObjectName}' not found.");
        
            var component = go.GetComponent(componentName);
            if (component == null) return CreateErrorResponse($"Component '{componentName}' not found on '{gameObjectName}'.");
        
            var member = component.GetType().GetMember(parameterName, BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.MemberType == MemberTypes.Field || m.MemberType == MemberTypes.Property);
        
            if (member == null) return CreateErrorResponse($"Parameter '{parameterName}' not found on component '{componentName}'.");
            if (member is PropertyInfo prop && !prop.CanWrite) return CreateErrorResponse($"Property '{parameterName}' is read-only.");
        
            Undo.RecordObject(component, $"Set {parameterName}");
        
            if (TryParseAndSetValue(component, member, value))
            {
                EditorUtility.SetDirty(component);
                return CreateSuccessResponse($"Successfully set '{parameterName}' on '{componentName}' to '{value}'.");
            }
            
            var memberType = member.MemberType == MemberTypes.Field ? ((FieldInfo)member).FieldType : ((PropertyInfo)member).PropertyType;
            return CreateErrorResponse($"Could not parse value '{value}' for parameter '{parameterName}' of type '{memberType.Name}'.");
        }

        [ToolFunction("Retrieves structured information about a script file, combining reflection and XML doc comments.")]
        public static string GetScriptContent(
            [ToolParameter("The path to the script file, relative to the project root (e.g., 'Assets/Scripts/Player.cs' or 'Scripts/Player.cs').")] string filePath)
        {
            if (!TryGetValidatedFullPath(filePath, out var fullPath, expectDirectory: false))
            {
                return CreateErrorResponse($"File not found at '{filePath}' or access is denied.");
            }
            try
            {
                var className = Path.GetFileNameWithoutExtension(fullPath);
                var type = FindTypeByClassName(className);
                if (type == null) return CreateErrorResponse($"Could not find type '{className}' in any loaded assembly.");

                var sourceCode = File.ReadAllText(fullPath);
                var xmlDocs = ParseXmlDocumentation(sourceCode);
                var formattedInfo = FormatTypeInformation(type, xmlDocs);
                return CreateSuccessResponse("Script content retrieved successfully.", formattedInfo);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"An error occurred while getting script content: {ex.Message}");
            }
        }

        [ToolFunction("Provides a tree-like view of a specific directory within the project folder.")]
        public static string GetDirectoryTree(
            [ToolParameter("The path to the directory, relative to the project assets root (e.g., 'Assets/Prefabs' or 'Prefabs' or '/'(all files)).")] string directoryPath)
        {
            if (directoryPath == "/")
            {
                directoryPath = "";
            }
            if (!TryGetValidatedFullPath(directoryPath, out var fullPath, expectDirectory: true))
            {
                return CreateErrorResponse($"Directory not found at '{directoryPath}' or access is denied.");
            }

            var builder = new StringBuilder();
            Debug.Log(directoryPath);
            Debug.Log(fullPath);
            BuildTree(directoryPath, "", true);
            return CreateSuccessResponse("Directory tree retrieved successfully.", builder.ToString());

            void BuildTree(string path, string indent, bool last)
            {
                Debug.Log(path);
                if (!TryGetValidatedFullPath(path, out string fp, checkExistence: false)) return;
                Debug.Log(fp);
                builder.Append(indent).Append(last ? "└── " : "├── ").AppendLine(File.Exists(fp) ? Path.GetFileName(fp) : fp[fp.LastIndexOf("/")..]);
                indent += last ? "    " : "│   ";
                var subDirs = Directory.GetDirectories(fp);
                var files = Directory.GetFiles(fp).Where(f => !f.EndsWith(".meta")).ToArray();
                for (var i = 0; i < subDirs.Length; i++) BuildTree(subDirs[i], indent, i == subDirs.Length - 1 && files.Length == 0);
                for (var i = 0; i < files.Length; i++) builder.Append(indent).Append(i == files.Length - 1 ? "└── " : "├── ").AppendLine(Path.GetFileName(files[i]));
            }
        }

        [ToolFunction("Retrieves the latest logs from the Unity console (errors, warnings, and standard logs).")]
        public static string GetLogs()
        {
            var logEntriesType = Type.GetType("UnityEditor.LogEntries, UnityEditor.dll");
            if (logEntriesType == null) return CreateErrorResponse("Could not find UnityEditor.LogEntries type.");
            
            var getCountMethod = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
            var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);
            var startGettingEntriesMethod = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
            var endGettingEntriesMethod = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public);
            if (getCountMethod == null || getEntryMethod == null || startGettingEntriesMethod == null || endGettingEntriesMethod == null)
                return CreateErrorResponse("Could not access UnityEditor.LogEntries internal methods.");

            startGettingEntriesMethod.Invoke(null, null);
            var count = (int)getCountMethod.Invoke(null, null);
            var logs = new List<string>();
            var logEntry = Activator.CreateInstance(Type.GetType("UnityEditor.LogEntry, UnityEditor.dll"));
            for (var i = 0; i < Math.Min(count, 50); i++)
            {
                getEntryMethod.Invoke(null, new[] { i, logEntry });
                var message = logEntry.GetType().GetField("message")?.GetValue(logEntry)?.ToString();
                var mode = (int)logEntry.GetType().GetField("mode").GetValue(logEntry);
                var type = (mode & 1024) != 0 ? "Error" : (mode & 512) != 0 ? "Warning" : "Log";
                logs.Add($"[{type}] {message?.Trim()}");
            }
            endGettingEntriesMethod.Invoke(null, null);
            return CreateSuccessResponse("Logs retrieved successfully.", logs.AsEnumerable().Reverse());
        }
        
        [ToolFunction("Creates a new C# script file. The file content must adhere to the following style guide: 1. Add English XML Docstrings (///) for all public types and members. 2. Keep inline comments to a minimum. 3. Do not use emojis anywhere in the code.")]
        public static string CreateScriptFile(
            [ToolParameter("The path for the new file, relative to the project root (e.g., 'Assets/Scripts/NewScript.cs').")] string filePath,
            [ToolParameter("The C# code to write into the file.")] string content)
        {
            if (!TryGetValidatedFullPath(filePath, out var fullPath, checkExistence: false))
            {
                return CreateErrorResponse($"The path '{filePath}' is invalid or outside the project directory.");
            }

            if (File.Exists(fullPath) || Directory.Exists(fullPath))
            {
                return CreateErrorResponse($"A file or directory already exists at '{fullPath}'.");
            }
            try
            {
                var directoryName = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(directoryName)) return CreateErrorResponse($"Could not determine the directory for '{fullPath}'.");
                
                Directory.CreateDirectory(directoryName);
                File.WriteAllText(fullPath, content);
                return CreateSuccessResponse($"Successfully created script at '{ToRelativeAssetPath(fullPath)}'. Asset refresh is required.", requiresReload: true);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to create script: {ex.Message}");
            }
        }

        [ToolFunction("Rewrites a specific range of lines in an existing script file. The new content provided must adhere to the following style guide: 1. Add English XML Docstrings (///) for all public types and members. 2. Keep inline comments to a minimum. 3. Do not use emojis anywhere in the code.")]
        public static string RewriteScriptFile(
            [ToolParameter("The path to the script file, relative to the project root (e.g., 'Assets/Scripts/Player.cs').")] string filePath,
            [ToolParameter("The starting line number to replace (1-based index).")] int startLine,
            [ToolParameter("The ending line number to replace (1-based index).")] int endLine,
            [ToolParameter("The new content to insert in place of the specified lines.")] string newContent)
        {
            if (!TryGetValidatedFullPath(filePath, out var fullPath, expectDirectory: false))
            {
                return CreateErrorResponse($"File not found at '{filePath}' or access is denied.");
            }

            try
            {
                var lines = File.ReadAllLines(fullPath).ToList();
                if (startLine < 1 || endLine > lines.Count || startLine > endLine)
                {
                    return CreateErrorResponse("Invalid line range.");
                }

                lines.RemoveRange(startLine - 1, endLine - startLine + 1);
                lines.InsertRange(startLine - 1, newContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
                File.WriteAllLines(fullPath, lines);
                return CreateSuccessResponse($"Successfully rewrote lines {startLine}-{endLine} in '{ToRelativeAssetPath(fullPath)}'.", requiresReload: true);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"Failed to rewrite script: {ex.Message}");
            }
        }

        #endregion

        #region Filesystem Helpers
        
        public static string ToRelativeAssetPath(string fullPath)
        {
            if (fullPath == null) throw new ArgumentNullException(nameof(fullPath));

            var normalizedPath = fullPath.Replace("\\", "/");
            var assetsIndex = normalizedPath.IndexOf("Assets/", StringComparison.Ordinal);

            if (assetsIndex < 0)
            {
                Debug.LogWarning($"The specified path does not contain 'Assets/': {fullPath}");
                return fullPath;
            }
            return normalizedPath[assetsIndex..];
        }
        
        private static string NormalizeToAssetsRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return Application.dataPath;
            
            var cleanedPath = path.TrimStart('/', '\\');
            return cleanedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Application.dataPath, cleanedPath[7..])
                : Path.Combine(Application.dataPath, cleanedPath);
        }

        private static bool TryGetValidatedFullPath(
            string relativePath,
            out string fullPath,
            bool checkExistence = true,
            bool expectDirectory = false)
        {
            var normalizedRequestPath = NormalizeToAssetsRelativePath(relativePath);
            var protectedDirs = Config.ProtectedDirectories;
            if (protectedDirs.Any(protectedDir => normalizedRequestPath.StartsWith(protectedDir, StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogError($"Access Denied: The path '{relativePath}' is within a protected directory.");
                fullPath = string.Empty;
                return false;
            }

            try
            {
                fullPath = Path.GetFullPath(normalizedRequestPath);
            }
            catch (ArgumentException)
            {
                fullPath = string.Empty;
                return false;
            }

            if (!checkExistence) return true;
            
            var exists = expectDirectory ? Directory.Exists(fullPath) : File.Exists(fullPath);
            if (!exists) fullPath = string.Empty;
            return exists;
        }

        #endregion

        #region Script Content Helpers

        private static Type FindTypeByClassName(string className)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .FirstOrDefault(t => t.Name == className);
        }

        private static Dictionary<string, XElement> ParseXmlDocumentation(string sourceCode)
        {
            var docDict = new Dictionary<string, XElement>();
            const string pattern = @"(\s*\/\/\/ <summary>.*?\s*\/\/\/.*?)^\s*.*?(?:class|struct|enum|interface|delegate|void|[\w\.<>\[\]]+\s+)(\w+)(?:\s*\(|\s*;|\s*{|\s*:)";
            var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.Multiline);
            
            foreach (Match match in regex.Matches(sourceCode))
            {
                var xmlComment = match.Groups[1].Value;
                var memberName = match.Groups[2].Value;
                var cleanXml = "<doc>" + Regex.Replace(xmlComment, @"^\s*\/\/\/", "", RegexOptions.Multiline) + "</doc>";

                try
                {
                    var element = XElement.Parse(cleanXml);
                    if (!docDict.ContainsKey(memberName)) docDict.Add(memberName, element);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"XML parsing failed for member '{memberName}': {ex.Message}");
                }
            }
            return docDict;
        }

        private static string FormatTypeInformation(Type type, IReadOnlyDictionary<string, XElement> xmlDocs)
        {
            var builder = new StringBuilder();
            var typeName = GetFullTypeName(type);
            builder.AppendLine($"**Class: {typeName}**");
            if (type.IsNested) builder.AppendLine($"*Nesting: {type.DeclaringType.FullName}*");
            if (xmlDocs.TryGetValue(type.Name, out var classDoc))
            {
                builder.AppendLine($"*Summary:* {classDoc.Element("summary")?.Value.Trim() ?? "N/A"}");
            }
            builder.AppendLine("\n---");

            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            foreach (var member in members)
            {
                xmlDocs.TryGetValue(member.Name, out var memberDoc);
                switch (member.MemberType)
                {
                    case MemberTypes.Method:
                        var method = (MethodInfo)member;
                        if (!method.IsSpecialName) FormatMethod(builder, method, memberDoc);
                        break;
                    case MemberTypes.Property:
                        FormatProperty(builder, (PropertyInfo)member, memberDoc);
                        break;
                }
            }
            return builder.ToString();
        }

        private static void FormatMethod(StringBuilder builder, MethodInfo method, XElement memberDoc)
        {
            var parameters = method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}").ToArray();
            builder.AppendLine($"**Method: {method.ReturnType.Name} {method.Name}({string.Join(", ", parameters)})**");
            builder.AppendLine($"*Summary:* {memberDoc?.Element("summary")?.Value.Trim() ?? "N/A"}");
            var paramDocs = memberDoc?.Elements("param").ToDictionary(p => p.Attribute("name")?.Value, p => p.Value.Trim());
            
            if (method.GetParameters().Any())
            {
                builder.AppendLine("*Parameters:*");
                foreach (var p in method.GetParameters())
                {
                    var doc = paramDocs != null && paramDocs.TryGetValue(p.Name, out var d) ? d : "N/A";
                    builder.AppendLine($"  - `{p.Name}` ({p.ParameterType.Name}): {doc}");
                }
            }
            
            var returnsDoc = memberDoc?.Element("returns")?.Value.Trim();
            if (!string.IsNullOrEmpty(returnsDoc)) builder.AppendLine($"*Returns:* ({method.ReturnType.Name}) {returnsDoc}");
            builder.AppendLine("\n---");
        }

        private static void FormatProperty(StringBuilder builder, PropertyInfo property, XElement memberDoc)
        {
            builder.AppendLine($"**Property: {property.PropertyType.Name} {property.Name} {{ {(property.CanRead ? "get; " : "")}{(property.CanWrite ? "set; " : "")}}}**");
            builder.AppendLine($"*Summary:* {memberDoc?.Element("summary")?.Value.Trim() ?? "N/A"}");
            builder.AppendLine("\n---");
        }

        private static string GetFullTypeName(Type type)
        {
            if (!type.IsGenericType) return type.FullName ?? type.Name;
            var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetFullTypeName));
            return $"{type.Name.Split('`')[0]}<{genericArgs}>";
        }

        private static Type FindTypeByName(string name)
        {
            var type = Type.GetType(name, false, true);
            if (type != null) return type;
            
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEngine." + name, false, true))
                .FirstOrDefault(foundType => foundType != null);
        }
        
        private static bool TryParseAndSetValue(object component, MemberInfo member, string stringValue)
        {
            var memberType = member.MemberType == MemberTypes.Field
                ? ((FieldInfo)member).FieldType
                : ((PropertyInfo)member).PropertyType;

            object parsedValue = null;

            if (memberType == typeof(string)) parsedValue = stringValue;
            else if (memberType == typeof(int) && int.TryParse(stringValue, out var intVal)) parsedValue = intVal;
            else if (memberType == typeof(float) && float.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal)) parsedValue = floatVal;
            else if (memberType == typeof(bool) && bool.TryParse(stringValue, out var boolVal)) parsedValue = boolVal;
            else if (memberType == typeof(Vector3) && TryParseVector3(stringValue, out var vec3Val)) parsedValue = vec3Val;
            else if (memberType.IsEnum && Enum.TryParse(memberType, stringValue, true, out var enumVal)) parsedValue = enumVal;

            if (parsedValue == null) return false;

            if (member.MemberType == MemberTypes.Field) ((FieldInfo)member).SetValue(component, parsedValue);
            else if (member.MemberType == MemberTypes.Property) ((PropertyInfo)member).SetValue(component, parsedValue);
            else return false;

            return true;
        }

        private static bool TryParseVector3(string input, out Vector3 result)
        {
            result = Vector3.zero;
            if (string.IsNullOrEmpty(input)) return false;

            var parts = input.Split(',');
            if (parts.Length != 3) return false;

            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                result = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        #endregion
    }
}