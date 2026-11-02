#!/usr/bin/env bash
# 一键打包发布产物（需在 macOS 上运行，需 .NET 8 SDK）
#   Windows: Clothesline-<ver>-win-x64.exe / -win-arm64.exe  （自包含单文件，双击即用）
#   macOS:   Clothesline-<ver>-macos-arm64.dmg / -macos-x64.dmg （拖入「应用程序」即可）
# 用法: scripts/package.sh [版本号]      例: scripts/package.sh 1.0.0
set -euo pipefail

VERSION="${1:-1.0.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/src/Clothesline.App/Clothesline.App.csproj"
OUT="$ROOT/release"
WORK="$OUT/.work"

rm -rf "$OUT"
mkdir -p "$OUT" "$WORK"

publish() { # $1 = RID, $2 = 输出目录, 其余为额外参数
  local rid="$1" dir="$2"; shift 2
  dotnet publish "$PROJ" -c Release -r "$rid" --self-contained true \
    -p:Version="$VERSION" -p:DebugType=none -p:DebugSymbols=false \
    -o "$dir" "$@" >/dev/null
}

# ---------- Windows：自包含单文件 exe ----------
for rid in win-x64 win-arm64; do
  echo "==> Windows $rid"
  publish "$rid" "$WORK/$rid" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true
  cp "$WORK/$rid/Clothesline.App.exe" "$OUT/Clothesline-$VERSION-$rid.exe"
done

# ---------- macOS：.app 包 + dmg ----------
ICONSET="$WORK/Clothesline.iconset"
mkdir -p "$ICONSET"
SRC_ICON="$ROOT/src/Clothesline.App/Assets/clothesline.png"
for s in 16 32 128 256; do
  sips -z $s $s "$SRC_ICON" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  d=$((s * 2))
  sips -z $d $d "$SRC_ICON" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$WORK/Clothesline.icns"

for arch in arm64 x64; do
  rid="osx-$arch"
  echo "==> macOS $rid"
  APP="$WORK/$rid/Clothesline.app"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  publish "$rid" "$APP/Contents/MacOS" -p:UseAppHost=true
  sed "s/__VERSION__/$VERSION/g" "$ROOT/packaging/macos/Info.plist" > "$APP/Contents/Info.plist"
  cp "$WORK/Clothesline.icns" "$APP/Contents/Resources/Clothesline.icns"
  # 无开发者证书时做 ad-hoc 签名（Apple Silicon 要求可执行文件至少带 ad-hoc 签名）
  codesign --force --deep --sign - "$APP" >/dev/null 2>&1

  DMG_SRC="$WORK/$rid/dmg"
  mkdir -p "$DMG_SRC"
  cp -R "$APP" "$DMG_SRC/"
  ln -s /Applications "$DMG_SRC/Applications"
  hdiutil create -volname "Clothesline" -srcfolder "$DMG_SRC" -ov -format UDZO \
    "$OUT/Clothesline-$VERSION-macos-$arch.dmg" >/dev/null
done

rm -rf "$WORK"
echo "==> 完成："
ls -lh "$OUT"
