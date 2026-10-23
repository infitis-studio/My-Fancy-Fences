#ifndef AppVersion
  #define AppVersion "3.8.5"
#endif
#ifndef PublishDir
  #define PublishDir "..\new-build\My Fancy Fences"
#endif
#ifndef InstallerOutputDir
  #define InstallerOutputDir "..\new-build"
#endif

[Setup]
AppId={{A2CC9A40-D764-4FE6-90D5-2734E1657C2B}
AppName=My Fancy Fences
AppVersion={#AppVersion}
AppPublisher=Infitis Studio
AppPublisherURL=https://github.com/infitis-studio/My-Fancy-Fences
DefaultDirName={autopf}\My Fancy Fences
DefaultGroupName=My Fancy Fences
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
UsePreviousPrivileges=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerOutputDir}
OutputBaseFilename=My-Fancy-Fences-v{#AppVersion}-Setup-win-x64
SetupIconFile=..\My Fancy Fences\Assets\my-fancy-fences.ico
UninstallDisplayIcon={app}\My Fancy Fences.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
DisableWelcomePage=no
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=My Fancy Fences
VersionInfoVersion={#AppVersion}.0

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
polish.OptionsTitle=Opcje instalacji
polish.OptionsDescription=Wybierz składniki i sposób uruchamiania programu.
polish.RuntimeMissing=Zainstaluj .NET 10 Desktop Runtime (x64) — wymagane
polish.RuntimeMissingInfo=Nie wykryto .NET 10 Desktop Runtime. Instalator pobierze go z Microsoft do folderu tymczasowego i zainstaluje w tle. Tej opcji nie można odznaczyć.
polish.RuntimePresent=Napraw .NET 10 Desktop Runtime (x64)
polish.RuntimePresentInfo=Zainstalowano wersję %1. Zaznacz opcję, aby pobrać instalator tej wersji i ją naprawić. Bez zaznaczenia .NET pozostanie bez zmian.
polish.RuntimePermissions=Instalacja lub naprawa .NET wymaga internetu i zgody administratora w oknie systemowym.
polish.AutoStart=Uruchamiaj My Fancy Fences przy logowaniu do Windows
polish.Desktop=Utwórz skrót na pulpicie
polish.PreparingRuntime=Przygotowywanie .NET 10 Desktop Runtime
polish.DownloadingRuntime=Pobieranie z Microsoft i sprawdzanie pliku. Proszę czekać…
polish.InstallingRuntime=Instalowanie lub naprawianie .NET w tle. Proszę czekać…
polish.RuntimeError=Nie udało się przygotować .NET 10. Sprawdź połączenie z internetem i zgodę administratora, a następnie spróbuj ponownie.
polish.RuntimeNotDetected=Instalator .NET zakończył pracę, ale nie wykryto wymaganego środowiska. Uruchom ponownie komputer i spróbuj jeszcze raz.
polish.Launch=Uruchom My Fancy Fences
polish.FolderNotWritable=Brak uprawnień do zapisu w wybranym folderze. Wybierz folder użytkownika lub uruchom instalator ponownie i wybierz instalację dla wszystkich użytkowników.
english.OptionsTitle=Installation options
english.OptionsDescription=Choose components and how the application starts.
english.RuntimeMissing=Install .NET 10 Desktop Runtime (x64) — required
english.RuntimeMissingInfo=.NET 10 Desktop Runtime was not detected. Setup will download it from Microsoft to a temporary folder and install it silently. This option cannot be unchecked.
english.RuntimePresent=Repair .NET 10 Desktop Runtime (x64)
english.RuntimePresentInfo=Version %1 is installed. Select this option to download its installer and repair it. Leave it unchecked to keep .NET unchanged.
english.RuntimePermissions=Installing or repairing .NET requires internet access and administrator approval in a Windows prompt.
english.AutoStart=Start My Fancy Fences when signing in to Windows
english.Desktop=Create a desktop shortcut
english.PreparingRuntime=Preparing .NET 10 Desktop Runtime
english.DownloadingRuntime=Downloading from Microsoft and verifying the file. Please wait…
english.InstallingRuntime=Installing or repairing .NET silently. Please wait…
english.RuntimeError=Could not prepare .NET 10. Check your internet connection and administrator approval, then retry.
english.RuntimeNotDetected=The .NET installer finished, but the required runtime was not detected. Restart Windows and try again.
english.Launch=Launch My Fancy Fences
english.FolderNotWritable=The selected folder is not writable. Choose a user folder, or restart Setup and choose installation for all users.

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Prepare-DotNet.ps1"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\My Fancy Fences"; Filename: "{app}\My Fancy Fences.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\My Fancy Fences"; Filename: "{app}\My Fancy Fences.exe"; WorkingDir: "{app}"; Check: DesktopSelected

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "My Fancy Fences"; ValueData: """{app}\My Fancy Fences.exe"""; Flags: uninsdeletevalue; Check: AutoStartSelected
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "My Fancy Fences"; Flags: deletevalue; Check: AutoStartNotSelected

[Run]
Filename: "{app}\My Fancy Fences.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent; Check: CanLaunch

[UninstallDelete]
Type: files; Name: "{app}\installer-managed"

[Code]
var
  OptionsPage: TWizardPage;
  RuntimeCheck, AutoStartCheck, DesktopCheck: TNewCheckBox;
  RuntimeInfo, PermissionsInfo: TNewStaticText;
  RuntimeVersion: String;
  RuntimeDone, RuntimeRestart: Boolean;
  RuntimeProgress: TOutputProgressWizardPage;

function DetectRuntime: String;
var
  Versions: TArrayOfString;
  I, View: Integer;
  Root: Integer;
  Location, Candidate: String;
begin
  Result := '';
  for View := 0 to 1 do begin
    if View = 0 then Root := HKLM32 else Root := HKLM64;
    if not RegQueryStringValue(Root, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Location) then
      Location := ExpandConstant('{commonpf64}\dotnet');
    if RegGetValueNames(Root, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions) then
      for I := 0 to GetArrayLength(Versions) - 1 do begin
        Candidate := Versions[I];
        if (Copy(Candidate, 1, 5) = '10.0.') and (Pos('-', Candidate) = 0) and
           FileExists(AddBackslash(Location) + 'shared\Microsoft.WindowsDesktop.App\' + Candidate + '\PresentationFramework.dll') then
          if (Result = '') or (CompareStr(Candidate, Result) = 0) or
             (StrToIntDef(Copy(Candidate, 6, 10), -1) > StrToIntDef(Copy(Result, 6, 10), -1)) then
            Result := Candidate;
      end;
  end;
end;

function AutoStartSelected: Boolean;
begin
  Result := AutoStartCheck.Checked;
end;

function AutoStartNotSelected: Boolean;
begin
  Result := not AutoStartSelected;
end;

function DesktopSelected: Boolean;
begin
  Result := DesktopCheck.Checked;
end;

function CanLaunch: Boolean;
begin
  Result := not RuntimeRestart;
end;

procedure InitializeWizard;
begin
  RuntimeVersion := DetectRuntime;
  OptionsPage := CreateCustomPage(wpSelectDir, CustomMessage('OptionsTitle'), CustomMessage('OptionsDescription'));
  RuntimeCheck := TNewCheckBox.Create(WizardForm);
  RuntimeCheck.Parent := OptionsPage.Surface;
  RuntimeCheck.SetBounds(0, 0, OptionsPage.SurfaceWidth, ScaleY(24));
  RuntimeCheck.Checked := RuntimeVersion = '';
  RuntimeCheck.Enabled := RuntimeVersion <> '';
  RuntimeInfo := TNewStaticText.Create(WizardForm);
  RuntimeInfo.Parent := OptionsPage.Surface;
  RuntimeInfo.AutoSize := False;
  RuntimeInfo.WordWrap := True;
  RuntimeInfo.SetBounds(ScaleX(20), ScaleY(30), OptionsPage.SurfaceWidth - ScaleX(20), ScaleY(68));
  if RuntimeVersion = '' then begin
    RuntimeCheck.Caption := CustomMessage('RuntimeMissing');
    RuntimeInfo.Caption := CustomMessage('RuntimeMissingInfo');
  end else begin
    RuntimeCheck.Caption := CustomMessage('RuntimePresent');
    RuntimeInfo.Caption := FmtMessage(CustomMessage('RuntimePresentInfo'), [RuntimeVersion]);
  end;
  PermissionsInfo := TNewStaticText.Create(WizardForm);
  PermissionsInfo.Parent := OptionsPage.Surface;
  PermissionsInfo.AutoSize := False;
  PermissionsInfo.WordWrap := True;
  PermissionsInfo.SetBounds(ScaleX(20), ScaleY(104), OptionsPage.SurfaceWidth - ScaleX(20), ScaleY(44));
  PermissionsInfo.Caption := CustomMessage('RuntimePermissions');
  AutoStartCheck := TNewCheckBox.Create(WizardForm);
  AutoStartCheck.Parent := OptionsPage.Surface;
  AutoStartCheck.SetBounds(0, ScaleY(166), OptionsPage.SurfaceWidth, ScaleY(26));
  AutoStartCheck.Caption := CustomMessage('AutoStart');
  AutoStartCheck.Checked := RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'My Fancy Fences');
  DesktopCheck := TNewCheckBox.Create(WizardForm);
  DesktopCheck.Parent := OptionsPage.Surface;
  DesktopCheck.SetBounds(0, ScaleY(202), OptionsPage.SurfaceWidth, ScaleY(26));
  DesktopCheck.Caption := CustomMessage('Desktop');
  DesktopCheck.Checked := GetPreviousData('DesktopShortcut', '1') = '1';
  RuntimeProgress := CreateOutputProgressPage(CustomMessage('PreparingRuntime'), '');
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine + CustomMessage('OptionsTitle') + ':';
  if RuntimeCheck.Checked then Result := Result + NewLine + Space + RuntimeCheck.Caption;
  if AutoStartSelected then Result := Result + NewLine + Space + AutoStartCheck.Caption;
  if DesktopSelected then Result := Result + NewLine + Space + DesktopCheck.Caption;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Directory, Parent, Probe: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then exit;
  Directory := WizardDirValue;
  while not DirExists(Directory) do begin
    Parent := ExtractFileDir(Directory);
    if (Parent = '') or (Parent = Directory) then break;
    Directory := Parent;
  end;
  Probe := AddBackslash(Directory) + '.mff-setup-write-' + IntToStr(Random(2147483647)) + '.tmp';
  Result := SaveStringToFile(Probe, '', False);
  if Result then DeleteFile(Probe)
  else MsgBox(CustomMessage('FolderNotWritable'), mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Parameters, RuntimePath: String;
begin
  Result := '';
  if RuntimeDone then exit;
  if (DetectRuntime <> '') and not RuntimeCheck.Checked then exit;
  RuntimeProgress.Show;
  try
    RuntimeProgress.SetText(CustomMessage('DownloadingRuntime'), '');
    RuntimeProgress.SetProgress(0, 0);
    ExtractTemporaryFile('Prepare-DotNet.ps1');
    RuntimePath := ExpandConstant('{tmp}\windowsdesktop-runtime.exe');
    Parameters := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Prepare-DotNet.ps1') +
      '" -Destination "' + RuntimePath + '"';
    if RuntimeVersion <> '' then Parameters := Parameters + ' -RepairVersion "' + RuntimeVersion + '"';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, Code) then begin
      Result := CustomMessage('RuntimeError') + ' (' + SysErrorMessage(Code) + ')';
      exit;
    end;
    if Code <> 0 then begin
      Result := CustomMessage('RuntimeError');
      exit;
    end;
    RuntimeProgress.SetText(CustomMessage('InstallingRuntime'), '');
    if RuntimeVersion = '' then Parameters := '/install' else Parameters := '/repair';
    Parameters := Parameters + ' /quiet /norestart';
    if not ShellExec('runas', RuntimePath, Parameters, '', SW_HIDE, ewWaitUntilTerminated, Code) then begin
      Result := CustomMessage('RuntimeError') + ' (' + SysErrorMessage(Code) + ')';
      exit;
    end;
    if (Code <> 0) and (Code <> 3010) then begin
      Result := CustomMessage('RuntimeError') + ' (' + IntToStr(Code) + ')';
      exit;
    end;
    RuntimeRestart := Code = 3010;
    if (DetectRuntime = '') and not RuntimeRestart then begin
      Result := CustomMessage('RuntimeNotDetected');
      exit;
    end;
    RuntimeDone := True;
    DeleteFile(RuntimePath);
  finally
    RuntimeProgress.Hide;
  end;
end;

function NeedRestart: Boolean;
begin
  Result := RuntimeRestart;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if DesktopCheck.Checked then SetPreviousData(PreviousDataKey, 'DesktopShortcut', '1')
  else SetPreviousData(PreviousDataKey, 'DesktopShortcut', '0');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    if IsAdminInstallMode then
      SaveStringToFile(ExpandConstant('{app}\installer-managed'), 'allusers', False)
    else
      SaveStringToFile(ExpandConstant('{app}\installer-managed'), 'currentuser', False);
    if not DesktopSelected then DeleteFile(ExpandConstant('{autodesktop}\My Fancy Fences.lnk'));
  end;
end;
