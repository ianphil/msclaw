#!/usr/bin/env bash
set -euo pipefail

BASE_URL="http://localhost:5050"
SESSION_ID=""

clear
printf "\033[36m=== Chat Client (localhost:5050/chat) ===\033[0m\n"
printf "\033[90mType your message and press Enter. Type 'quit' to exit.\033[0m\n"
echo ""

while true; do
    printf "\033[32mYou: \033[0m"
    read -r input_msg
    if [ "$input_msg" = "quit" ]; then break; fi
    if [ -z "$input_msg" ]; then continue; fi

    # Build JSON payload
    if [ -n "$SESSION_ID" ]; then
        body=$(jq -n --arg msg "$input_msg" --arg sid "$SESSION_ID" \
            '{message: $msg, sessionId: $sid}')
    else
        body=$(jq -n --arg msg "$input_msg" '{message: $msg}')
    fi

    # Fire request in background so we can animate
    tmpfile=$(mktemp)
    curl -s -w "\n%{http_code}" -X POST "$BASE_URL/chat" \
        -H "Content-Type: application/json" \
        -d "$body" \
        --max-time 120 \
        -o "$tmpfile" 2>/dev/null &
    curl_pid=$!

    # Animated ellipsis while waiting
    frames=('.  ' '.. ' '...')
    i=0
    while kill -0 "$curl_pid" 2>/dev/null; do
        printf "\r\033[90m%s\033[0m" "${frames[$((i % ${#frames[@]}))]}"
        i=$((i + 1))
        sleep 0.4
    done
    printf "\r   \r"

    wait "$curl_pid"
    curl_exit=$?

    if [ $curl_exit -ne 0 ]; then
        printf "\033[31mError: curl request failed (exit code %d)\033[0m\n\n" "$curl_exit"
        rm -f "$tmpfile"
        continue
    fi

    resp=$(cat "$tmpfile")
    rm -f "$tmpfile"

    # Capture sessionId for subsequent requests
    sid=$(echo "$resp" | jq -r '.sessionId // empty' 2>/dev/null)
    if [ -n "$sid" ]; then
        SESSION_ID="$sid"
    fi

    # Extract reply from various possible response fields
    reply=$(echo "$resp" | jq -r '
        if .response then .response
        elif .message then .message
        elif .reply then .reply
        elif .content then .content
        elif .text then .text
        else . | tostring
        end
    ' 2>/dev/null)

    if [ -z "$reply" ]; then
        reply="$resp"
    fi

    printf "\033[33mBot: \033[0m%s\n\n" "$reply"
done

printf "\033[36mGoodbye!\033[0m\n"
