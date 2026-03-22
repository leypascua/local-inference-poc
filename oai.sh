#!/usr/bin/env bash

set -euo pipefail

# Load .env file if it exists in the current directory
if [[ -f ".env" ]]; then
  # shellcheck source=/dev/null
  source ".env"
fi

BASE_URL="${OPENAI_BASE_URL:-http://localhost:8000}"
MODEL="${OPENAI_MODEL:-}"
API_KEY="${OPENAI_API_KEY:-}"

usage() {
  cat <<EOF
Usage: $0 --input image.jpg --prompt instructions.md [OPTIONS]

Options:
  --input PATH    Input document image, PDF, or HTTP/S URL (required)
  --prompt PATH   Prompt text file (required)
  --schema PATH   JSON schema file for structured output
  --dpi N         DPI for PDF rasterization (default: 150, lower uses less memory)
  --quality N     JPEG quality for PDF pages (default: 85, lower uses less memory)
  --help          Show this help message

Environment:
  OPENAI_BASE_URL   Base URL for the OpenAI-compatible API server (default: http://localhost:8000)
  OPENAI_MODEL      Model name (required)
  OPENAI_API_KEY    Optional bearer token
                    
  Environment variables can also be set in a .env file in the current directory.
EOF
}

die() {
  printf 'Error: %s\n' "$1" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || die "Missing required command: $1"
}

mime_type_for_file() {
  local path="$1"
  local mime

  if command -v file >/dev/null 2>&1; then
    mime="$(file --brief --mime-type "$path")"
  else
    case "${path##*.}" in
      jpg|jpeg|JPG|JPEG) mime="image/jpeg" ;;
      png|PNG) mime="image/png" ;;
      webp|WEBP) mime="image/webp" ;;
      gif|GIF) mime="image/gif" ;;
      pdf|PDF) mime="application/pdf" ;;
      *) die "Could not determine MIME type for '$path'. Install 'file' or use a supported extension." ;;
    esac
  fi

  case "$mime" in
    image/jpeg|image/png|image/webp|image/gif|application/pdf) ;;
    *) die "Unsupported input MIME type: $mime" ;;
  esac

  printf '%s' "$mime"
}

INPUT_PATH=""
PROMPT_PATH=""
SCHEMA_PATH=""
PDF_DPI="150"
JPEG_QUALITY="85"

