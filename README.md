# UnityGeminiAssistant

## 概要

このツールはGoogleとは無関係の、サードパーティ製プログラムです。
日本語環境での利用を想定したプロンプトが設定されています。

## 仕組み

[`Function Calling`](https://ai.google.dev/gemini-api/docs/function-calling)機能を利用して、Unityエディタの状態を取得・操作します。

チャットの状態は `Assets/Temp/Gemini/chat_state.json` に保存されます。このファイルは初回起動時に自動で作成されます。

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

3.  （任意）ツールに無視させたいフォルダを以下のファイルで指定できます。

    **ファイルパス:** [`Assets/Settings/Gemini/gemini_config.json`](Assets/Settings/Gemini/gemini_config.json)
    ```json
    {
        ...,
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

## 起動方法

Unityエディタ上部のメニューから `Tools > Gemini Chat` を選択すると、チャットウィンドウが開きます。

---

## ライセンス

このプロジェクトは **MITライセンス** の下で公開されています。
詳細については、プロジェクトに含まれる `LICENSE` ファイルをご確認ください。