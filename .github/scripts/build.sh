#!/usr/bin/env bash
# AppMare Build Script — handles all platforms
# Usage: bash build.sh <platform>
# Platforms: Androidnewapk | androidkeystore | playnew | playkey | IOS | MAC | Windows
set -euo pipefail

PLATFORM="${1:?Usage: build.sh <platform>}"

# ── Detect .NET major version from csproj ─────────────────────────────────
detect_dotnet_major() {
    grep -oP 'net\K\d+' SampleApp/SampleApp.csproj | sort -rn | head -1
}

# ── Detect bundle ID from csproj ──────────────────────────────────────────
detect_bundle_id() {
    grep -oP '(?<=<ApplicationId>)[^<]+' SampleApp/SampleApp.csproj | head -1 || \
    grep -oP '(?<=<BundleIdentifier>)[^<]+' SampleApp/SampleApp.csproj | head -1
}

# ── Install .NET SDK ───────────────────────────────────────────────────────
install_dotnet() {
    local major="$1"
    export PATH="$HOME/.dotnet:$PATH"
    export DOTNET_ROOT="$HOME/.dotnet"
    if [[ -x "$HOME/.dotnet/dotnet" ]]; then
        echo ">>> .NET SDK already cached ($(dotnet --version))"
        return
    fi
    echo ">>> Installing .NET ${major}.0..."
    curl -fsSL https://dot.net/v1/dotnet-install.sh | \
        bash -s -- --channel "${major}.0" --install-dir "$HOME/.dotnet"
    dotnet --version
}

# ── Install workload (skips if already present) ────────────────────────────
workload_install() {
    local wl="$1"
    if dotnet workload list 2>/dev/null | grep -q "^${wl}"; then
        echo ">>> Workload ${wl} already cached, skipping"
        return
    fi
    dotnet workload install "$wl" --skip-manifest-update
}

# ── Download and extract project ZIP ──────────────────────────────────────
download_project() {
    echo ">>> Downloading project..."
    wget -q -O AppName.zip "$PROJECTURL"
    command -v 7z &>/dev/null || brew install sevenzip -q
    7z x AppName.zip -y -bd
}

# ── OneSignal notification ─────────────────────────────────────────────────
notify() {
    local msg="$1"
    curl -sf --location 'https://api.onesignal.com/notifications' \
        --header 'Content-Type: application/json' \
        --header "Authorization: Basic ${ONESIGNALAPI}" \
        --data "{
            \"app_id\": \"897e14e3-7897-4206-aa80-ee2fd35c7c0d\",
            \"include_subscription_ids\": [\"${ONESIGNAL_SUB_ID}\"],
            \"contents\": {\"en\": \"${msg}\"}
        }" || true
}

# ── Keychain setup (iOS + macOS) ───────────────────────────────────────────
setup_keychain() {
    security create-keychain -p 2001 build.keychain
    security default-keychain -s build.keychain
    security list-keychains -s build.keychain
    security unlock-keychain -p 2001 build.keychain
    security set-keychain-settings -t 3600 -u build.keychain
}

# ── Apple signing via fastlane cert + sigh (no git repo needed) ────────────
install_apple_signing() {
    local bundle_id="$1"
    local platform="${2:-ios}"   # ios | macos

    local api_key_json
    api_key_json=$(jq -n \
        --arg key       "$APIKEY" \
        --arg key_id    "$KEYID" \
        --arg issuer_id "$ISSUERID" \
        '{key: $key, key_id: $key_id, issuer_id: $issuer_id, in_house: false}')

    # Download/create distribution certificate → installs into keychain
    fastlane cert \
        --api_key "$api_key_json" \
        --platform "$platform" \
        --output_path /tmp/certs

    security set-key-partition-list \
        -S apple-tool:,apple:,codesign: \
        -s -k 2001 build.keychain

    # Download/create provisioning profile → installs into ~/Library/MobileDevice/Provisioning Profiles/
    fastlane sigh \
        --api_key "$api_key_json" \
        --app_identifier "$bundle_id" \
        --platform "$platform" \
        --output_path /tmp/profiles \
        --skip_install false
}

# ── Set artifact paths as GitHub env vars ─────────────────────────────────
set_env() { echo "${1}=${2}" >> "$GITHUB_ENV"; }

