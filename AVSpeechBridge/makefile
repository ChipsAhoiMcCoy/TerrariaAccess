# Makefile for building the native macOS speech bridge

# --------------------------------------------------------------------------
# --- Configuration Variables ---
# --------------------------------------------------------------------------

# Output library name
# The dylib will be generated in the same directory as this Makefile.
LIB_NAME = libAVSpeechBridge.dylib

# Source files
SOURCES = AVSpeechBridge.m

# Compiler
CC = clang

# Compiler Flags
# -fobjc-arc for memory management, -fPIC for position-independent code (required for dylib)
CFLAGS = -Wall -O2 -fPIC -fobjc-arc 

# Linker Flags
# -dynamiclib creates a dylib, -framework links to required macOS frameworks
LDFLAGS = -dynamiclib -framework Foundation -framework AVFoundation -framework CoreFoundation

# tModLoader Installation Path (macOS)
# The standard location for tModLoader mods on macOS.
TML_MOD_DIR = ~/Library/Application\ Support/Terraria/tModLoader/Mods

# --------------------------------------------------------------------------
# --- Targets ---
# --------------------------------------------------------------------------

# Phony targets are not actual files, ensuring 'make' runs the commands every time.
.PHONY: all clean install

# Default target: build the dylib in the current directory
all: $(LIB_NAME)

# Build the dynamic library
$(LIB_NAME): $(SOURCES)
	@echo "--- Compiling $@ ---"
	$(CC) $(CFLAGS) $(LDFLAGS) -o $@ $(SOURCES)
	@echo "Built $@ successfully in the current directory."

# Install target (builds and copies the dylib to the tModLoader Mods folder)
install: $(LIB_NAME)
	@echo "--- Installing $(LIB_NAME) to tModLoader Mods directory ---"
	# 1. Ensure the destination directory exists
	@mkdir -p $(TML_MOD_DIR)
	# 2. Copy the generated dylib to the Mods folder
	cp $(LIB_NAME) $(TML_MOD_DIR)/
	@echo "Installed $(LIB_NAME) successfully to $(TML_MOD_DIR)"

# Clean target
clean:
	@echo "--- Cleaning build artifacts ---"
	@rm -f $(LIB_NAME)
	@echo "Cleaned build artifacts"