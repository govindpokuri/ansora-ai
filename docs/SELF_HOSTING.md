# Self-hosting

Direct Community desktop mode does not need a backend. This service is optional
for operators who want their own accounts, token usage and billing.

## Local development

1. Install PHP 8.3+ with PDO MySQL, cURL and OpenSSL, plus MySQL/MariaDB.
2. Create an isolated database named `cuemitra_test` and a development DB user.
3. Run `php commercial/bin/development-config.php`. It creates ignored private
   config with a random app secret, localhost URL, sales disabled and log mail.
   It refuses to replace an existing file.
4. Edit commercial/config.local.php's DSN, database credentials and port for
   that isolated DB. The generator defaults to port 3317; change it if needed.
   Configure your own AI deployment in private commercial/.env if needed.
5. Import tables: `php commercial/bin/setup.php`.
6. Start the development server from the repository root:

   ```sh
   php -S 127.0.0.1:8181 -t commercial/public commercial/dev-router.php
   ```

7. Open http://127.0.0.1:8181/. Development OTPs are written to
   commercial/storage/outbox/, with no email delivery. Never expose log mail or
   this development server to the Internet. Add your development email to
   admin_emails to test admin access after verifying the code.
8. Run `php commercial/tests/run.php` only against that isolated database.
   Other scripts in commercial/tests exercise specific subsystems; read their
   prerequisites first. Never point tests at production data.

PHPMailer source and its LGPL license are bundled in commercial/src. The SMTP
path does not need another machine's vendor folder. Composer manifests are
retained; run composer install when using Composer-managed dependencies.

## Production / cPanel

1. Use PHP 8.3+, PDO MySQL, cURL, OpenSSL and HTTPS. Test outbound connections
   and streaming timeout/buffering limits on the hosting plan.
2. Place private backend files outside the document root, for example
   /home/YOUR_ACCOUNT/ansora-private/: bootstrap.php, src/, database/, bin/,
   config.example.php, config.local.php and private .env. Do not upload tests.
3. Put only commercial/public/ contents into public_html/.
4. Set the server's `CUEMITRA_APP_ROOT` to the absolute private directory.
   Alternatively adjust public/api.php's default path (legacy: cuemitra-private).
5. Create a dedicated database/user, configure the DSN/credentials, and import
   database/schema.sql using phpMyAdmin or the backend CLI setup script.
6. Set environment=production, your HTTPS base_url, your admin emails, your
   business/support details, and your mail provider. Generate an independent
   app secret of at least 32 characters:

   ```sh
   php -r "echo bin2hex(random_bytes(32)), PHP_EOL;"
   ```

7. Configure your own AI and email keys in the private .env. Keep provider keys
   off clients and installers. Configure backup deployments separately.
8. Replace example.com URLs, canonical tags, robots/sitemap, prices, policies,
   business information and promotional content with your deployment's values.
   The example does not include a promotional video or commercial installer.
9. Optional Razorpay: configure your own account/secret and webhook URL
   `https://YOUR-DOMAIN/api/payments/webhook`. Implemented events are
   payment.captured, refund.created and payment.refunded. Use test credentials
   first; check signature verification, capture status, idempotency and wallet
   crediting before enabling sales. An order alone is not a completed payment.
10. Point downloads to a build you produced without credentials. The example
    update manifest is disabled. Configure your own manifest and installer
    before enabling update distribution.
11. Verify sign-in, admin authorization, balances, exhausted-credit rejection,
    AI streaming, payment/webhook behavior and logout before admitting users.

Back up your DB and secure private files. This source release does not certify
production security or compatibility with every shared-hosting plan.
