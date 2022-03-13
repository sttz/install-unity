#!/bin/bash

PROJECT="Command/Command.csproj"
TARGET="net6.0"
ARCH="osx-x64"
SIGN_IDENTITY="Developer ID Application: Feist GmbH (DHNHQKSSYT)"
ENTITLEMENTS="Build/notarization.entitlements"
BUNDLE_ID="ch.sttz.install-unity"

if [[ -z "$ASC_USER" ]]; then
    echo "ASC user not set in ASC_USER"
    exit 1
fi

cd "$(dirname "$0")/.."

# Extract version from project

VERSION="$(sed -n 's/[\ ]*<Version>\(.*\)<\/Version>\r*/\1/p' "$PROJECT")"

if [[ -z "$VERSION" ]]; then
    echo "Could not parse version from project: $PROJECT"
    exit 1
fi

# Build a new executable

dotnet publish -r "$ARCH" -c release -f "$TARGET" "$PROJECT" || exit 1

BUILD_OUTPUT="Command/bin/release/$TARGET/$ARCH/publish/Command"

if [ ! -f "$BUILD_OUTPUT" ]; then
    echo "Could not find executable at path: $BUILD_OUTPUT"
    exit 1
fi

# Codesign, archive and notarize executable

ARCHIVE="Releases/$VERSION"
EXECUTABLE="$ARCHIVE/install-unity"
ZIPARCHIVE="Releases/install-unity-$VERSION.zip"

mkdir "$ARCHIVE"
cp "$BUILD_OUTPUT" "$EXECUTABLE"

codesign --force --timestamp --options=runtime --entitlements="$ENTITLEMENTS" --sign "$SIGN_IDENTITY" "$EXECUTABLE" || exit 1

pushd "$ARCHIVE"
zip "../install-unity-$VERSION.zip" "install-unity" || exit 1
popd

xcrun altool --notarize-app --primary-bundle-id "$BUNDLE_ID" --asc-provider "$ASC_PROVIDER" --username "$ASC_USER" --file "$ZIPARCHIVE" || exit 1

# Shasum for Homebrew

shasum -a 256 "$ZIPARCHIVE"
