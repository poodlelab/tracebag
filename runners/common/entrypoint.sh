#!/bin/sh
set -eu

fail() {
  echo "tracebag-runner: $1" >&2
  exit 64
}

require_positive_integer() {
  case "$1" in
    ''|*[!0-9]*|0) fail "expected a positive integer" ;;
  esac
}

require_safe_name() {
  case "$1" in
    ''|*[!A-Za-z0-9._-]*) fail "invalid server-owned name" ;;
  esac
}

require_providers() {
  case "$1" in
    ''|*[!A-Za-z0-9.,_\[\]-]*) fail "invalid counter provider list" ;;
  esac
}

collect_counters() {
  pid="$1"
  providers="$2"
  session="$3"
  refresh="$4"
  duration="$5"
  index=0
  child=''

  cleanup_counters() {
    if [ -n "${child}" ]; then
      kill "${child}" 2>/dev/null || true
      wait "${child}" 2>/dev/null || true
    fi
    rm -f "/tmp/tracebag-counters-${session}-"*.csv "/tmp/tracebag-counters-${session}-"*.log
  }

  trap 'cleanup_counters; exit 143' INT TERM
  trap cleanup_counters EXIT

  while true; do
    output="/tmp/tracebag-counters-${session}-${index}.csv"
    status="/tmp/tracebag-counters-${session}-${index}.log"
    rm -f "${output}" "${status}"
    dotnet-counters collect \
      --process-id "${pid}" \
      --counters "${providers}" \
      --refresh-interval "${refresh}" \
      --format csv \
      --output "${output}" \
      --duration "${duration}" >"${status}" 2>&1 &
    child="$!"
    if wait "${child}"; then
      :
    else
      exit_code="$?"
      child=''
      cat "${status}" >&2
      exit "${exit_code}"
    fi
    child=''

    if [ "${index}" -eq 0 ]; then
      cat "${output}"
    else
      tail -n +2 "${output}"
    fi
    rm -f "${output}" "${status}"
    index=$((index + 1))
  done
}

profile="${1:-}"
case "${profile}" in
  processes)
    [ "$#" -eq 1 ] || fail "processes accepts no arguments"
    exec dotnet-counters ps
    ;;

  trace-cpu|trace-threading|trace-contention)
    [ "$#" -eq 4 ] || fail "$profile expects process, duration, and file name"
    require_positive_integer "$2"
    case "$3" in
      [0-9][0-9]:[0-9][0-9]:[0-9][0-9]) ;;
      *) fail "invalid trace duration" ;;
    esac
    require_safe_name "$4"
    case "$profile" in
      trace-cpu)
        # The serviced diagnostics train calls the EventPipe-only sampling
        # profile dotnet-sampled-thread-time. The separate cpu-sampling profile
        # belongs to collect-linux and would require forbidden kernel access.
        exec dotnet-trace collect --process-id "$2" --duration "$3" --profile dotnet-sampled-thread-time --output "/artifacts/$4"
        ;;
      trace-threading)
        exec dotnet-trace collect --process-id "$2" --duration "$3" --providers "Microsoft-Windows-DotNETRuntime:0x10000:4" --output "/artifacts/$4"
        ;;
      trace-contention)
        exec dotnet-trace collect --process-id "$2" --duration "$3" --providers "Microsoft-Windows-DotNETRuntime:0x4000:4" --output "/artifacts/$4"
        ;;
    esac
    ;;

  stack)
    [ "$#" -eq 3 ] || fail "stack expects process and file name"
    require_positive_integer "$2"
    require_safe_name "$3"
    temporary="/tmp/$3"
    trap 'rm -f "${temporary}"' EXIT
    stack_attempt=1
    while [ "${stack_attempt}" -le 3 ]; do
      if dotnet-stack report --process-id "$2" >"${temporary}" \
        && grep -Eq '^(Thread|OS Thread)' "${temporary}" \
        && grep -Eq '^[[:space:]]+.*[.!]' "${temporary}"; then
        cp "${temporary}" "/artifacts/$3"
        exit 0
      fi
      stack_attempt=$((stack_attempt + 1))
      sleep 1
    done
    fail "dotnet-stack produced no recognizable managed stacks after 3 attempts"
    ;;

  gcdump)
    [ "$#" -eq 3 ] || fail "gcdump expects process and file name"
    require_positive_integer "$2"
    require_safe_name "$3"
    exec dotnet-gcdump collect --process-id "$2" --output "/artifacts/$3"
    ;;

  dump-full)
    [ "$#" -eq 3 ] || fail "dump-full expects process and file name"
    require_positive_integer "$2"
    require_safe_name "$3"
    temporary="/tmp/$3"
    trap 'rm -f "${temporary}"' EXIT
    dotnet-dump collect --process-id "$2" --type Full --output "${temporary}"
    cp "${temporary}" "/artifacts/$3"
    ;;

  counter-loop)
    [ "$#" -eq 4 ] || fail "counter-loop expects process, providers, and session"
    require_positive_integer "$2"
    require_providers "$3"
    require_safe_name "$4"
    collect_counters "$2" "$3" "$4" "1" "00:00:03"
    ;;

  counter-recording)
    [ "$#" -eq 6 ] || fail "counter-recording expects process, providers, recording, interval, and chunk duration"
    require_positive_integer "$2"
    require_providers "$3"
    require_safe_name "$4"
    require_positive_integer "$5"
    case "$6" in
      [0-9][0-9]:[0-9][0-9]:[0-9][0-9]) ;;
      *) fail "invalid recording duration" ;;
    esac
    collect_counters "$2" "$3" "$4" "$5" "$6"
    ;;

  *)
    fail "unknown diagnostic profile"
    ;;
esac
