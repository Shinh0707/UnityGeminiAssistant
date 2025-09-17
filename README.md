# UnityGeminiAssistant

## 概要

このツールはGoogleとは無関係の、サードパーティ製プログラムです。

日本語環境での利用を想定したプロンプトが設定されています。

## 仕組み

[`Function Calling`](https://ai.google.dev/gemini-api/docs/function-calling)機能を利用して、Unityエディタの状態を取得・操作します。

チャットの状態は `Assets/Temp/Gemini/chat_state.json` に保存されます。
このファイルは初回起動時に自動で作成されます。

## 導入要件

-   [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) の導入
-   `System.Text.Json` パッケージの導入

## セットアップ手順

1.  [Google AI Studio](https://ai.google.dev/aistudio) にアクセスし、APIキーを取得します。

2.  取得したAPIキーを以下のファイルに設定します。

    **ファイルパス:** [`Assets/Settings/Gemini/secrets_gemini_config.json`](Assets/Settings/Gemini/secrets_gemini_config.json)
    ```json
    {
        "GeminiChatWindow_ApiKey":"ここに設定する"
    }
    ```

3.  （任意）ツールの設定を以下のファイルで指定できます。

    **ファイルパス:** [`Assets/Settings/Gemini/gemini_config.json`](Assets/Settings/Gemini/gemini_config.json)

    ```json
    {
        "GeminiChatWindow_Model": "gemini-2.5-pro",
        "GeminiChat_InstructionFile": "Settings/Gemini/assistant_instruction.md",
        "GeminiChatStateFile": "Temp/Gemini/chat_state.json",
        "GeminiChatMaxResponseLoop": 10,
        "ProtectedDirectories": [
            "Packages",
            "Plugins",
            "ProjectSettings",
            "Settings/Gemini",
            "Editor/GUI",
            "Editor/Gemini",
            "Editor/Externals",
            "Temp/Gemini"
        ]
    }
    ```

    ### 設定項目の説明

    | キー                         | 型      | 説明                                                                                                                              |
    | ---------------------------- | ------- | --------------------------------------------------------------------------------------------------------------------------------- |
    | `GeminiChatWindow_Model`     | `string`  | Geminiチャットウィンドウで使用するモデル名を指定します。モデル名は[Gemini公式ページ](https://ai.google.dev/gemini-api/docs/models#model-variations)に従います。                                                     |
    | `GeminiChat_InstructionFile` | `string`  | チャットAIに与える追加の指示が記述されたマークダウンファイルへのパスを指定します。                                     |
    | `GeminiChatStateFile`        | `string`  | チャットの状態（会話履歴など）を保存するJSONファイルへのパスを指定します。一時的なファイルがここに保存されます。                    |
    | `GeminiChatMaxResponseLoop`  | `int`     | AIの応答生成における最大ループ回数を指定します。意図しない無限ループを防ぐための設定です。                                        |
    | `ProtectedDirectories`       | `array`   | AIによるファイル操作やスキャンから保護する`Assets/`以下のディレクトリのリストを指定します。プロジェクトの重要な設定ファイルや外部ライブラリを保護します。 |

## 起動方法

Unityエディタ上部のメニューから `Tools > Gemini Chat` を選択すると、チャットウィンドウが開きます。

---

## ライセンス

このプロジェクトは **MITライセンス** の下で公開されています。
詳細については、プロジェクトに含まれる `LICENSE` ファイルをご確認ください。