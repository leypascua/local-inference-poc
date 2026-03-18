#!/usr/bin/env bash

set -euo pipefail

API_BASE=${API_BASE:-http://localhost:9998}
MAX_WAIT_SECONDS=${MAX_WAIT_SECONDS:-120}
POLL_INTERVAL_SECONDS=${POLL_INTERVAL_SECONDS:-2}

INPUT_FILE=${INPUT_FILE:-}

is_remote_input() {
  case "$1" in
    http://*|https://*) return 0 ;;
    *) return 1 ;;
  esac
}

input_name_for() {
  local input_path trimmed

  input_path=$1
  trimmed=${input_path%%#*}
  trimmed=${trimmed%%\?*}
  trimmed=${trimmed##*/}

  if [ -n "$trimmed" ]; then
    printf '%s\n' "$trimmed"
  else
    printf 'remote-input\n'
  fi
}

usage() {
  cat <<'EOF'
Usage: ./run-ocr.sh --input FILE_OR_URL

Environment overrides:
  API_BASE=http://localhost:9998
  MAX_WAIT_SECONDS=120
  POLL_INTERVAL_SECONDS=2

Examples:
  ./run-ocr.sh --input ./sample-invoice.jpg
  ./run-ocr.sh --input ./sample-invoice.pdf
  ./run-ocr.sh --input https://example.com/invoice.jpg
  API_BASE=http://localhost:9998 ./run-ocr.sh --input ./invoice.jpg
EOF
}

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    printf 'Missing required command: %s\n' "$1" >&2
    exit 1
  fi
}

cleanup() {
  rm -f "${FILE_URL_FILE:-}" "${PAYLOAD_FILE:-}" "${RESPONSE_FILE:-}"
}

mime_type_for() {
  case "$1" in
    *.jpg|*.jpeg|*.JPG|*.JPEG) printf 'image/jpeg\n' ;;
    *.png|*.PNG) printf 'image/png\n' ;;
    *.gif|*.GIF) printf 'image/gif\n' ;;
    *.pdf|*.PDF) printf 'application/pdf\n' ;;
    *)
      printf 'Unsupported file type for %s\n' "$1" >&2
      exit 1
      ;;
  esac
}

wait_for_server() {
  local deadline now status

  deadline=$((SECONDS + MAX_WAIT_SECONDS))

  while :; do
    status=$(curl -s -o /dev/null -w '%{http_code}' "$API_BASE/health" || true)
    if [ "$status" = "200" ]; then
      return 0
    fi

    now=$SECONDS
    if [ "$now" -ge "$deadline" ]; then
      printf 'Timed out waiting for %s/health (last status: %s)\n' "$API_BASE" "$status" >&2
      exit 1
    fi

    sleep "$POLL_INTERVAL_SECONDS"
  done
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --input)
      if [ "$#" -lt 2 ]; then
        printf 'Missing value for --input\n' >&2
        usage >&2
        exit 1
      fi
      INPUT_FILE=$2
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      printf 'Unknown argument: %s\n' "$1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [ -z "$INPUT_FILE" ]; then
  printf 'Missing required argument: --input FILE_OR_URL\n' >&2
  usage >&2
  exit 1
fi

require_command curl
require_command jq
require_command mktemp

trap cleanup EXIT

INPUT_NAME=$(input_name_for "$INPUT_FILE")
MIME_TYPE=$(mime_type_for "$INPUT_NAME")
FILE_URL_FILE=$(mktemp)
PAYLOAD_FILE=$(mktemp)
RESPONSE_FILE=$(mktemp)

if is_remote_input "$INPUT_FILE"; then
  printf '%s' "$INPUT_FILE" > "$FILE_URL_FILE"
else
  require_command base64
  require_command tr

  if [ ! -f "$INPUT_FILE" ]; then
    printf 'Input file not found: %s\n' "$INPUT_FILE" >&2
    exit 1
  fi

  base64 < "$INPUT_FILE" | tr -d '\n' > "$FILE_URL_FILE"
fi

wait_for_server

jq -n \
  --arg file_name "$INPUT_NAME" \
  --arg mime_type "$MIME_TYPE" \
  --rawfile file_url "$FILE_URL_FILE" \
  '{
    content: [
      {
        file_url: $file_url,
        name: $file_name,
        content_type: $mime_type
      }
    ]
  }' > "$PAYLOAD_FILE"

HTTP_STATUS=$(curl -sS \
  -o "$RESPONSE_FILE" \
  -w '%{http_code}' \
  -X POST "$API_BASE/invoices/extract/" \
  -H 'Content-Type: application/json' \
  --data-binary "@$PAYLOAD_FILE")

if [ "$HTTP_STATUS" -lt 200 ] || [ "$HTTP_STATUS" -ge 300 ]; then
  if jq -e '.error' >/dev/null 2>&1 "$RESPONSE_FILE"; then
    jq -r '.error.message' "$RESPONSE_FILE" >&2
  else
    printf 'Request failed with HTTP %s\n' "$HTTP_STATUS" >&2
    jq . "$RESPONSE_FILE" >&2 || true
  fi
  exit 1
fi

CONTENT=$(jq -c '.response // empty' "$RESPONSE_FILE")

if [ -z "$CONTENT" ]; then
  printf 'No extraction response returned.\n' >&2
  jq . "$RESPONSE_FILE" >&2 || true
  exit 1
fi

jq . <<<"$CONTENT"
