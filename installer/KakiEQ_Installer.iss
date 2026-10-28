#define MyAppName "Kaki EQ"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "CFJyA"
#define MyAppExeName "KakiEQ_WinUI.exe"

[Setup]
AppId={{5D0B1F45-8B2A-4D78-9B21-884A496B5094}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=KakiEQ_Setup_v1.0
SetupIconFile=..\KakiEQ_WinUI\Assets\Kaki_Oficial_Blanco.ico
UninstallDisplayIcon={app}\Assets\Kaki_Oficial_Blanco.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
ChangesAssociations=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "fileassoc"; Description: "Asociar archivos de audio (.mp3, .flac, .wav, .m4a, .aac, .ogg, .opus, .wma)"; GroupDescription: "Asociaciones de archivo:"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\Kaki_Oficial_Blanco.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\Assets\Kaki_Oficial_Blanco.ico"

[Registry]
; File type association definition
Root: HKA; Subkey: "Software\Classes\KakiEQ.AudioFile"; ValueType: string; ValueName: ""; ValueData: "Archivo de Audio Kaki EQ"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\KakiEQ.AudioFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Assets\Kaki_Oficial_Blanco.ico,0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\KakiEQ.AudioFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc

; Associate audio formats
Root: HKA; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "KakiEQ.AudioFile"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "KakiEQ.AudioFile"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "KakiEQ.AudioFile"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "KakiEQ.AudioFile"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "KakiEQ.AudioFile"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "KakiEQ.AudioFile"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.opus\OpenWithProgids"; ValueType: string; ValueName: "KakiEQ.AudioFile"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.wma\OpenWithProgids"; ValueType: string; ValueName: "KakiEQ.AudioFile"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\Assets\Driver\nefconc.exe"; Parameters: "remove ""ROOT\VirtualAudioDriver"""; Flags: runhidden; RunOnceId: "RemoveKakiVirtualAudioDriver"
