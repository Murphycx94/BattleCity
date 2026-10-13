#!/usr/bin/env bash
# 发布一个已提交的版本：在该提交的干净 worktree 里跑测试、打包客户端（macOS / Windows / Linux）
# 和专用服务器（macOS arm64/x64、Windows x64、Linux x64），再建 GitHub Release。
# - 版本号只写在 build.json；打包时才写进 worktree 里的 project.godot 和 export_presets.cfg，工作区不受影响；
# - 工作区里没提交的改动永远不会进包；产物先放临时目录，全部成功后才换掉 <OUT>/release；
# - github 可以重复运行：Release 已存在且附件一致时只做核对（REPAIR=1 时用本地文件覆盖对不上的附件）。
#
# 用法：ship.sh test                       在干净 worktree 里跑单测和网络压测
#       ship.sh check                      发布前检查（不改任何东西）
#       ship.sh build                      check + test + 打包到 <OUT>/release
#       ship.sh github [发布说明.md]        用 <OUT>/release 建 tag v<version> 和 GitHub Release
#       ship.sh all [发布说明.md]           build + github
#       ship.sh bump <版本> "<一句话说明>"   改 build.json 并提交（不推送；推到 main 后 CI 自动发布）
# 环境变量：REF（默认 HEAD）、GODOT（默认 godot-mono）、SHIP_OUT（默认 build/）、
#       SHIP_SKIP_TESTS=1（build 时不跑测试）、DRY_RUN=1（github 只核对、打印）、REPAIR=1
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GODOT="${GODOT:-godot-mono}"
OUT="${SHIP_OUT:-$ROOT/build}"
REMOTE="origin"
APP="Tank3D"
MAX_NOTES_CHARS=80
SERVER_RIDS=(osx-arm64 osx-x64 win-x64 linux-x64)
REF_SHA="$(git -C "$ROOT" rev-parse --verify "${REF:-HEAD}^{commit}")"
WT=""

die() { echo "✗ $*" >&2; exit 1; }
ok() { echo "✓ $*"; }
step() { echo; echo "==> $*"; }

json_field() {  # json_field <字段> <json 文件或 ->
	python3 -c 'import json,sys; d=json.load(open(sys.argv[2], encoding="utf-8") if sys.argv[2]!="-" else sys.stdin); v=d.get(sys.argv[1], ""); print(str(v).lower() if isinstance(v, bool) else v)' "$1" "$2"
}
at_ref() { git -C "$ROOT" show "$REF_SHA:$1"; }
sha256() { if command -v shasum >/dev/null; then shasum -a 256 "$@"; else sha256sum "$@"; fi; }

VERSION="$(at_ref build.json | json_field version -)"
NOTES="$(at_ref build.json | json_field notes -)"
PRERELEASE="$(at_ref build.json | json_field prerelease -)"
TAG="v$VERSION"
# Godot 引擎版本跟随 C# 工程引用的 Godot.NET.Sdk，导出模板必须同版本
GODOT_VERSION="$(at_ref src/Tank.Client/Tank.Client.csproj | sed -n 's#.*Sdk="Godot.NET.Sdk/\([0-9.]*\)".*#\1#p')"

worktree() {  # 该提交的干净 worktree，整个脚本只建一次，退出时删掉
	[ -z "$WT" ] || return 0
	WT="$(mktemp -d "${TMPDIR:-/tmp}/tank_ship.XXXXXX")"
	trap 'git -C "$ROOT" worktree remove --force "$WT" >/dev/null 2>&1 || true; rm -rf "$WT"' EXIT
	git -C "$ROOT" worktree add -q --detach "$WT" "$REF_SHA"
}

templates_dir() {
	case "$(uname -s)" in
		Darwin) echo "$HOME/Library/Application Support/Godot/export_templates/$GODOT_VERSION.stable.mono" ;;
		*) echo "${XDG_DATA_HOME:-$HOME/.local/share}/godot/export_templates/$GODOT_VERSION.stable.mono" ;;
	esac
}

