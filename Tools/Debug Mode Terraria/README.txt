TerrariaAccess tModLoader Debug Launcher
=======================================

This package launches tModLoader with TerrariaAccess debug logging enabled.

How to use
----------

1. Extract this zip somewhere easy to find.
2. Double-click "Start Debug Mode.bat".
3. Play until the issue happens.
4. Send the tModLoader client.log file back to the person who asked for it.

What it enables
---------------

The launcher sets these environment variables before starting tModLoader:

- SRM_DEBUG_INPUT=1
- SRM_DEBUG_HOTBAR=1

Those settings only apply to the tModLoader process started by this launcher.

Finding tModLoader
------------------

The launcher automatically checks Steam library folders and common Steam install
locations for:

steamapps\common\tModLoader

If it cannot find tModLoader, open "Launch tModLoader Debug.ps1" in Notepad and
set this line near the top:

$TModLoaderPath = ""

For example:

$TModLoaderPath = "D:\SteamLibrary\steamapps\common\tModLoader"

Log locations
-------------

The active client log is usually in one of these locations:

C:\Program Files (x86)\Steam\steamapps\common\tModLoader\tModLoader-Logs\client.log

or:

Documents\My Games\Terraria\tModLoader\Logs\client.log

Send client.log after reproducing the problem.
