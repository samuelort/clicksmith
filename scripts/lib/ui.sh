# shellcheck shell=bash
# Clicksmith terminal UI: forge animation, banner, step loader and finale.
# Works with the macOS system bash (3.2). Falls back to plain text when output isn't a
# terminal, when NO_COLOR is set, or with --no-anim / CLICKSMITH_NO_ANIM=1.
# Note for bash 3.2: always write ${var} before non-ASCII characters (e.g. ${out}█).

UI_LOG="${UI_LOG:-$HOME/Library/Logs/Clicksmith-install.log}"
UI_I=0
UI_TOTAL=0
UI_PID=""
UI_START=$SECONDS
UI_DRAWN=0
UI_TMP="$(mktemp -d)"

UI_TTY=0
[ -t 1 ] && [ "${TERM:-dumb}" != "dumb" ] && UI_TTY=1
UI_ANIM=$UI_TTY
[ "${CLICKSMITH_NO_ANIM:-0}" = "1" ] && UI_ANIM=0

if [ "$UI_TTY" = 1 ] && [ -z "${NO_COLOR:-}" ]; then
  R=$'\033[0m'; B=$'\033[1m'; D=$'\033[2m'
  W=$'\033[97m'; G=$'\033[38;5;245m'; DG=$'\033[38;5;240m'
  # Nordic palette (1.2.3): sand → copper, sage for success, clay for errors
  O=$'\033[38;5;173m'; Y=$'\033[38;5;180m'; BR=$'\033[38;5;137m'
  OK=$'\033[38;5;108m'; ERR=$'\033[38;5;167m'
  GRAD1=$'\033[38;5;223m'; GRAD2=$'\033[38;5;180m'; GRAD3=$'\033[38;5;174m'; GRAD4=$'\033[38;5;173m'; GRAD5=$'\033[38;5;137m'
else
  R=""; B=""; D=""; W=""; G=""; DG=""; O=""; Y=""; BR=""; OK=""; ERR=""
  GRAD1=""; GRAD2=""; GRAD3=""; GRAD4=""; GRAD5=""
fi

ui_cols() { local c; c=$(tput cols 2>/dev/null || true); [ -n "$c" ] && [ "$c" -gt 0 ] 2>/dev/null && echo "$c" || echo 80; }

ui_cleanup() {
  [ -n "$UI_PID" ] && kill "$UI_PID" 2>/dev/null
  [ "$UI_TTY" = 1 ] && printf '\033[?25h'
  rm -rf "$UI_TMP"
}
ui_interrupt() {
  ui_cleanup
  printf '\n\n  %s✋ Installation cancelled.%s Nothing was half-installed that a re-run won'"'"'t fix.\n\n' "$Y" "$R"
  exit 130
}
trap ui_cleanup EXIT
trap ui_interrupt INT TERM

ui_fmt_time() {
  local s=$1
  if [ "$s" -lt 60 ]; then printf '%ds' "$s"; else printf '%dm %02ds' $((s / 60)) $((s % 60)); fi
}