while (($# > 0)); do
  case "$1" in
    --input)
      (($# >= 2)) || die "--input requires a value"
      INPUT_PATH="$2"
      shift 2
      ;;
    --prompt)
      (($# >= 2)) || die "--prompt requires a value"
      PROMPT_PATH="$2"
      shift 2
      ;;
    --schema)
      (($# >= 2)) || die "--schema requires a value"
      SCHEMA_PATH="$2"
      shift 2
      ;;
    --dpi)
      (($# >= 2)) || die "--dpi requires a value"
      PDF_DPI="$2"
      shift 2
      ;;
    --quality)
      (($# >= 2)) || die "--quality requires a value"
      JPEG_QUALITY="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      die "Unknown argument: $1"
      ;;
  esac
done

[[ -n "$INPUT_PATH" ]] || die "--input is required"
[[ -n "$PROMPT_PATH" ]] || die "--prompt is required"
[[ -n "$MODEL" ]] || die "OPENAI_MODEL environment variable is required"

DOWNLOADED_FILE=""
if [[ "$INPUT_PATH" =~ ^https?:// ]]; then
  URL_EXT="${INPUT_PATH##*.}"
  DOWNLOADED_FILE="$(mktemp --suffix=".$URL_EXT")"
  curl -sS -L -o "$DOWNLOADED_FILE" "$INPUT_PATH" || die "Failed to download: $INPUT_PATH"
  INPUT_PATH="$DOWNLOADED_FILE"
fi

[[ -f "$INPUT_PATH" ]] || die "Input file not found: $INPUT_PATH"
[[ -f "$PROMPT_PATH" ]] || die "Prompt file not found: $PROMPT_PATH"
if [[ -n "$SCHEMA_PATH" ]]; then
  [[ -f "$SCHEMA_PATH" ]] || die "Schema file not found: $SCHEMA_PATH"
fi

require_command curl
require_command python3

INPUT_MIME_TYPE="$(mime_type_for_file "$INPUT_PATH")"

if [[ "$INPUT_MIME_TYPE" == "application/pdf" ]]; then
  require_command pdftoppm
fi

AUTH_ARGS=()
if [[ -n "$API_KEY" ]]; then
  AUTH_ARGS=(-H "Authorization: Bearer $API_KEY")
fi

RESPONSE_FILE="$(mktemp)"
PAGES_DIR=""

if [[ "$INPUT_MIME_TYPE" == "application/pdf" ]]; then
  PAGES_DIR="$(mktemp -d)"
  trap 'rm -rf "$PAGES_DIR" "$RESPONSE_FILE"; rm -f "$DOWNLOADED_FILE"' EXIT
  pdftoppm -jpeg -jpegopt "quality=$JPEG_QUALITY" -r "$PDF_DPI" "$INPUT_PATH" "$PAGES_DIR/page"
  INPUT_PATH="$PAGES_DIR"
  INPUT_MIME_TYPE="image/jpeg"
else
  trap 'rm -f "$RESPONSE_FILE" "$DOWNLOADED_FILE"' EXIT
fi

python3 - "$INPUT_PATH" "$INPUT_MIME_TYPE" "$PROMPT_PATH" "$SCHEMA_PATH" "$MODEL" <<'PY' \
| curl -sS -X POST "${BASE_URL%/}/v1/chat/completions" \
    -H "Content-Type: application/json" \
    "${AUTH_ARGS[@]}" \
    --data-binary @- \
    > "$RESPONSE_FILE"
import base64
import json
import mimetypes
import pathlib
import sys


def read_text(path_str: str) -> str:
    return pathlib.Path(path_str).read_text(encoding="utf-8").strip()


input_path = pathlib.Path(sys.argv[1])
input_mime_type = sys.argv[2] or mimetypes.guess_type(str(input_path))[0] or "application/octet-stream"
prompt_path = pathlib.Path(sys.argv[3])
schema_arg = sys.argv[4]
model_name = sys.argv[5]

prompt_text = read_text(str(prompt_path))
if not prompt_text:
    raise SystemExit("Prompt file is empty")

def build_image_content():
    if input_path.is_dir():
        pages = sorted(input_path.glob("*.jpg"), key=lambda p: int(p.stem.split("-")[-1]))
        if not pages:
            raise SystemExit(f"No JPEG pages found in {input_path}")
        content = []
        for i, page in enumerate(pages, 1):
            page_b64 = base64.b64encode(page.read_bytes()).decode("ascii")
            content.append({
                "type": "image_url",
                "image_url": {"url": f"data:image/jpeg;base64,{page_b64}"},
            })
        return content
    else:
        input_b64 = base64.b64encode(input_path.read_bytes()).decode("ascii")
        data_url = f"data:{input_mime_type};base64,{input_b64}"
        return [{"type": "image_url", "image_url": {"url": data_url}}]

image_content = build_image_content()
file_desc = f"{len(image_content)} pages from PDF" if input_path.is_dir() else input_path.name

system_prompt = prompt_text + "\n\nReturn JSON only. Do not wrap the JSON in markdown code fences."

body = {
    "model": model_name,
    "temperature": 0,
    "messages": [
        {
            "role": "system",
            "content": system_prompt,
        },
        {
            "role": "user",
            "content": [
                {
                    "type": "text",
                    "text": f"Process {file_desc} and return the extracted data as JSON.",
                },
            ] + image_content,
        },
    ],
}

if schema_arg:
    schema_text = read_text(schema_arg)
    if not schema_text:
        raise SystemExit("Schema file is empty")
    schema_wrapper = json.loads(schema_text)
    if "schema" in schema_wrapper:
        body["response_format"] = {
            "type": "json_schema",
            "json_schema": schema_wrapper,
        }
    else:
        body["response_format"] = {
            "type": "json_schema",
            "json_schema": {
                "name": "extracted_data",
                "strict": True,
                "schema": schema_wrapper,
            },
        }

print(json.dumps(body))
PY

python3 - "$RESPONSE_FILE" <<'PY'
import json
import pathlib
import sys


def fail(message: str, payload: dict | None = None) -> None:
    print(f"Error: {message}", file=sys.stderr)
    if payload:
        print(f"Full response: {json.dumps(payload, indent=2)}", file=sys.stderr)
    raise SystemExit(1)


response_path = pathlib.Path(sys.argv[1])

try:
    payload = json.loads(response_path.read_text(encoding="utf-8"))
except json.JSONDecodeError as exc:
    fail(f"Response was not valid JSON: {exc}")

if "error" in payload:
    fail(f"API error: {payload['error']}", payload)

choices = payload.get("choices")
if not isinstance(choices, list) or not choices:
    fail("Response did not include choices", payload)

message = choices[0].get("message")
if not isinstance(message, dict):
    fail("Response did not include choices[0].message")

content = message.get("content")
if isinstance(content, str):
    print(content)
    raise SystemExit(0)

if isinstance(content, list):
    text_parts = []
    for part in content:
        if isinstance(part, dict) and part.get("type") == "text":
            text = part.get("text")
            if isinstance(text, str):
                text_parts.append(text)
    if text_parts:
        print("".join(text_parts))
        raise SystemExit(0)

fail("Assistant content was empty or unsupported")
PY
