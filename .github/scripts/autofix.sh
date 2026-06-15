#!/usr/bin/env bash
# AppMare AutoFix — runs OpenCode CLI to fix build errors
# Usage: bash autofix.sh --provider <provider> --model <model> --api-key <key> --prompt "<text>"
set -euo pipefail

PROVIDER=""
MODEL=""
API_KEY=""
PROMPT=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --provider)  PROVIDER="$2";  shift 2 ;;
        --model)     MODEL="$2";     shift 2 ;;
        --api-key)   API_KEY="$2";   shift 2 ;;
        --prompt)    PROMPT="$2";    shift 2 ;;
        *) echo "Unknown argument: $1"; exit 1 ;;
    esac
done

: "${PROVIDER:?--provider is required (e.g. anthropic, openai, google, groq)}"
: "${MODEL:?--model is required (e.g. claude-sonnet-4-5, gpt-4o)}"
: "${API_KEY:?--api-key is required}"
: "${PROMPT:?--prompt is required}"

# Map provider → correct env var that OpenCode expects
case "$PROVIDER" in
    anthropic)   export ANTHROPIC_API_KEY="$API_KEY" ;;
    openai)      export OPENAI_API_KEY="$API_KEY" ;;
    google)      export GOOGLE_API_KEY="$API_KEY" ;;
    groq)        export GROQ_API_KEY="$API_KEY" ;;
    openrouter)  export OPENROUTER_API_KEY="$API_KEY" ;;
    *)           export OPENAI_API_KEY="$API_KEY" ;;
esac

# Install OpenCode if not present
if ! command -v opencode &>/dev/null; then
    echo ">>> Installing OpenCode CLI..."
    curl -fsSL https://opencode.ai/install | bash
fi

export PATH="$HOME/.opencode/bin:$HOME/.local/bin:$PATH"

echo ">>> Running OpenCode autofix  model=$PROVIDER/$MODEL"

# OpenCode model format is provider/model
opencode run \
    -m "$PROVIDER/$MODEL" \
    --dangerously-skip-permissions \
    "$PROMPT"