# ── Trap: notify on any error ─────────────────────────────────────────────
trap 'notify "❌ ${PLATFORM} build failed"' ERR

# ═══════════════════════════════════════════════════════════════════════════
# Platform builds
# ═══════════════════════════════════════════════════════════════════════════

DOTNET_MAJOR=$(detect_dotnet_major)
echo ">>> .NET major version: ${DOTNET_MAJOR}"

case "$PLATFORM" in

# ─────────────────────────────────────────────────────────────────────────
# Android: generate new keystore → APK + AAB only
# ─────────────────────────────────────────────────────────────────────────
Androidnewapk)
    download_project
    install_dotnet "$DOTNET_MAJOR"
    workload_install maui-android

    keytool -genkeypair -v \
        -keystore "$(pwd)/AppMare.keystore" \
        -keyalg RSA -keysize 2048 -validity 365000 \
        -storepass "$KEYSTOREPASS" \
        -alias    "$KEYSTOREALIAS" \
        -dname    "cn=Appmare"

    dotnet publish -f "net${DOTNET_MAJOR}.0-android" -c Release \
        -p:AndroidKeyStore=true \
        -p:AndroidSigningKeyStore="$(pwd)/AppMare.keystore" \
        -p:AndroidSigningKeyAlias="$KEYSTOREALIAS" \
        -p:AndroidSigningKeyPass="$KEYSTOREPASS" \
        -p:AndroidSigningStorePass="$KEYSTOREPASS"

    AAB=$(find SampleApp/bin/Release/net${DOTNET_MAJOR}.0-android/publish -name "*Signed*.aab" | head -1)
    APK=$(find SampleApp/bin/Release/net${DOTNET_MAJOR}.0-android/publish -name "*Signed*.apk" | head -1)
    printf "Alias: %s\nPassword: %s\nSave this keystore for future publishes.\n" \
        "$KEYSTOREALIAS" "$KEYSTOREPASS" > KeyStoreCredentials.txt

    set_env "aab-path"   "$AAB"
    set_env "apk-path"   "$APK"
    set_env "key-path"   "$(pwd)/AppMare.keystore"
    set_env "creds-path" "KeyStoreCredentials.txt"

    notify "✅ Android APK/AAB build complete"
    ;;

# ─────────────────────────────────────────────────────────────────────────
# Android: existing keystore → APK + AAB only
# ─────────────────────────────────────────────────────────────────────────
androidkeystore)
    download_project
    install_dotnet "$DOTNET_MAJOR"
    workload_install maui-android

    wget -q -O "$(pwd)/AppMare.keystore" "$KEYURL"

    dotnet publish -f "net${DOTNET_MAJOR}.0-android" -c Release \
        -p:AndroidKeyStore=true \
        -p:AndroidSigningKeyStore="$(pwd)/AppMare.keystore" \
        -p:AndroidSigningKeyAlias="$KEYSTOREALIAS" \
        -p:AndroidSigningKeyPass="$KEYSTOREPASS" \
        -p:AndroidSigningStorePass="$KEYSTOREPASS"

    AAB=$(find SampleApp/bin/Release/net${DOTNET_MAJOR}.0-android/publish -name "*Signed*.aab" | head -1)
    APK=$(find SampleApp/bin/Release/net${DOTNET_MAJOR}.0-android/publish -name "*Signed*.apk" | head -1)

    set_env "aab-path" "$AAB"
    set_env "apk-path" "$APK"

    notify "✅ Android APK/AAB build complete"
    ;;

