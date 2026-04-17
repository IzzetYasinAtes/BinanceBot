#!/usr/bin/env bash
# BinanceBot hook trace script — jq yerine node kullanır (portable).
# Çağrı: bash trace.sh <kind>
#   kind: user-prompt | tool-use | subagent-stop | session-end

set -u

# cwd-independent: Claude Code resmi env var'ı, repo root'a geç
cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || true

KIND="${1:-unknown}"
TRACE_DIR=".ai-trace"
DATE="$(date -u +%F)"

mkdir -p "$TRACE_DIR/sessions" "$TRACE_DIR/tool-calls" "$TRACE_DIR/subagent-stops"

# stdin JSON'u oku — Claude Code hook payload'unu stdin'de verir
PAYLOAD="$(cat 2>/dev/null || true)"

# node ile JSON-wrap + dosyaya append (jq bağımlılığı yok)
wrap_and_append() {
  local target_file="$1"
  local record_kind="$2"
  node -e '
    const fs = require("fs");
    const kind = process.argv[1];
    const rawStr = process.argv[2];
    let raw;
    try { raw = rawStr ? JSON.parse(rawStr) : null; } catch (e) { raw = { raw_text: rawStr }; }
    const rec = { ts: new Date().toISOString(), kind: kind, payload: raw };
    fs.appendFileSync(process.argv[3], JSON.stringify(rec) + "\n");
  ' "$record_kind" "$PAYLOAD" "$target_file"
}

case "$KIND" in
  user-prompt)
    wrap_and_append "$TRACE_DIR/sessions/$DATE.jsonl" "user_prompt"
    ;;
  tool-use)
    wrap_and_append "$TRACE_DIR/tool-calls/$DATE.jsonl" "tool_use"
    ;;
  subagent-stop)
    # Subagent özetini MD dosyasına yaz + handoffs.jsonl'a index satırı at
    AGENT="$(node -e '
      let raw;
      try { raw = JSON.parse(process.argv[1] || "null"); } catch (e) { raw = null; }
      const a = raw && (raw.subagent_name || raw.agent_type || raw.agent || (raw.tool_input && raw.tool_input.subagent_type)) || "unknown";
      process.stdout.write(String(a));
    ' "$PAYLOAD" 2>/dev/null || echo "unknown")"
    SID="$(node -e '
      let raw;
      try { raw = JSON.parse(process.argv[1] || "null"); } catch (e) { raw = null; }
      const s = raw && (raw.session_id || raw.sessionId) || Math.random().toString(36).slice(2,10);
      process.stdout.write(String(s).slice(0,8));
    ' "$PAYLOAD" 2>/dev/null || echo "unknown")"
    STOP_FILE="$TRACE_DIR/subagent-stops/${DATE}_${AGENT}_${SID}.md"
    {
      echo "# $AGENT"
      echo ""
      echo "- ts: $(date -u +%FT%TZ)"
      echo "- agent: $AGENT"
      echo "- session: $SID"
      echo ""
      echo "## Payload"
      echo ""
      echo '```json'
      echo "$PAYLOAD"
      echo '```'
    } > "$STOP_FILE"
    wrap_and_append "$TRACE_DIR/handoffs.jsonl" "subagent_stop"
    ;;
  session-end)
    wrap_and_append "$TRACE_DIR/sessions/$DATE.jsonl" "session_end"
    ;;
  *)
    wrap_and_append "$TRACE_DIR/sessions/$DATE.jsonl" "unknown"
    ;;
esac

exit 0