ui__base() {
  FRAME[6]="$W"'        |       |'"$R"
  FRAME[7]="$G"'    ____'"$W"'\_______/'"$G"'____'"$R"
  FRAME[8]="$G"'    \_______________/'"$R"
  FRAME[9]="$DG"'         |     |'"$R"
  FRAME[10]="$DG"'        /_______\'"$R"
}
ui__frame_raised() {
  FRAME=()
  FRAME[0]="$G"'                         _______'"$R"
  FRAME[1]="$G"'                        |_______|'"$R"
  FRAME[2]="$BR"'                           | |'"$R"
  FRAME[3]="$W"'          .---.'"$BR"'            | |'"$R"
  FRAME[4]="$W"'         /  |  \'"$BR"'           | |'"$R"
  FRAME[5]="$W"'        |   |   |'"$BR"'          |_|'"$R"
  ui__base
}
ui__frame_swing() {
  FRAME=()
  FRAME[0]=''
  FRAME[1]="$G"'                    _______'"$R"
  FRAME[2]="$G"'                   |_______|'"$BR"'========='"$R"
  FRAME[3]="$W"'          .---.'"$R"
  FRAME[4]="$W"'         /  |  \'"$R"
  FRAME[5]="$W"'        |   |   |'"$R"
  ui__base
}
ui__frame_strike() {
  FRAME=()
  if [ "$1" = 1 ]; then
    FRAME[0]="$Y"'       *    .   *    .   *'"$R"
  else
    FRAME[0]="$O"'     .    *   .    *    .   *'"$R"
  fi
  FRAME[1]="$O"'    *   '"$G"'  _______'"$O"'   .    *'"$R"
  FRAME[2]="$Y"'   -  - '"$G"' |_______|'"$BR"'============'"$R"
  FRAME[3]="$O"'     *  . '"$W"'.---.'"$O"' .  *'"$R"
  FRAME[4]="$W"'         /  '"$O"'|'"$W"'  \'"$R"
  FRAME[5]="$W"'        |   '"$O"'|'"$W"'   |'"$R"
  ui__base
}
ui__frame_glow() {
  ui__frame_raised
  FRAME[3]="$W"'          .'"$O"'-*-'"$W"'.'"$BR"'            | |'"$R"
  FRAME[4]="$W"'         /  '"$O"'|'"$W"'  \'"$BR"'           | |'"$R"
  FRAME[5]="$W"'        |   '"$O"'|'"$W"'   |'"$BR"'          |_|'"$R"
}
ui__draw() {
  [ "$UI_DRAWN" = 1 ] && printf '\033[%dA' "${#FRAME[@]}"
  local l
  for l in "${FRAME[@]}"; do printf '\r\033[K  %s\n' "$l"; done
  UI_DRAWN=1
}

ui_forge_animation() {
  if [ "$UI_ANIM" != 1 ]; then ui__frame_glow; ui__draw; return; fi
  printf '\033[?25l'
  local strike
  for strike in 1 2 3; do
    ui__frame_raised; ui__draw; sleep 0.32
    ui__frame_swing;  ui__draw; sleep 0.09
    ui__frame_strike 1; ui__draw; sleep 0.14
    ui__frame_strike 2; ui__draw; sleep 0.18
  done
  ui__frame_glow; ui__draw
  printf '\033[?25h'
}

ui_banner() {
  echo
  if [ "$(ui_cols)" -ge 64 ]; then
    ui_forge_animation
    echo
    printf '  %s%s%s\n' "$GRAD1$B" ' ████ ██    ██  ████ ██  ██  ████ ██   ██ ██ ██████ ██  ██' "$R"
    printf '  %s%s%s\n' "$GRAD2$B" '██    ██    ██ ██    ██ ██  ██    ███ ███ ██   ██   ██  ██' "$R"
    printf '  %s%s%s\n' "$GRAD3$B" '██    ██    ██ ██    ████    ███  ██ █ ██ ██   ██   ██████' "$R"
    printf '  %s%s%s\n' "$GRAD4$B" '██    ██    ██ ██    ██ ██     ██ ██   ██ ██   ██   ██  ██' "$R"
    printf '  %s%s%s\n' "$GRAD5$B" ' ████ █████ ██  ████ ██  ██ ████  ██   ██ ██   ██   ██  ██' "$R"
  else
    printf '  %s⚒  CLICKSMITH%s\n' "$O$B" "$R"
  fi
  echo
  printf '  %sForge every mouse button into something useful.%s\n' "$D" "$R"
  printf '  %s%s%s\n\n' "$G" "$1" "$R"
}

UI_QUIPS=("heating the forge" "teaching the mouse new tricks" "hammering out bugs" "polishing every pixel"
          "convincing the compiler" "aligning the sparks" "tempering the steel" "counting mouse buttons")

ui__bar() { # $1 = completed steps; 12 cells
  local filled=$(( $1 * 12 / (UI_TOTAL > 0 ? UI_TOTAL : 1) )) i out=""
  for ((i = 0; i < 12; i++)); do
    if [ $i -lt $filled ]; then out="${out}█"; else out="${out}░"; fi
  done
  printf '%s' "$out"
}

ui_log_header() {
  mkdir -p "$(dirname "$UI_LOG")"
  { echo "=== Clicksmith $1 — $(date) ==="; sw_vers 2>/dev/null || true; uname -m || true; } > "$UI_LOG"
  chmod 600 "$UI_LOG" 2>/dev/null || true
}