# ─────────────────────────────────────────────────────────────────────────
# Play Store: generate new keystore → upload to Play
# ─────────────────────────────────────────────────────────────────────────
playnew)
    download_project
    install_dotnet "$DOTNET_MAJOR"
    workload_install maui-android
    BUNDLE_ID=$(detect_bundle_id)

    keytool -genkeypair -v \
        -keystore "$(pwd)/AppMare.keystore" \
        -keyalg RSA -keysize 2048 -validity 365000 \
        -storepass "$KEYSTOREPASS" \
        -alias    "$KEYSTOREALIAS" \
        -dname    "cn=Appmare"

    dotnet publish -f "net${DOTNET_MAJOR}.0-android" -c Release \
        -p:AndroidKeyStore=true \
        -p:AndroidSigningKeyStore="$(pwd)/AppMare.keystore" \
        -p:AndroidSigningKeyAlias="$KEYSTOREALIAS" \
        -p:AndroidSigningKeyPass="$KEYSTOREPASS" \
        -p:AndroidSigningStorePass="$KEYSTOREPASS"

    AAB=$(find SampleApp/bin/Release/net${DOTNET_MAJOR}.0-android/publish -name "*Signed*.aab" | head -1)
    printf "Alias: %s\nPassword: %s\nSave this keystore for future publishes.\n" \
        "$KEYSTOREALIAS" "$KEYSTOREPASS" > KeyStoreCredentials.txt

    # Deploy via fastlane supply (pre-installed on macOS runners)
    echo "$PLAYDATA" > /tmp/play-key.json
    fastlane supply \
        --aab "$AAB" \
        --json_key /tmp/play-key.json \
        --package_name "$BUNDLE_ID" \
        --track "${PLAY_TRACK:-production}"

    set_env "aab-path"   "$AAB"
    set_env "key-path"   "$(pwd)/AppMare.keystore"
    set_env "creds-path" "KeyStoreCredentials.txt"

    notify "✅ Play Store upload complete (${PLAY_TRACK:-production})"
    ;;

# ─────────────────────────────────────────────────────────────────────────
# Play Store: existing keystore → upload to Play
# ─────────────────────────────────────────────────────────────────────────
playkey)
    download_project
    install_dotnet "$DOTNET_MAJOR"
    workload_install maui-android
    BUNDLE_ID=$(detect_bundle_id)

    wget -q -O "$(pwd)/AppMare.keystore" "$KEYURL"

    dotnet publish -f "net${DOTNET_MAJOR}.0-android" -c Release \
        -p:AndroidKeyStore=true \
        -p:AndroidSigningKeyStore="$(pwd)/AppMare.keystore" \
        -p:AndroidSigningKeyAlias="$KEYSTOREALIAS" \
        -p:AndroidSigningKeyPass="$KEYSTOREPASS" \
        -p:AndroidSigningStorePass="$KEYSTOREPASS"

    AAB=$(find SampleApp/bin/Release/net${DOTNET_MAJOR}.0-android/publish -name "*Signed*.aab" | head -1)

    echo "$PLAYDATA" > /tmp/play-key.json
    fastlane supply \
        --aab "$AAB" \
        --json_key /tmp/play-key.json \
        --package_name "$BUNDLE_ID" \
        --track "${PLAY_TRACK:-production}"

    set_env "aab-path" "$AAB"

    notify "✅ Play Store upload complete (${PLAY_TRACK:-production})"
    ;;

