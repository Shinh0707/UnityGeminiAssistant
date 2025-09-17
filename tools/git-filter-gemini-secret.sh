#!/bin/bash
#
# git-filter-gemini-secret.sh
#
# Description:
#   A Git content filter script that replaces the content of the
#   Gemini secrets file with a placeholder JSON object.

set -eu

# 標準入力の内容は無視し、常に以下のプレースホルダーJSONを出力する
printf '%s\n' '{ "GeminiChatWindow_ApiKey":"__GEMINI_API_KEY_PLACEHOLDER__" }'