# —— check：只读检查 ——
cmd_check() {
	local need_godot="${1:-yes}"
	step "检查 $TAG（提交 ${REF_SHA:0:7}）"
	[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]] || die "build.json 的 version「$VERSION」不是 X.Y.Z 或 X.Y.Z-后缀"
	[ -n "$NOTES" ] || die "build.json 缺少 notes（一句话版本说明）"
	case "$NOTES" in *$'\n'*) die "notes 只能有一行" ;; esac
	local chars; chars="$(python3 -c 'import sys; print(len(sys.argv[1]))' "$NOTES")"
	[ "$chars" -le "$MAX_NOTES_CHARS" ] || die "notes $chars 字，太长（≤ $MAX_NOTES_CHARS）"
	ok "版本 $VERSION$([ "$PRERELEASE" = true ] && echo "（预发布）")：$NOTES"

	command -v dotnet >/dev/null || die "找不到 dotnet（.NET 8 SDK）"
	if [ "$need_godot" = yes ]; then
		command -v "$GODOT" >/dev/null || [ -x "$GODOT" ] || die "找不到 Godot .NET 版：设置 GODOT=<路径>"
		local gv; gv="$("$GODOT" --version 2>/dev/null | head -1)"
		[[ "$gv" == "$GODOT_VERSION.stable.mono"* ]] || die "Godot 版本是「$gv」，工程需要 $GODOT_VERSION.stable.mono"
		[ -f "$(templates_dir)/macos.zip" ] || die "缺少导出模板：$(templates_dir)"
		ok "Godot $gv，导出模板就绪"
	fi

	git -C "$ROOT" fetch -q "$REMOTE" main --tags 2>/dev/null || echo "! 拉取 $REMOTE 失败，按本地记录检查"
	if git -C "$ROOT" merge-base --is-ancestor "$REF_SHA" "$REMOTE/main" 2>/dev/null; then
		ok "提交已在 $REMOTE/main 上"
	else
		echo "! 提交 ${REF_SHA:0:7} 还不在 $REMOTE/main 上：可以试打包，但 github 会拒绝发布"
	fi
	local tagged; tagged="$(git -C "$ROOT" ls-remote --tags "$REMOTE" "refs/tags/$TAG^{}" "refs/tags/$TAG" 2>/dev/null | tail -1 | cut -f1)"
	if [ -n "$tagged" ] && [ "$tagged" != "$REF_SHA" ]; then
		die "远端已有 $TAG，指向另一个提交 ${tagged:0:7}：发新版请先改 build.json 的 version"
	fi
	[ -z "$tagged" ] || ok "远端已有 $TAG 且指向这个提交（重跑）"
}

# —— test：干净 worktree 里的单测和压测 ——
cmd_test() {
	worktree
	step "单元与集成测试"
	dotnet test "$WT/tests/Tank.Core.Tests" -c Release --nologo -v q
	dotnet test "$WT/tests/Tank.Net.Tests" -c Release --nologo -v q
	step "网络压测（docs/03 §11）"
	dotnet run --project "$WT/tests/Tank.Soak" -c Release
}

# 把 build.json 的版本写进 worktree 里的 Godot 工程（Windows 文件版本只能是四段纯数字）
stamp_version() {
	local numeric="${VERSION%%-*}"
	python3 - "$WT/src/Tank.Client" "$VERSION" "$numeric" <<'PY'
import re, sys
root, version, numeric = sys.argv[1:4]
def sub(path, pairs):
    text = open(path, encoding="utf-8").read()
    for pattern, value in pairs:
        text, n = re.subn(pattern, value, text, flags=re.M)
        if n == 0:
            sys.exit(f"{path}: 找不到 {pattern}")
    open(path, "w", encoding="utf-8").write(text)
sub(f"{root}/project.godot", [(r'^config/version=".*"$', f'config/version="{version}"')])
sub(f"{root}/export_presets.cfg", [
    (r'^application/short_version=".*"$', f'application/short_version="{numeric}"'),
    (r'^application/version=".*"$', f'application/version="{numeric}"'),
    (r'^application/file_version=".*"$', f'application/file_version="{numeric}.0"'),
    (r'^application/product_version=".*"$', f'application/product_version="{numeric}.0"'),
])
PY
}

