#!/usr/bin/env bash
# GitHub Actions 自动发布（.github/workflows/release.yml）的辅助步骤；打包和发布本身由 tools/ship.sh 完成。
#   plan  决定这次 push 要不要发布：build.json 的 version 在远端还没有 tag v<version> 就发布，
#         结果写进 ${GITHUB_OUTPUT}（release / version / notes）
# 用法：tools/ci_release.sh plan
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
die() { echo "::error::$*" >&2; exit 1; }
field() { python3 -c 'import json,sys; v=json.load(open(sys.argv[2], encoding="utf-8")).get(sys.argv[1], ""); print(str(v).lower() if isinstance(v, bool) else v)' "$1" "$2"; }
output() { echo "$1=$2" >> "${GITHUB_OUTPUT:-/dev/stdout}"; }

plan() {
	local version notes
	version="$(field version "$ROOT/build.json")"
	notes="${NOTES_OVERRIDE:-$(field notes "$ROOT/build.json")}"
	[ -n "$version" ] || die "build.json 缺少 version"
	output version "$version"
	if git -C "$ROOT" ls-remote --exit-code --tags origin "refs/tags/v$version" >/dev/null 2>&1; then
		echo "远端已经有 tag v${version}：版本号没变，不发布"
		output release false
		return 0
	fi
	[ -n "$notes" ] || die "build.json 缺少 notes（一句话版本说明）"
	case "$notes" in *$'\n'*) die "notes 只能有一行" ;; esac
	echo "发布 v${version}：$notes"
	output release true
	output notes "$notes"
}

case "${1:-}" in
	plan) plan ;;
	*) sed -n '2,5p' "$0"; exit 2 ;;
esac
