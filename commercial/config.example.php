<?php
// Copy to config.local.php OUTSIDE public_html. Never put secrets in public files.
return [
    'environment' => 'production',
    'brand' => 'Ansora AI',
    'base_url' => 'https://YOUR-DOMAIN.example',
    'support_email' => 'support@YOUR-DOMAIN.com',
    'business_name' => '',
    'business_address' => '',
    'dsn' => 'mysql:host=localhost;dbname=CPANEL_DATABASE;charset=utf8mb4',
    'db_user' => 'CPANEL_DATABASE_USER', 'db_password' => '',
    'app_secret' => '', // Generate: php -r "echo bin2hex(random_bytes(32));"
    'admin_emails' => [], // Owner email only. Verified email login still required.
    'mail_mode' => 'sendgrid', 'mail_from' => '', 'sendgrid_api_key' => '', 'sendgrid_from_name' => 'Ansora AI', // Key and From come only from the private .env.
    'mail_from_name' => 'Ansora AI',
    'smtp_host' => '', 'smtp_port' => '587', 'smtp_encryption' => 'tls',
    'smtp_username' => '', 'smtp_password' => '', // SMTP values loaded from the private .env.
    'azure_url' => '', 'azure_key' => '', 'azure_model' => 'gpt-oss-120b',
    'azure_model_family' => '', // Set gpt-4.1-mini for aliases using that model; enables 320/1600/1500 output ceilings.
    'azure_fallback_enabled' => false, 'azure_fallback_url' => '', 'azure_fallback_key' => '', 'azure_fallback_model' => 'gpt-oss-120b',
    'azure_first_text_timeout' => 8, 'azure_fallback_first_text_timeout' => 20,
    'azure_fallback_cooldown' => 30, 'azure_fallback_max_tokens' => 4096,
    'razorpay_key_id' => '', 'razorpay_key_secret' => '', 'razorpay_webhook_secret' => '', // Loaded only from environment / private .env.
    'sales_enabled' => false, // Enable only after legal/business fields, live payments and download are ready.
    'pack_tokens' => 1000000, 'pack_price_paise' => 59900,
    'max_output_tokens' => 4096, 'upstream_timeout' => 55,
    'download_file' => '', // Absolute path OUTSIDE public_html to the commercial setup EXE (no Azure key).
    'download_name' => 'AnsoraAI-Setup.exe',
    'privacy_version' => '2026-10-08',
];
