Ansora AI Local Preview - Windows testing only

This build connects to http://127.0.0.1:8181 on this computer. Keep the
development website/backend running before opening Account. It cannot connect
to another computer's localhost and should not be distributed to customers.

Close the previous app with Ctrl+Alt+Q. Install this setup, then open
Ansora AI Local Preview from the Start menu. Use the same email as on the
local website. Check the inbox and spam folder for the requested sign-in code.
In development log mode no email is sent; the code is in the local outbox.

The local preview keeps its sign-in and settings separate from the customer
application. Purchased tokens are still required for metered AI requests.
No Azure or payment credentials are included in this installer.

Select the call playback device and click Start listening. Use Text & code
to paste a written question. Stop listening before exporting a call report.
Ctrl+Alt+Q closes the app; Ctrl+Alt+H hides/restores it. No tray icon is added.

This preview is unsigned. Uninstall from Windows Settings > Apps using
Ansora AI Local Preview. Use the production installer after deploying the
website/backend at https://example.com.