ui_step() {
  local label="$1"; shift
  UI_I=$((UI_I + 1))
  local start=$SECONDS counter
  counter=$(printf '%2d/%d' "$UI_I" "$UI_TOTAL")
  printf '\n### [%s] %s\n' "$counter" "$label" >> "$UI_LOG"

  if [ "$UI_TTY" != 1 ]; then
    printf '  [%s] %s… ' "$counter" "$label"
    if "$@" >> "$UI_LOG" 2>&1; then
      printf 'done (%s)\n' "$(ui_fmt_time $((SECONDS - start)))"; return 0
    fi
    printf 'FAILED\n'; ui__fail "$label"
  fi

  "$@" >> "$UI_LOG" 2>&1 &
  UI_PID=$!
  local spin=("⠋" "⠙" "⠹" "⠸" "⠼" "⠴" "⠦" "⠧" "⠇" "⠏") n=0 quip="" cols el
  cols=$(ui_cols)
  printf '\033[?25l'
  while kill -0 "$UI_PID" 2>/dev/null; do
    el=$((SECONDS - start))
    quip=""
    if [ "$cols" -ge 100 ] && [ $el -ge 3 ]; then
      quip="${D}· ${UI_QUIPS[$(( (el / 4) % ${#UI_QUIPS[@]} ))]}…${R}"
    fi
    printf '\r\033[K  %s%s%s %s%s%s  %s  %-30s %s%6s%s  %s' \
      "$O" "${spin[$((n % 10))]}" "$R" "$O" "$(ui__bar $((UI_I - 1)))" "$R" "$counter" "$label" "$G" "$(ui_fmt_time $el)" "$R" "$quip"
    n=$((n + 1))
    sleep 0.08
  done
  printf '\033[?25h'
  if wait "$UI_PID"; then
    UI_PID=""
    printf '\r\033[K  %s✔%s %s%s%s  %s  %-30s %s%6s%s\n' \
      "$OK" "$R" "$OK" "$(ui__bar "$UI_I")" "$R" "$counter" "$label" "$G" "$(ui_fmt_time $((SECONDS - start)))" "$R"
    return 0
  fi
  UI_PID=""
  printf '\r\033[K  %s✘%s %s  %s%-30s%s\n' "$ERR" "$R" "$counter" "$ERR$B" "$label" "$R"
  ui__fail "$label"
}

ui__fail() {
  printf '\n  %sThe forge went cold at “%s”.%s Last lines of the log:\n\n' "$ERR$B" "$1" "$R"
  tail -n 25 "$UI_LOG" | sed 's/^/    │ /'
  printf '\n  Full log: %s\n' "$UI_LOG"
  printf '  Paste the lines above when asking for help. Nothing else was changed.\n\n'
  exit 1
}

ui_note() { printf '      %s↳ %s%s\n' "$G" "$1" "$R"; }

ui_finale() {
  local total; total=$(ui_fmt_time $((SECONDS - UI_START)))
  echo
  printf '  %s%s%s\n' "$Y" '            .   *   .' "$R"
  printf '  %s%s%s\n' "$O" '         *   \  |  /   *' "$R"
  printf '  %s%s%s%s%s%s%s\n' "$O" '      .   --- ' "$W" '.-*-.' "$O" ' ---   .' "$R"
  printf '  %s%s%s%s%s%s%s\n' "$W" '         /  ' "$O" '|' "$W" '  \' "$R"
  printf '  %s%s%s%s%s%s%s\n' "$W" '        |   ' "$O" '|' "$W" '   |' "$R"
  printf '  %s%s%s\n' "$W" '        |       |' "$R"
  printf '  %s%s%s\n\n' "$W" '         \_____/' "$R"
  printf '  %s✔ %s%s  %s(%s)%s\n\n' "$OK$B" "$1" "$R" "$G" "$total" "$R"
}

ui_box() {
  local title="$1"; shift
  local w=60 line
  printf '  %s╭%s╮%s\n' "$G" "$(printf '─%.0s' $(seq 1 $((w + 2))))" "$R"
  printf '  %s│%s %s%-*s%s %s│%s\n' "$G" "$R" "$B" "$w" "$title" "$R" "$G" "$R"
  for line in "$@"; do
    printf '  %s│%s %-*s %s│%s\n' "$G" "$R" "$w" "$line" "$G" "$R"
  done
  printf '  %s╰%s╯%s\n\n' "$G" "$(printf '─%.0s' $(seq 1 $((w + 2))))" "$R"
}