# ─────────────────────────────────────────────────────────────────────────
# iOS → App Store / TestFlight
# ─────────────────────────────────────────────────────────────────────────
IOS)
    download_project
    install_dotnet "$DOTNET_MAJOR"
    workload_install maui-ios
    BUNDLE_ID=$(detect_bundle_id)

    sudo xcode-select -s "$(ls -d /Applications/Xcode_*.app | sort -V | tail -1)" || true

    setup_keychain
    install_apple_signing "$BUNDLE_ID" "ios"

    CODE_SIGNING_KEY=$(security find-identity -p codesigning -v \
        | grep '"' | head -n1 | awk -F '"' '{print $2}')
    PROFILE=$(ls ~/Library/MobileDevice/Provisioning\ Profiles/*.mobileprovision 2>/dev/null | head -1)

    dotnet publish -f "net${DOTNET_MAJOR}.0-ios" -c Release \
        -p:ArchiveOnBuild=true \
        -p:RuntimeIdentifier=ios-arm64 \
        -p:CodesignKey="$CODE_SIGNING_KEY" \
        -p:CodesignProvision="$PROFILE"

    IPA=$(find SampleApp/bin/Release/net${DOTNET_MAJOR}.0-ios/ios-arm64/publish \
        -name "*.ipa" | head -1)

    mkdir -p ~/private_keys
    echo "$APIKEY" > ~/private_keys/AuthKey_${KEYID}.p8

    xcrun altool --validate-app -f "$IPA" -t ios \
        --apiKey "$KEYID" --apiIssuer "$ISSUERID"
    xcrun altool --upload-app   -f "$IPA" -t ios \
        --apiKey "$KEYID" --apiIssuer "$ISSUERID"

    set_env "ipa-path" "$IPA"

    notify "✅ iOS App Store upload complete"
    ;;

# ─────────────────────────────────────────────────────────────────────────
# macOS → Mac App Store / TestFlight
# ─────────────────────────────────────────────────────────────────────────
MAC)
    download_project
    install_dotnet "$DOTNET_MAJOR"
    workload_install maui-maccatalyst
    BUNDLE_ID=$(detect_bundle_id)

    setup_keychain
    install_apple_signing "$BUNDLE_ID" "macos"

    CODE_SIGNING_KEY=$(security find-identity -p codesigning -v \
        | grep 'Apple Distribution' | head -n1 | awk -F '"' '{print $2}')
    PKG_SIGNING_KEY=$(security find-identity -p macappstore -v \
        | grep '3rd Party Mac Developer Installer' | head -n1 | awk -F '"' '{print $2}')
    PROFILE=$(ls ~/Library/MobileDevice/Provisioning\ Profiles/*.mobileprovision 2>/dev/null | head -1)

    dotnet publish -f "net${DOTNET_MAJOR}.0-maccatalyst" -c Release \
        -p:CreatePackage=true \
        -p:EnableCodeSigning=true \
        -p:EnablePackageSigning=true \
        -p:CodesignKey="$CODE_SIGNING_KEY" \
        -p:CodesignProvision="$PROFILE" \
        -p:PackageSigningKey="$PKG_SIGNING_KEY"

    PKG=$(find SampleApp/bin/Release/net${DOTNET_MAJOR}.0-maccatalyst/publish \
        -name "*.pkg" | head -1)

    mkdir -p ~/private_keys
    echo "$APIKEY" > ~/private_keys/AuthKey_${KEYID}.p8

    xcrun altool --validate-app -f "$PKG" -t osx \
        --apiKey "$KEYID" --apiIssuer "$ISSUERID"
    xcrun altool --upload-app   -f "$PKG" -t osx \
        --apiKey "$KEYID" --apiIssuer "$ISSUERID"

    set_env "pkg-path" "$PKG"

    notify "✅ macOS App Store upload complete"
    ;;

# ─────────────────────────────────────────────────────────────────────────
# Windows → single EXE
# ─────────────────────────────────────────────────────────────────────────
Windows)
    echo ">>> Downloading project (Windows)..."
    curl -fsSL -o AppName.zip "$PROJECTURL"

    if command -v 7z &>/dev/null; then
        7z x AppName.zip -y -bd
    else
        powershell -Command "Expand-Archive -Path AppName.zip -DestinationPath . -Force"
    fi

    install_dotnet "$DOTNET_MAJOR"

    dotnet publish \
        -f "net${DOTNET_MAJOR}.0-windows10.0.19041.0" \
        -c Release \
        -p:RuntimeIdentifierOverride=win10-x64 \
        -p:WindowsPackageType=None \
        -p:WindowsAppSDKSelfContained=true \
        -p:PublishSingleFile=true \
        --self-contained true \
        -p:PublishTrimmed=true \
        -p:TrimMode=partial

    EXE=$(find SampleApp/bin/Release/net${DOTNET_MAJOR}.0-windows10.0.19041.0/win10-x64/publish \
        -name "*.exe" | head -1)

    set_env "exe-path" "$EXE"

    notify "✅ Windows build complete"
    ;;

# ─────────────────────────────────────────────────────────────────────────
# Web → WASM via Avalonia.Controls.Maui → Firebase Hosting
# ─────────────────────────────────────────────────────────────────────────
Web)
    download_project
    install_dotnet "$DOTNET_MAJOR"
    workload_install wasm-tools

    BROWSER_PROJ=$(find . -name "*.Browser.csproj" | head -1)
    dotnet publish "$BROWSER_PROJ" -c Release

    WWWROOT=$(find . -path "*/publish/wwwroot" -type d | head -1)

    # Deploy via Firebase Hosting REST API (Python stdlib — no npm/firebase-tools)
    python3 "$(dirname "$0")/firebase_deploy.py"

    set_env "web-url" "https://${FIREBASE_PROJECT_ID}.web.app"
    notify "✅ Web app live at https://${FIREBASE_PROJECT_ID}.web.app"
    ;;

*)
    echo "Unknown platform: $PLATFORM"
    exit 1
    ;;
esac
