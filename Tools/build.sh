#!/bin/zsh

HOMEBREW_PREFIX="$(brew --prefix 2>/dev/null || echo '/usr/local')"
TML_DIR="$HOME/Library/Application Support/Steam/steamapps/common/tModLoader"
TML_DLL="tModLoader.dll"

if ! command -v dotnet &> /dev/null; then
    echo "❌ Error: The 'dotnet' command was not found."
    echo "Please ensure .NET is installed and accessible in your shell's PATH."
    exit 1
fi

  export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"

  if [[ ! -d "$TML_DIR" ]]; then
    echo "❌ Error: tModLoader installation directory not found."
    echo "Expected path: $TML_DIR"
    exit 1
fi

# Build native macOS speech bridge if on macOS
if [[ "$OSTYPE" == "darwin"* ]]; then
    echo "--- Building native macOS speech bridge ---"
    cd AVSpeechBridge
    make clean all
    
    # Ensure Libraries directory exists in ModSources
    MOD_SOURCES_DIR="$HOME/Library/Application Support/Terraria/tModLoader/ModSources/TerrariaAccess"
    LIB_DEST_DIR="$MOD_SOURCES_DIR/Libraries"
    mkdir -p "$LIB_DEST_DIR"
    
    # Copy dylib to ModSources Libraries (for tModLoader to package it)
    cp libAVSpeechBridge.dylib "$LIB_DEST_DIR/"
    
    # Generate/copy config file to ModSources Libraries
    # AVSpeechProvider looks for it in the same directory as dylib
    if [[ ! -f "$LIB_DEST_DIR/avspeechbridge.conf" ]]; then
        cat > "$LIB_DEST_DIR/avspeechbridge.conf" <<EOF
[Provider 1]
voiceId=com.apple.speech.synthesis.voice.Alex
rate=0.5

[Provider 2]
voiceId=com.apple.voice.enhanced.en-GB.Kate
rate=0.5
EOF
        echo "Created default avspeechbridge.conf in $LIB_DEST_DIR"
    fi
    
    cd ..
fi

cd "$TML_DIR"
"$HOME/Library/Application Support/Steam/steamapps/common/tModLoader/LaunchUtils/ScriptCaller.sh" -build "$HOME/Library/Application Support/Terraria/tModLoader/ModSources/TerrariaAccess"
cd -
