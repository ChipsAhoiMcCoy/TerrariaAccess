; ============================================================================
; Terraria Access - Inno Setup Installer Script
; ============================================================================
; Builds a single Setup.exe that installs:
;   - Screen reader DLLs, license, and documentation into the tModLoader Steam directory
;   - TerrariaAccess.tmod + enabled.json into the tModLoader Mods folder
;   - Input profiles into the tModLoader user data folder
;
; Prerequisites:
;   - Inno Setup 6.x (https://jrsoftware.org/isinfo.php)
;   - All distributable files staged in Release/files/
;
; To compile:
;   - Open this file in Inno Setup Compiler and click Build
;   - Or run: ISCC.exe TerrariaAccess.iss
;   - Output goes to Release/Output/TerrariaAccessSetup.exe
; ============================================================================

#define MyAppName "Terraria Access"
#define MyAppVersion "0.1.10"
#define MyAppPublisher "ChipsAhoiMcCoy"
#define MyAppURL "https://github.com/ChipsAhoiMcCoy/TerrariaAccess"

[Setup]
AppId={{B7E3F2A1-9C84-4D5E-A6B0-1234567890AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
DefaultDirName={code:GetTModLoaderPath}
; No Start Menu group needed - this is a mod, not a standalone app
DisableProgramGroupPage=yes
DisableDirPage=no
DirExistsWarning=no
; License shown during install
LicenseFile=files\tmodloader\THIRD-PARTY-LICENSES.txt
; Output location
OutputDir=Output
OutputBaseFilename=TerrariaAccessSetup
; Compression
Compression=lzma2
SolidCompression=yes
; Require admin only if Steam is in Program Files (auto-detect)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Installer appearance
WizardStyle=modern
; Uninstall support
Uninstallable=yes
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Welcome to the {#MyAppName} Installer
WelcomeLabel2=This will install {#MyAppName} version {#MyAppVersion}, an accessibility mod that makes Terraria playable for blind and low-vision players.%n%nRequirements:%n- Terraria and tModLoader must be installed via Steam%n- A supported screen reader (NVDA, JAWS, etc.) should be running%n%nClick Next to continue.
SelectDirLabel3=The installer has detected your tModLoader installation below. If this is incorrect, click Browse to select the correct folder.
SelectDirBrowseLabel=The folder must contain tModLoader.dll to be valid.
FinishedLabelNoIcons=Setup has finished installing {#MyAppName}.%n%nYou can now launch tModLoader from Steam. The mod will be enabled automatically.%n%nMake sure your screen reader is running before starting the game.

; ============================================================================
; Files
; ============================================================================
; Screen reader DLLs and license -> tModLoader Steam directory
[Files]
Source: "files\tmodloader\Tolk.dll";                    DestDir: "{app}"; Flags: ignoreversion
Source: "files\tmodloader\nvdaControllerClient64.dll";   DestDir: "{app}"; Flags: ignoreversion
Source: "files\tmodloader\SAAPI64.dll";                  DestDir: "{app}"; Flags: ignoreversion
Source: "files\tmodloader\THIRD-PARTY-LICENSES.txt";     DestDir: "{app}"; Flags: ignoreversion
Source: "files\tmodloader\Terraria Access Documentation.html"; DestDir: "{app}"; Flags: ignoreversion

; Mod file and enabled list -> Documents/My Games/Terraria/tModLoader/Mods/
Source: "files\mods\TerrariaAccess.tmod";  DestDir: "{userdocs}\My Games\Terraria\tModLoader\Mods"; Flags: ignoreversion
Source: "files\mods\enabled.json";          DestDir: "{userdocs}\My Games\Terraria\tModLoader\Mods"; Flags: ignoreversion

; Input profiles -> Documents/My Games/Terraria/tModLoader/
Source: "files\tmodloader-docs\input profiles.json";  DestDir: "{userdocs}\My Games\Terraria\tModLoader"; Flags: ignoreversion

; ============================================================================
; Validation
; ============================================================================
; Verify the selected directory actually contains tModLoader
[Code]

function GetSteamPath: String;
var
  SteamPath: String;
begin
  Result := '';

  // Try 64-bit registry first
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath', SteamPath) then
  begin
    Result := SteamPath;
    Exit;
  end;

  // Try 32-bit registry
  if RegQueryStringValue(HKLM, 'SOFTWARE\Valve\Steam', 'InstallPath', SteamPath) then
  begin
    Result := SteamPath;
    Exit;
  end;
end;

function GetTModLoaderPath(Param: String): String;
var
  SteamPath: String;
  TMLPath: String;
begin
  // Default fallback
  Result := 'C:\Program Files (x86)\Steam\steamapps\common\tModLoader';

  SteamPath := GetSteamPath;
  if SteamPath <> '' then
  begin
    TMLPath := SteamPath + '\steamapps\common\tModLoader';
    if DirExists(TMLPath) then
    begin
      Result := TMLPath;
      Exit;
    end;
  end;

  // Also check common non-default Steam library locations
  if DirExists('C:\Program Files\Steam\steamapps\common\tModLoader') then
  begin
    Result := 'C:\Program Files\Steam\steamapps\common\tModLoader';
    Exit;
  end;

  if DirExists('D:\SteamLibrary\steamapps\common\tModLoader') then
  begin
    Result := 'D:\SteamLibrary\steamapps\common\tModLoader';
    Exit;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  // Validate the selected directory on the directory selection page
  if CurPageID = wpSelectDir then
  begin
    if not FileExists(ExpandConstant('{app}\tModLoader.dll')) then
    begin
      MsgBox('The selected folder does not appear to contain tModLoader.' + #13#10 +
             'Please select the folder where tModLoader is installed.' + #13#10 + #13#10 +
             'This is typically:' + #13#10 +
             'C:\Program Files (x86)\Steam\steamapps\common\tModLoader',
             mbError, MB_OK);
      Result := False;
    end;
  end;
end;

// Create the tModLoader Mods directory if it doesn't exist yet
procedure CurStepChanged(CurStep: TSetupStep);
var
  ModsDir: String;
  TMLDocsDir: String;
begin
  if CurStep = ssInstall then
  begin
    ModsDir := ExpandConstant('{userdocs}\My Games\Terraria\tModLoader\Mods');
    if not DirExists(ModsDir) then
      ForceDirectories(ModsDir);

    TMLDocsDir := ExpandConstant('{userdocs}\My Games\Terraria\tModLoader');
    if not DirExists(TMLDocsDir) then
      ForceDirectories(TMLDocsDir);
  end;
end;
