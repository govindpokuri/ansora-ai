# Configuration

## Direct desktop mode

`OverlayCopilot/settings.defaults.json` contains only non-secret defaults:
`CommercialMode: false`, `AnswerUrl`, `AnswerModel`, `AnswerAuth` (`Bearer` or
`api-key`), `MaxTokens`, `Temperature`, `UseLocalSpeech`.

Use the exact chat-completions URL provided by your deployment, including any
required API-version query. HTTPS is required, except HTTP on localhost.
The desktop consumes `OVERLAYCOPILOT_AI_KEY` at startup, clears the variable in
its process, and saves the key with DPAPI. Prefer run-community.ps1's hidden
prompt. Local Whisper needs no speech API key. Endpoint speech mode can use
`OVERLAYCOPILOT_SPEECH_KEY` with a separately configured speech service.

Saved UI settings override defaults. Community settings are stored under
LocalAppData/AnsoraAICommunity. Do not share that directory or DPAPI files.

## Optional hosted mode

Set `CommercialMode: true` and `CommercialBaseUrl` to your own service in the
defaults beside the executable. The app uses email sign-in; provider credentials
stay on the server. Remote services require HTTPS. Loopback HTTP is for local
development. Hosted accounts still need server-authorized token balances.

## Backend files and variables

Copy `commercial/config.example.php` to `commercial/config.local.php` and
`commercial/.env.example` to `commercial/.env`. Both private files are ignored.
Configure database DSN/user/password, random `app_secret`, `base_url`,
`admin_emails`, `environment`, mail provider and optional sales in private config.

- `AZURE_URL`, `AZURE_KEY`, `AZURE_MODEL`: primary AI deployment.
- `AZURE_MODEL_FAMILY`: explicit model family where an alias needs token limits.
- `AZURE_FALLBACK_*`: separately configured backup deployment; disabled by default.
- `MAIL_MODE`: log for local tests, or sendgrid / smtp / zeptomail in production.
- `RAZORPAY_KEY_ID`, `RAZORPAY_KEY_SECRET`, `RAZORPAY_WEBHOOK_SECRET`: optional billing.
- `sales_enabled`: keep false until your own billing/download setup is verified.

Example-file comments describe remaining fields. The backend's adapter is
Azure-oriented; do not assume every provider supports identical parameters.
