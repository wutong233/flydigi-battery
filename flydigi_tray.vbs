Option Explicit
Dim fso, shell, dir, exe, ps1, cmd
Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
exe = dir & "\flydigi_battery_tray.exe"
ps1 = dir & "\flydigi_tray.ps1"
If fso.FileExists(exe) Then
  cmd = """" & exe & """"
  shell.Run cmd, 0, False
ElseIf fso.FileExists(ps1) Then
  cmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & ps1 & """ -Hidden"
  shell.Run cmd, 0, False
Else
  MsgBox "flydigi_battery_tray.exe / flydigi_tray.ps1 not found next to this launcher.", 48, "Flydigi Battery Tray"
  WScript.Quit 1
End If
