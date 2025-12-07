#!/bin/zsh
/usr/bin/xcrun swift -e 'import AVFoundation; AVSpeechSynthesisVoice.speechVoices().forEach { print($0.identifier) }' > voice_identifiers.txt
echo "Voice identifiers can be found in 'voice_identifiers.txt'"