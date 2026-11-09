# Quick Connect ⚡

A lightweight, minimalistic Windows desktop utility for instantly switching and toggling paired Bluetooth devices without opening Windows Settings.

![Quick Connect](app.ico)

---

### Features

- **Minimalist & Native:** Compact fixed-size window (~380×420px), fast startup, low memory footprint.
- **Direct Bluetooth Control:** Uses native Windows Bluetooth APIs (`bthprops.cpl` P/Invoke & WinRT radio status) with zero external third-party dependencies.
- **Connection Verification:** Active polling verification ensuring status transitions to `Connected` only on real handshake, and reporting `Failed to connect` if unresponsive.
- **Audio Profile Switching:** Directly toggles A2DP Audio Sink and Hands-Free Profile (HFP/HSP) connection states.
- **Auto-Refresh:** Periodically updates device connection states and provides an instant one-click Refresh button.

---

### Prerequisites & Tech Stack

- **OS:** Windows 10 (19041+) / Windows 11
- **Platform:** C# / .NET 10 (WPF)
- **Target:** x64

---

### Building from Source

To build a release binary:

```powershell
dotnet build -c Release
```

To publish a self-contained single-file executable:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

The compiled standalone executable will be located at `publish/QuickConnect.exe`.
