#!/usr/bin/env bash
# Compares golden state-hash files from different machines (plan 9.7, 9.8).
# Each file is a Headless~/out/golden-hashes.<os>-<arch>.txt written by the
# golden runner: one "<file> [<mode>]: <hash>" line per golden run.
#
#   ./Headless~/compare-hashes.sh golden-hashes.linux-x64.txt golden-hashes.macos-arm64.txt ...
#
# Every file is compared with the first. Prints the lines that differ and
# exits 1 if any do (including runs present in only one file), 0 otherwise.
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 <reference-hashes.txt> <other-hashes.txt>..." >&2
  exit 2
fi

reference="$1"
shift
status=0
for other in "$@"; do
  if ! diff -u --label "$reference" --label "$other" <(sort "$reference") <(sort "$other"); then
    status=1
  fi
done
if [ "$status" -eq 0 ]; then
  echo "All $(( $# + 1 )) hash files match ($(wc -l < "$reference") runs)."
fi
exit "$status"
