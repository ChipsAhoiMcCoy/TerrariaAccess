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

cd "$TML_DIR"
"$HOME/Library/Application Support/Steam/steamapps/common/tModLoader/LaunchUtils/ScriptCaller.sh" -build "$HOME/Library/Application Support/Terraria/tModLoader/ModSources/ScreenReaderMod"
cd -
