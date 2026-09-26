#!/bin/zsh
set -eu
cd "$(dirname "$0")/.."
mkdir -p 'native/build/Kinesthetic Club Motion.app/Contents/MacOS'
cp native/Info.plist 'native/build/Kinesthetic Club Motion.app/Contents/Info.plist'
# This Mac's CLT has two definitions of SwiftBridging. A build-only virtual
# filesystem overlay hides the obsolete definition; system files stay intact.
/usr/local/bin/python3 - <<'PY'
import json
from pathlib import Path
p=Path('/tmp/kinesthetic-swift-overlay');p.mkdir(exist_ok=True)
(p/'empty.modulemap').write_text('// Superseded by bridging.modulemap\n')
(p/'overlay.json').write_text(json.dumps({'version':0,'roots':[{'type':'file','name':'/Library/Developer/CommandLineTools/usr/include/swift/module.modulemap','external-contents':str(p/'empty.modulemap')}]}))
PY
swiftc -parse-as-library -target arm64-apple-macos14.0 \
  -sdk /Library/Developer/CommandLineTools/SDKs/MacOSX15.4.sdk \
  -vfsoverlay /tmp/kinesthetic-swift-overlay/overlay.json \
  -Xcc -ivfsoverlay -Xcc /tmp/kinesthetic-swift-overlay/overlay.json \
  native/ClubMotionBridge.swift \
  -o 'native/build/Kinesthetic Club Motion.app/Contents/MacOS/ClubMotionBridge' \
  -framework SwiftUI -framework CoreMotion \
  -module-cache-path /tmp/kinesthetic-swift-clean-cache
codesign --force --deep --sign - 'native/build/Kinesthetic Club Motion.app'
