#!/bin/bash

set -e

APP_NAME="Engine"
APP_BUNDLE_NAME="${APP_NAME}.app"
BUILD_CONFIG="Release"
TARGET_FRAMEWORK="net9.0"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="${PROJECT_DIR}/dist"
APP_BUNDLE_PATH="${OUTPUT_DIR}/${APP_BUNDLE_NAME}"

ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    RUNTIME_ID="osx-arm64"
else
    RUNTIME_ID="osx-x64"
fi

echo "Building ${APP_NAME} for macOS (${RUNTIME_ID})..."
echo "Project directory: ${PROJECT_DIR}"
echo "Output directory: ${OUTPUT_DIR}"

if [ -d "${OUTPUT_DIR}" ]; then
    echo "Cleaning previous build..."
    rm -rf "${OUTPUT_DIR}"
fi

mkdir -p "${OUTPUT_DIR}"

echo "Building project..."
dotnet publish "${PROJECT_DIR}/Engine.Editor/Engine.Editor.csproj" \
    --configuration "${BUILD_CONFIG}" \
    --runtime "${RUNTIME_ID}" \
    --self-contained true \
    --output "${OUTPUT_DIR}/publish" \
    -p:PublishSingleFile=false \
    -p:IncludeNativeLibrariesForSelfExtract=true

echo "Creating .app bundle structure..."
mkdir -p "${APP_BUNDLE_PATH}/Contents/MacOS"
mkdir -p "${APP_BUNDLE_PATH}/Contents/Resources"

echo "Copying published files..."
cp -R "${OUTPUT_DIR}/publish/"* "${APP_BUNDLE_PATH}/Contents/MacOS/"

EXECUTABLE_NAME="Engine.Editor"
if [ ! -f "${APP_BUNDLE_PATH}/Contents/MacOS/${EXECUTABLE_NAME}" ]; then
    echo "Error: Executable ${EXECUTABLE_NAME} not found in publish output"
    exit 1
fi

chmod +x "${APP_BUNDLE_PATH}/Contents/MacOS/${EXECUTABLE_NAME}"

echo "Creating Info.plist..."
cat > "${APP_BUNDLE_PATH}/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleExecutable</key>
    <string>${EXECUTABLE_NAME}</string>
    <key>CFBundleIdentifier</key>
    <string>com.engine.editor</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>${APP_NAME}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSRequiresAquaSystemAppearance</key>
    <false/>
</dict>
</plist>
EOF

cat > "${APP_BUNDLE_PATH}/Contents/MacOS/launcher.sh" <<'EOF'
#!/bin/bash
cd "$(dirname "$0")"
exec ./Engine.Editor "$@"
EOF
chmod +x "${APP_BUNDLE_PATH}/Contents/MacOS/launcher.sh"

echo ""
echo "✓ Build complete!"
echo "  App bundle created at: ${APP_BUNDLE_PATH}"
echo ""
echo "You can now:"
echo "  1. Test the app: open \"${APP_BUNDLE_PATH}\""
echo "  2. Move it to Applications: cp -R \"${APP_BUNDLE_PATH}\" /Applications/"
echo ""

