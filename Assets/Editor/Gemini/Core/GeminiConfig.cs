using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace Gemini.Core
{
    /// <summary>
    /// Settings/Gemini/gemini_config.json, Settings/Gemini/secrets_gemini_config.json から情報を読み込み、安全に提供します
    /// このクラスは静的であり、遅延初期化によって効率的なアクセスを実現します
    /// </summary>
    public static class Config
    {
        private static readonly Lazy<IReadOnlyDictionary<string, JsonElement>> _config =
            new Lazy<IReadOnlyDictionary<string, JsonElement>>(LoadConfiguration);

        private static readonly Lazy<IReadOnlyList<string>> _protectedDirectories =
            new Lazy<IReadOnlyList<string>>(LoadProtectedDirectories);

        private const string ConfigFileName = "Settings/Gemini/gemini_config.json";
        private const string SecretConfigFileName = "Settings/Gemini/secrets_gemini_config.json";

        /// <summary>
        /// アクセスが禁止されているディレクトリのリストを取得します。
        /// </summary>
        public static IReadOnlyList<string> ProtectedDirectories => _protectedDirectories.Value;

        /// <summary>
        /// 指定されたキーに対応する設定値（文字列）を取得します
        /// </summary>
        public static string Get(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (_config.Value.TryGetValue(key, out var jsonElement))
            {
                if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    return jsonElement.GetString();
                }
                throw new InvalidOperationException($"The value for key '{key}' is not a string.");
            }

            throw new KeyNotFoundException(
                $"The key '{key}' was not found in '{ConfigFileName}' or '{SecretConfigFileName}'.");
        }

        public static T Get<T>(string key, Func<JsonElement,T> cast)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (_config.Value.TryGetValue(key, out var jsonElement))
            {
                return cast(jsonElement);
            }

            throw new KeyNotFoundException(
                $"The key '{key}' was not found in '{ConfigFileName}' or '{SecretConfigFileName}'.");
        }

        private static IReadOnlyDictionary<string, JsonElement> LoadConfiguration()
        {
            var baseConfig = LoadConfigFile(ConfigFileName);
            var secretConfig = LoadConfigFile(SecretConfigFileName);

            foreach (var pair in secretConfig)
            {
                baseConfig[pair.Key] = pair.Value;
            }

            return new ReadOnlyDictionary<string, JsonElement>(baseConfig);
        }

        private static Dictionary<string, JsonElement> LoadConfigFile(string fileName)
        {
            var filePath = Path.Combine(Application.dataPath, fileName);

            if (!File.Exists(filePath))
            {
                // デバッグログは不要な場合があるため、より静かに失敗させる
                Debug.Log($"Configuration file not found, skipping: {filePath}");
                return new Dictionary<string, JsonElement>();
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                return config ?? new Dictionary<string, JsonElement>();
            }
            catch (Exception ex)
            {
                Debug.LogException(new Exception($"Failed to load or parse '{fileName}'. See inner exception for details.", ex));
                return new Dictionary<string, JsonElement>();
            }
        }

        private static IReadOnlyList<string> LoadProtectedDirectories()
        {
            if (!_config.Value.TryGetValue("ProtectedDirectories", out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return element.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()?.Replace('\\', '/').Trim('/'))
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => Path.Combine(Application.dataPath,s))
                .ToList()
                .AsReadOnly();
        }
    }
}