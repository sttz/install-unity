#!/bin/zsh

PROJECT="Command/Command.csproj"
TARGET="net7.0"
ARCHES=("osx-x64" "osx-arm64")
SIGN_IDENTITY="Developer ID Application: Feist GmbH (DHNHQKSSYT)"
ENTITLEMENTS="Build/notarization.entitlements"

# Mapping of arche names used by .Net to the ones used by lipo
typeset -A LIPO_ARCHES=()
LIPO_ARCHES[osx-x64]=x86_64
LIPO_ARCHES[osx-arm64]=arm64

if [[ -z "$NOTARY_PROFILE" ]]; then
    echo "notarytool keychain profile not set in NOTARY_PROFILE"
    exit 1
fi

cd "$(dirname "$0")/.."

# Extract version from project

VERSION="$(sed -n 's/[\ ]*<Version>\(.*\)<\/Version>\r*/\1/p' "$PROJECT")"

if [[ -z "$VERSION" ]]; then
    echo "Could not parse version from project: $PROJECT"
    exit 1
fi

# Build new executables, one per arch

ARCH_ARGS=()
for arch in $ARCHES; do
    dotnet publish \
        -r "$arch" \
        -c release \
        -f "$TARGET" \
        --self-contained \
        "$PROJECT" \
        || exit 1

    output="Command/bin/release/$TARGET/$arch/publish/Command"

    if [ ! -f "$output" ]; then
        echo "Could not find executable at path: $output"
        exit 1
    fi

    if [[ -z $LIPO_ARCHES[$arch] ]]; then
        echo "No lipo arch specified for .Net arch: $arch"
        exit 1
    fi

    ARCH_ARGS+=(-arch $LIPO_ARCHES[$arch] "$output")
done

# Merge, codesign, archive and notarize executable

ARCHIVE="Releases/$VERSION"
EXECUTABLE="$ARCHIVE/install-unity"
ZIPARCHIVE="Releases/install-unity-$VERSION.zip"

mkdir -p "$ARCHIVE"

lipo -create $ARCH_ARGS -output "$EXECUTABLE" || exit 1

codesign --force --timestamp --options=runtime --entitlements="$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$EXECUTABLE" || exit 1

pushd "$ARCHIVE"
zip "../install-unity-$VERSION.zip" "install-unity" || exit 1
popd

xcrun notarytool submit --wait --keychain-profile "$NOTARY_PROFILE" --wait --progress "$ZIPARCHIVE" || exit 1

# Shasum for Homebrew

shasum -a 256 "$ZIPARCHIVE"
