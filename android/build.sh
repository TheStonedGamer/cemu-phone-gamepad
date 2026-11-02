#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
android_build_tools="$ANDROID_HOME/build-tools/35.0.0"
android_platform="$ANDROID_HOME/platforms/android-35/android.jar"
mkdir -p build/classes build/dex signing ../dist
"$android_build_tools/aapt2" compile --dir res -o build/resources.zip
"$android_build_tools/aapt2" link -o build/base.apk -I "$android_platform" --manifest AndroidManifest.xml build/resources.zip
javac --release 8 -classpath "$android_platform" -d build/classes src/com/brian/cemugamepad/*.java
jar cf build/classes.jar -C build/classes .
"$android_build_tools/d8" --lib "$android_platform" --min-api 23 --output build/dex build/classes.jar
cp build/base.apk build/unsigned.apk
(cd build/dex && zip -q ../unsigned.apk classes.dex)
"$android_build_tools/zipalign" -f -p 4 build/unsigned.apk build/aligned.apk
if [ ! -f signing/controller.jks ]; then
 keytool -genkeypair -keystore signing/controller.jks -storepass android -keypass android -alias controller -dname 'CN=Brian Cemu Phone GamePad' -keyalg RSA -keysize 2048 -validity 10000
fi
chmod 600 signing/controller.jks
"$android_build_tools/apksigner" sign --ks signing/controller.jks --ks-pass pass:android --key-pass pass:android --out ../dist/Cemu-Phone-GamePad.apk build/aligned.apk
"$android_build_tools/apksigner" verify --verbose ../dist/Cemu-Phone-GamePad.apk
"$android_build_tools/aapt2" dump badging ../dist/Cemu-Phone-GamePad.apk
