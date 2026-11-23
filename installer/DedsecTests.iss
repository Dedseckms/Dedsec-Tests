#ifndef AppVersion
  #define AppVersion "0.1.2"
#endif
#ifndef Architecture
  #define Architecture "x64"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\artifacts\win-x64"
#endif
[Setup]
AppId={{C38D40BC-76DD-482A-9700-95B064920D8E}
AppName=Dedsec Tests
AppVersion={#AppVersion}
AppPublisher=Dedseckms
AppPublisherURL=https://github.com/Dedseckms/Dedsec-Tests
AppSupportURL=https://github.com/Dedseckms/Dedsec-Tests/issues
AppUpdatesURL=https://github.com/Dedseckms/Dedsec-Tests/releases
DefaultDirName={localappdata}\Programs\DedsecTests
DefaultGroupName=Dedsec Tests
PrivilegesRequired=lowest
MinVersion=10.0.14393
#if Architecture == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
OutputDir=..\artifacts\release
OutputBaseFilename=DedsecTests-{#AppVersion}-Setup-{#Architecture}
SetupIconFile=..\src\Dedsec.Tests\Assets\app.ico
UninstallDisplayIcon={app}\Dedsec.Tests.exe
LicenseFile=..\LICENSE-RU.txt
WizardStyle=modern dark
WizardBackColor=$12100B
WizardImageFile=wizard.bmp
WizardSmallImageFile=small.bmp
WizardImageStretch=yes
WizardImageBackColor=$12100B
WizardSmallImageBackColor=$12100B
DisableWelcomePage=no
DisableProgramGroupPage=yes
Compression=lzma2
SolidCompression=yes
LZMANumBlockThreads=2
AppMutex=Local\DedsecTests
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Dedsec Tests
SetupLogging=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Dedsec Tests"; Filename: "{app}\Dedsec.Tests.exe"
Name: "{autodesktop}\Dedsec Tests"; Filename: "{app}\Dedsec.Tests.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Dedsec.Tests.exe"; Description: "Открыть Dedsec Tests"; Flags: nowait postinstall skipifsilent
[Code]
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Font.Color := $55FAC1;
  WizardForm.WelcomeLabel1.Caption := 'DEDSEC / TESTS';
  WizardForm.WelcomeLabel2.Caption := 'Диагностика Windows. Понятные причины. Твой контроль.' + #13#10 + #13#10 + 'Программа проверяет систему, объясняет события Windows и помогает измерить CPU, GPU, RAM и накопители.' + #13#10 + #13#10 + 'Локальная работа по умолчанию. Нагрузка и публикация требуют отдельных согласий.';
  WizardForm.FinishedHeadingLabel.Font.Color := $55FAC1;
end;
