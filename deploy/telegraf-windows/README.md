# Native Windows Telegraf

Use Telegraf 1.29 or newer on the Windows host. Host and OS metrics must come from this native service, not from a container or the OpenTelemetry Collector `hostmetrics` receiver.

1. Download the `windows_amd64` Telegraf package.
2. Place `telegraf.exe` and this directory's `telegraf.conf` in `C:\Program Files\Telegraf`.
3. Replace `REPLACE_WITH_RNDFWCORE_SERVICE_NAME` in `telegraf.conf` with the actual rndfwcore/FWCORE Windows service name or names for this deployment.
4. In an elevated Administrator PowerShell session, validate the configuration on the target host:

   ```powershell
   Set-Location "C:\Program Files\Telegraf"
   .\telegraf.exe --config "C:\Program Files\Telegraf\telegraf.conf" --test
   ```

   Windows performance counter names are locale-sensitive and may differ on non-English Windows installations. Run `--test` on the actual target host to catch locale mismatches before installing the service.

5. Install and start the Windows service:

   ```powershell
   .\telegraf.exe --config "C:\Program Files\Telegraf\telegraf.conf" --service install
   Start-Service telegraf
   ```

6. Configure automatic restart after host-level failures:

   ```powershell
   sc.exe failure telegraf reset= 86400 actions= restart/60000/restart/60000/restart/60000
   ```

   This keeps telemetry collection running after a host-level hiccup without requiring a support engineer to restart it manually. The same recovery policy can be set with PowerShell or the Services GUI.
