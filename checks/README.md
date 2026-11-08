# Desktop window recovery checks

Run on Windows with Explorer running:

```powershell
dotnet run --project .\checks\DesktopRecoveryChecks.csproj -c Release
```

The checks create temporary WPF windows and use actual Win32 hide, minimize, style and Z-order operations. They verify rejection of shell minimize commands, recovery from direct native minimization, non-activation, both layer modes, independent companion widgets and intentional hiding. Synthetic layer snapshots cover the case where a raised or recreated desktop sits above a widget. `TaskbarCreated` is sent only to a test window to simulate Explorer's rebuild notification; the user's Explorer process is not restarted. The checks do not send Win+D. Test windows are closed afterwards. Application settings and startup registry entries are not changed.
