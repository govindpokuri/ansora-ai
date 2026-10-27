# Community releases

1. Build/test on Windows from a fresh checkout without production config.
2. Run scripts/check-public-source.py and a Gitleaks scan before committing.
3. Run the self-contained dotnet publish command in README.md.
4. Keep defaults in direct mode with placeholders. Never include
   test-connection.json, DPAPI files, commercial.mode, resumes or call records.
5. Include applicable dependency notices. Do not redistribute Microsoft fonts or
   downloaded model weights without checking their terms.
6. Archive the output, include prerequisites/setup instructions and a SHA-256
   checksum, then attach it to a versioned GitHub Release. Do not commit binaries.
7. Keep signing certificates/passwords in secure release secrets. New unsigned
   binaries can trigger SmartScreen; do not ask users to disable security tools.

## Reproduce the portable preview

Run `./scripts/build-community-release.ps1` from a Windows checkout with the
.NET SDK. It publishes a self-contained Windows x64 app, copies the reviewed
setup/model scripts and package license notices, checks Community defaults and
forbidden private files, then creates a ZIP and SHA256SUMS.txt under ignored
artifacts/releases/. The model is downloaded separately on first use.

Use a fresh output directory for a rebuild, for example:

```powershell
./scripts/build-community-release.ps1 -OutputDirectory ./artifacts/rebuild-01
```

The release builder refuses to replace an existing bundle directory. Compare
results and rerun tests before publishing. Hashes may differ across SDK/runtime
patches; publish the checksum for the actual archive uploaded.

The repository does not include a prebuilt signed installer. The downloadable
ZIP is an unsigned Community preview and does not change existing paid installs
or their update feed. Read docs/WINDOWS_QUICKSTART.md for runtime requirements.
