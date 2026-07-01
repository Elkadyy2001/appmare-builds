#!/usr/bin/env bash
# AppMare AutoFix — runs OpenCode CLI to fix build errors
# Usage: bash autofix.sh --provider <provider> --model <model> --api-key <key> --prompt "<text>" [--build-cmd "<cmd>"]
set -euo pipefail

PROVIDER=""
MODEL=""
API_KEY=""
PROMPT=""
BUILD_CMD=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --provider)  PROVIDER="$2";  shift 2 ;;
        --model)     MODEL="$2";     shift 2 ;;
        --api-key)   API_KEY="$2";   shift 2 ;;
        --prompt)    PROMPT="$2";    shift 2 ;;
        --build-cmd) BUILD_CMD="$2"; shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

: "${PROVIDER:?--provider is required (e.g. anthropic, openai, google, groq)}"
: "${MODEL:?--model is required (e.g. claude-sonnet-4-5, gpt-4o)}"
: "${API_KEY:?--api-key is required}"
: "${PROMPT:?--prompt is required}"

# Install OpenCode if not present
if ! command -v opencode &>/dev/null; then
    echo ">>> Installing OpenCode CLI..."
    curl -fsSL https://opencode.ai/install | bash
fi

export PATH="$HOME/.opencode/bin:$HOME/.local/bin:$PATH"

# Write credentials for the configured provider
case "$PROVIDER" in
    opencode-go)
        mkdir -p "$HOME/.local/share/opencode"
        python3 -c "
import json, os
path = os.path.expanduser('~/.local/share/opencode/auth.json')
try:
    with open(path) as f:
        data = json.load(f)
except (FileNotFoundError, json.JSONDecodeError):
    data = {}
data['opencode-go'] = {'type': 'api', 'key': '$API_KEY'}
with open(path, 'w') as f:
    json.dump(data, f)
"
        ;;
    anthropic)   export ANTHROPIC_API_KEY="$API_KEY" ;;
    openai)      export OPENAI_API_KEY="$API_KEY" ;;
    google)      export GOOGLE_API_KEY="$API_KEY" ;;
    groq)        export GROQ_API_KEY="$API_KEY" ;;
    openrouter)  export OPENROUTER_API_KEY="$API_KEY" ;;
    *)           export OPENAI_API_KEY="$API_KEY" ;;
esac

echo ">>> Running OpenCode autofix  model=$PROVIDER/$MODEL"

# Append build verification instruction if provided
AUTOFIX_EXTRA=""
if [ -n "$BUILD_CMD" ]; then
    AUTOFIX_EXTRA="
If the build error is a C# compilation error in the library code:
  Fix it, then run: $BUILD_CMD
  DO NOT return until this command succeeds (exit code 0).

If the error is NOT library code (signing, credentials, SDK, environment):
  Either fix the issue, or create a file called .autofix-needs-user.md
  explaining what the user needs to do. Do NOT modify any other files in that case."
fi

# OpenCode model format is provider/model
opencode run \
    -m "$PROVIDER/$MODEL" \
    --dangerously-skip-permissions \
    "${PROMPT}${AUTOFIX_EXTRA}"