has_assemblies() {  # has_assemblies <预设> <导出产物>
	case "$1" in
		macOS) unzip -l "$2" | grep -q "/Tank.Client.dll$" ;;
		*) ls "$(dirname "$2")"/data_Tank.Client_*/Tank.Client.dll >/dev/null 2>&1 ;;
	esac
}

# 在 macOS 上把导出的安装包真正启动一次：开房、加一个机器人、开局，确认能玩再发
smoke_test_macos() {
	[ "$(uname -s)" = Darwin ] || { echo "! 不是 macOS，跳过安装包启动测试"; return 0; }
	local dir; dir="$(mktemp -d "${TMPDIR:-/tmp}/tank_smoke.XXXXXX")"
	ditto -x -k "$1" "$dir"
	local exe; exe="$(find "$dir" -path "*.app/Contents/MacOS/*" -type f | head -1)"
	local log="$dir/smoke.log"
	"$exe" --headless -- --host --port 27091 --bots 1 --autostart --quit-after 4 >"$log" 2>&1 || true
	grep -q "match started" "$log" || { tail -20 "$log" >&2; rm -rf "$dir"; die "macOS 安装包启动测试失败：没能开局"; }
	rm -rf "$dir"
	ok "macOS 安装包启动测试：开房、机器人加入、开局正常"
}

# —— build：check + test + 打包 ——
cmd_build() {
	cmd_check yes
	[ "${SHIP_SKIP_TESTS:-0}" = 1 ] && echo "! SHIP_SKIP_TESTS=1：跳过测试" || cmd_test
	worktree
	stamp_version
	local stage="$OUT/release.tmp"
	rm -rf "$stage" "$WT/build"
	mkdir -p "$stage" "$WT/build/client/macos" "$WT/build/client/windows" "$WT/build/client/linux"

	step "导出客户端（Godot $GODOT_VERSION）"
	# 首次导入编辑器在退出时可能崩溃，不影响导入结果；后面的导出会校验产物
	"$GODOT" --headless --path "$WT/src/Tank.Client" --import >/dev/null 2>&1 || true
	local preset target
	for preset in macOS Windows Linux; do
		case "$preset" in
			macOS) target="$WT/build/client/macos/$APP.zip" ;;
			Windows) target="$WT/build/client/windows/$APP.exe" ;;
			Linux) target="$WT/build/client/linux/$APP.x86_64" ;;
		esac
		"$GODOT" --headless --path "$WT/src/Tank.Client" --export-release "$preset" "$target" >"$WT/build/export-$preset.log" 2>&1 || true
		[ -s "$target" ] || { tail -30 "$WT/build/export-$preset.log" >&2; die "导出 $preset 失败"; }
		# Godot 在 C# 部分出错时仍会产出引擎和 pck，但游戏打不开：必须确认程序集进了包
		has_assemblies "$preset" "$target" || { grep -m5 "ERROR" "$WT/build/export-$preset.log" >&2 || true; die "导出 $preset 的包里没有 C# 程序集"; }
		ok "$preset"
	done
	smoke_test_macos "$WT/build/client/macos/$APP.zip"
	cp "$WT/build/client/macos/$APP.zip" "$stage/$APP-v$VERSION-macos-universal.zip"
	(cd "$WT/build/client/windows" && rm -f ./*.log && zip -qr "$stage/$APP-v$VERSION-windows-x64.zip" .)
	(cd "$WT/build/client/linux" && chmod +x "$APP.x86_64" && tar -czf "$stage/$APP-v$VERSION-linux-x64.tar.gz" .)

	step "发布专用服务器（自带 .NET 运行时）"
	local rid dir name
	for rid in "${SERVER_RIDS[@]}"; do
		dir="$WT/build/server/$rid"
		dotnet publish "$WT/src/Tank.Server" -c Release -r "$rid" --self-contained true \
			-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
			-p:Version="${VERSION%%-*}" -p:InformationalVersion="$VERSION" -o "$dir" --nologo -v q
		name="$APP-Server-v$VERSION-$rid"
		if [[ "$rid" == win-* ]]; then (cd "$dir" && zip -qr "$stage/$name.zip" .)
		else (cd "$dir" && tar -czf "$stage/$name.tar.gz" .)
		fi
		ok "$rid"
	done

	(cd "$stage" && sha256 -- *.zip *.tar.gz > SHA256SUMS.txt)
	python3 - "$stage/RELEASE.json" "$VERSION" "$REF_SHA" "$NOTES" "$PRERELEASE" "$GODOT_VERSION" <<'PY'
import json, sys, datetime
path, version, commit, notes, pre, godot = sys.argv[1:7]
json.dump({"version": version, "commit": commit, "notes": notes, "prerelease": pre == "true",
           "godot": godot, "built_at": datetime.datetime.now(datetime.timezone.utc).isoformat(timespec="seconds")},
          open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
PY
	rm -rf "$OUT/release" && mv "$stage" "$OUT/release"
	step "产物：$OUT/release"
	ls -lh "$OUT/release"
}

release_notes() {  # release_notes <输出文件> [说明文件]
	local file="${2:-}"
	[ -n "$file" ] || { [ -f "$ROOT/docs/releases/$TAG.md" ] && file="$ROOT/docs/releases/$TAG.md"; } || true
	if [ -n "$file" ]; then
		[ -f "$file" ] || die "找不到发布说明 $file"
		cp "$file" "$1"
		return
	fi
	cat > "$1" <<MD
$NOTES

## 下载

| 文件 | 说明 |
|---|---|
| \`$APP-v$VERSION-macos-universal.zip\` | macOS 客户端（Apple 芯片与 Intel） |
| \`$APP-v$VERSION-windows-x64.zip\` | Windows 客户端 |
| \`$APP-v$VERSION-linux-x64.tar.gz\` | Linux 客户端 |
| \`$APP-Server-v$VERSION-<平台>\` | 无头专用服务器，自带运行时 |

校验：\`SHA256SUMS.txt\`。
MD
}

# —— github：建 tag 和 Release，可重复运行 ——
cmd_github() {
	local notes_in="${1:-}" rel="$OUT/release"
	command -v gh >/dev/null || die "需要 GitHub CLI（gh）"
	gh auth status >/dev/null 2>&1 || [ -n "${GH_TOKEN:-}" ] || die "gh 还没登录：先运行 gh auth login"
	[ -f "$rel/RELEASE.json" ] || die "缺少 $rel/RELEASE.json：先运行 ship.sh build"
	local built_version built_commit
	built_version="$(json_field version "$rel/RELEASE.json")"
	built_commit="$(json_field commit "$rel/RELEASE.json")"
	[ "$built_version" = "$VERSION" ] || die "$rel 里是 v$built_version，build.json 是 v$VERSION：重新 build"
	(cd "$rel" && sha256 -c --quiet SHA256SUMS.txt) || die "产物和 SHA256SUMS.txt 对不上"
	git -C "$ROOT" fetch -q "$REMOTE" main --tags 2>/dev/null || true
	git -C "$ROOT" merge-base --is-ancestor "$built_commit" "$REMOTE/main" \
		|| die "打包用的提交 ${built_commit:0:7} 不在 $REMOTE/main 上：先推送"

	local repo assets=() f
	repo="$(cd "$ROOT" && gh repo view --json nameWithOwner -q .nameWithOwner)"
	for f in "$rel"/*.zip "$rel"/*.tar.gz "$rel/SHA256SUMS.txt"; do assets+=("$f"); done
	local notes_file; notes_file="$(mktemp)"
	release_notes "$notes_file" "$notes_in"
	local pre_flag=(); [ "$(json_field prerelease "$rel/RELEASE.json")" = true ] && pre_flag=(--prerelease)

	if gh release view "$TAG" -R "$repo" >/dev/null 2>&1; then
		step "Release $TAG 已存在：核对附件"
		local remote_sums; remote_sums="$(mktemp -d)"
		gh release download "$TAG" -R "$repo" -p SHA256SUMS.txt -D "$remote_sums" --clobber >/dev/null 2>&1 || true
		if cmp -s "$remote_sums/SHA256SUMS.txt" "$rel/SHA256SUMS.txt" && [ "$(missing_assets "$repo")" = "" ]; then
			ok "线上附件与本地一致：https://github.com/$repo/releases/tag/$TAG"
			return 0
		fi
		[ "${REPAIR:-0}" = 1 ] || die "线上附件与本地不一致：确认后用 REPAIR=1 覆盖"
		[ "${DRY_RUN:-0}" = 1 ] && { echo "DRY_RUN：会覆盖上传 ${#assets[@]} 个附件"; return 0; }
		gh release upload "$TAG" -R "$repo" "${assets[@]}" --clobber
		ok "已覆盖附件"
		return 0
	fi

	step "发布 $TAG → $repo（提交 ${built_commit:0:7}）"
	printf '附件：\n'; printf '  %s\n' "${assets[@]##*/}"
	printf '说明：\n'; sed 's/^/  | /' "$notes_file"
	if [ "${DRY_RUN:-0}" = 1 ]; then echo "DRY_RUN：不创建"; return 0; fi
	gh release create "$TAG" -R "$repo" "${assets[@]}" --target "$built_commit" \
		--title "Tank 3D $TAG" --notes-file "$notes_file" "${pre_flag[@]}"
	local missing; missing="$(missing_assets "$repo")"
	[ -z "$missing" ] || die "Release 已建但缺少附件：$missing（用 REPAIR=1 重跑补上）"
	ok "https://github.com/$repo/releases/tag/$TAG"
}

