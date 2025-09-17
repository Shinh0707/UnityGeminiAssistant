# UnityGeminiAssistant

## 概要
このツールはUnityエディタをAIアシスタント（Gemini）と連携させ、自然言語による対話を通じてエディタ操作を自動化するためのものです。`Function Calling`機能を活用し、Unityのコア機能をAIから直接呼び出すことで、シーンの構築、コンポーネントの操作、スクリプトの編集などを効率化します。

現在対応している処理は以下のようになります。

### シーン操作
- **階層取得**: シーン内のゲームオブジェクトの階層をツリー形式で取得します。
- **GameObject作成**: 新しいGameObjectを作成します。プリミティブ（キューブ、スフィア等）の指定や、親オブジェクト、コンポーネントの追加、Transform（位置、回転、スケール）の設定も可能です。
- **GameObject削除**: 指定したGameObjectをシーンから削除します。

### コンポーネント操作
- **コンポーネント追加**: 指定したGameObjectに新しいコンポーネントを追加します。
- **コンポーネント削除**: GameObjectから特定のコンポーネントを削除します。
- **コンポーネント一覧取得**: 指定したGameObjectにアタッチされている全てのコンポーネント名を取得します。
- **パラメータ取得**: コンポーネントが持つpublicなパラメータ（フィールドやプロパティ）とその値を取得します。
- **パラメータ設定**: コンポーネントのpublicなパラメータに新しい値を設定します。

### ファイル・スクリプト操作
- **ディレクトリ構造表示**: プロジェクト内の指定したフォルダのツリー構造を表示します。
- **スクリプト内容取得**: C#スクリプトのクラス構造、メソッド、プロパティ、そしてXMLドキュメントコメントを解析し、整形された情報を取得します。
- **スクリプト作成**: 指定したパスに新しいC#スクリプトを生成します。
- **スクリプト書き換え**: 既存のスクリプト内の指定した行範囲を、新しい内容で上書きします。

### エディタ情報
- **コンソールログ取得**: Unityエディタのコンソールに出力された最新のログ（情報、警告、エラー）を取得します。

## 注意
⚠️ **このツールはベータ版です。** 予期せぬファイル操作など、思いもよらない処理をする可能性があります。**ご使用の際は、必ずプロジェクト全体のバックアップを取ってからお試しください。**

このツールはGoogleとは無関係の、サードパーティ製プログラムです。

日本語環境での利用を想定したプロンプトが設定されています。

APIキーはご自身のものを使用して頂きます。使用時の料金については、自己責任でお願いします。

## 仕組み

GeminiのREST APIを使用しています。

[`Function Calling`](https://ai.google.dev/gemini-api/docs/function-calling)機能を利用して、Unityエディタの状態を取得・操作します。C#のリフレクション機能を用いて、特定の属性（`[ToolFunction]`）が付与された静的メソッドを動的にGeminiへ公開するツールセットを構築しています。ユーザーからの指示に応じて、Geminiはこれらのツール（C#メソッド）を呼び出すためのJSONを生成し、Unity側でそれを解釈・実行して結果を返します。

## 導入要件

-   [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) の導入
-   **NuGetForUnity**を用いた`System.Text.Json` パッケージの導入

## セットアップ手順

1.  [Google AI Studio](https://ai.google.dev/aistudio) にアクセスし、APIキーを取得する。

2.  取得したAPIキーを以下のファイルに設定する。

    **ファイルパス:** `Assets/Settings/Gemini/secrets_gemini_config.json`
    ```json
    {
        "GeminiChatWindow_ApiKey":"ここに設定"
    }
    ```

3.  （任意）ツールの設定を以下のファイルで指定する。

    **ファイルパス:** `Assets/Settings/Gemini/gemini_config.json`

    ```json
    {
        "GeminiChatWindow_Model":"gemini-1.5-pro-latest",
        "GeminiChat_InstructionFile": "Settings/Gemini/assistant_instruction.md",
        "GeminiChatStateFile": "Temp/Gemini/chat_state.json",
        "GeminiChatMaxResponseLoop": 10,
        "ProtectedDirectories": [
            "Packages",
            "Plugins",
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
    | `GeminiChatWindow_Model`     | `string`  | Geminiチャットウィンドウで使用するモデル名を指定。モデル名は[Gemini公式ページ](https://ai.google.dev/gemini-api/docs/models/gemini)に従う。                                                     |
    | `GeminiChat_InstructionFile` | `string`  | チャットAIに与える追加の指示が記述されたマークダウンファイルへのパスを指定。                                     |
    | `GeminiChatStateFile`        | `string`  | チャットの状態（会話履歴など）を保存するJSONファイルへのパスを指定。一時的なファイルがここに保存される。                    |
    | `GeminiChatMaxResponseLoop`  | `int`     | AIの応答生成における最大ループ回数を指定。意図しない無限ループを防ぐための設定。                                        |
    | `ProtectedDirectories`       | `array`   | AIによるファイル操作やスキャンから保護する`Assets/`以下のディレクトリのリストを指定。プロジェクトの重要な設定ファイルや外部ライブラリを保護する。 |

4. このリポジトリの`Assets`フォルダを、自身のUnityプロジェクトフォルダ内に配置する。

## 起動方法

Unityエディタ上部のメニューから `Tools > Gemini Chat` を選択すると、チャットウィンドウが開きます。

---

## ライセンス

このプロジェクトは **MITライセンス** の下で公開されています。
詳細については、プロジェクトに含まれる `LICENSE` ファイルをご確認ください。