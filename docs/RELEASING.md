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

The repository does not include a prebuilt signed installer and does not change
existing paid installations or their update feed.