missing_assets() {  # 本地有、线上 Release 没有（或大小不同）的附件名
	gh release view "$TAG" -R "$1" --json assets --jq '.assets[] | "\(.name) \(.size)"' > "${TMPDIR:-/tmp}/tank_assets.$$"
	local f name size
	for f in "$OUT/release"/*.zip "$OUT/release"/*.tar.gz "$OUT/release/SHA256SUMS.txt"; do
		name="${f##*/}"; size="$(wc -c < "$f" | tr -d ' ')"
		grep -qx "$name $size" "${TMPDIR:-/tmp}/tank_assets.$$" || printf '%s ' "$name"
	done
	rm -f "${TMPDIR:-/tmp}/tank_assets.$$"
}

# —— bump：改版本并提交 ——
cmd_bump() {
	local version="${1:?用法：ship.sh bump <版本> \"<一句话说明>\"}" notes="${2:?缺少一句话说明}"
	[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]] || die "版本「$version」不是 X.Y.Z 或 X.Y.Z-后缀"
	[ -z "$(git -C "$ROOT" status --porcelain build.json)" ] || die "build.json 有未提交的改动"
	git -C "$ROOT" ls-remote --exit-code --tags "$REMOTE" "refs/tags/v$version" >/dev/null 2>&1 && die "远端已有 tag v$version"
	python3 - "$ROOT/build.json" "$version" "$notes" <<'PY'
import json, sys
path, version, notes = sys.argv[1:4]
data = json.load(open(path, encoding="utf-8"))
data["version"], data["notes"] = version, notes
data["prerelease"] = version.startswith("0.") or "-" in version
open(path, "w", encoding="utf-8").write(json.dumps(data, ensure_ascii=False, indent="\t") + "\n")
PY
	git -C "$ROOT" add build.json
	git -C "$ROOT" commit -q -m "chore: 发布 v$version：$notes"
	ok "已提交 v$version。git push 到 main 后，Release 工作流会自动打包并发布"
}

case "${1:-}" in
	check) cmd_check yes ;;
	test) cmd_test ;;
	build) cmd_build ;;
	github) cmd_github "${2:-}" ;;
	all) cmd_build; cmd_github "${2:-}" ;;
	bump) shift; cmd_bump "$@" ;;
	*) sed -n '2,15p' "$0"; exit 2 ;;
esac
