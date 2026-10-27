<?php
declare(strict_types=1);
require_once __DIR__.'/src/Core.php';
require_once __DIR__.'/src/Payments.php';
require_once __DIR__.'/src/Ai.php';
function commercial_load_env(string $file): void {
    if(!is_file($file))return;
    $lines=file($file,FILE_IGNORE_NEW_LINES); if($lines===false)return;
    foreach($lines as $line) {
        $line=trim($line); if($line==='' || str_starts_with($line,'#'))continue;
        $eq=strpos($line,'='); if($eq===false)continue;
        $name=trim(substr($line,0,$eq)); $value=trim(substr($line,$eq+1));
        if(strlen($value)>=2 && (($value[0]==='"' && str_ends_with($value,'"')) || ($value[0]==="'" && str_ends_with($value,"'"))))$value=substr($value,1,-1);
        if($name==='' || !preg_match('/^[A-Z0-9_]+$/',$name) || getenv($name)!==false)continue;
        putenv($name.'='.$value); $_ENV[$name]=$value;
    }
}
function commercial_config(): array {
    commercial_load_env(dirname(__DIR__).'/.env'); commercial_load_env(__DIR__.'/.env');
    $path=__DIR__.'/config.local.php'; if(!is_file($path))throw new \CueMitra\Problem(503,'The service has not been configured yet.');
    $c=array_replace(require __DIR__.'/config.example.php',require $path);
    foreach(['azure_url'=>'AZURE_URL','azure_key'=>'AZURE_KEY','azure_model'=>'AZURE_MODEL','azure_model_family'=>'AZURE_MODEL_FAMILY'] as $field=>$env) {
        $value=getenv($env); if(is_string($value) && trim($value)!=='')$c[$field]=trim($value);
    }
    foreach(['azure_fallback_url'=>'AZURE_FALLBACK_URL','azure_fallback_key'=>'AZURE_FALLBACK_KEY','azure_fallback_model'=>'AZURE_FALLBACK_MODEL'] as $field=>$env) {
        $value=getenv($env); if(is_string($value) && trim($value)!=='')$c[$field]=trim($value);
    }
    $enabled=getenv('AZURE_FALLBACK_ENABLED'); if(is_string($enabled))$c['azure_fallback_enabled']=filter_var($enabled,FILTER_VALIDATE_BOOL);
    foreach(['azure_first_text_timeout'=>'AZURE_FIRST_TEXT_TIMEOUT','azure_fallback_first_text_timeout'=>'AZURE_FALLBACK_FIRST_TEXT_TIMEOUT','azure_fallback_cooldown'=>'AZURE_FALLBACK_COOLDOWN','azure_fallback_max_tokens'=>'AZURE_FALLBACK_MAX_TOKENS'] as $field=>$env) {
        $value=getenv($env); if(is_string($value) && trim($value)!=='')$c[$field]=max(0,(int)$value);
    }
    foreach(['razorpay_key_id'=>'RAZORPAY_KEY_ID','razorpay_key_secret'=>'RAZORPAY_KEY_SECRET','razorpay_webhook_secret'=>'RAZORPAY_WEBHOOK_SECRET','sendgrid_api_key'=>'SENDGRID_API_KEY'] as $field=>$env) {
        $value=getenv($env); $c[$field]=is_string($value)?trim($value):'';
    }
    $from=getenv('SENDGRID_FROM'); if(is_string($from) && trim($from)!=='')$c['mail_from']=trim($from);
    $fromName=getenv('SENDGRID_FROM_NAME'); if(is_string($fromName) && trim($fromName)!=='')$c['sendgrid_from_name']=trim($fromName);
    $mode=getenv('MAIL_MODE'); if(is_string($mode) && trim($mode)!=='')$c['mail_mode']=strtolower(trim($mode));
    foreach(['smtp_host'=>'SMTP_HOST','smtp_port'=>'SMTP_PORT','smtp_encryption'=>'SMTP_ENCRYPTION','smtp_username'=>'SMTP_USERNAME','smtp_password'=>'SMTP_PASSWORD'] as $field=>$env) {
        $value=getenv($env); if(is_string($value))$c[$field]=$field==='smtp_password'?$value:trim($value);
    }
    if($c['mail_mode']==='smtp') {
        $from=getenv('SMTP_FROM'); $name=getenv('SMTP_FROM_NAME');
        $c['mail_from']=is_string($from)?trim($from):'';
        $c['mail_from_name']=is_string($name) && trim($name)!==''?trim($name):(string)$c['brand'];
    }
    if(strlen($c['app_secret'])<32)throw new \CueMitra\Problem(503,'Server security configuration is incomplete.');
    $c['zeptomail_api_key']=trim((string)getenv('ZEPTOMAIL_API_KEY'));
    $c['zeptomail_api_key']=preg_replace('/^Zoho-enczapikey\s+/i','',$c['zeptomail_api_key']);
    $c['zeptomail_api_url']=trim((string)getenv('ZEPTOMAIL_API_URL')) ?: 'https://api.zeptomail.in/v1.1/email';
    $c['zeptomail_from']=trim((string)getenv('ZEPTOMAIL_FROM'));
    $c['zeptomail_from_name']=trim((string)getenv('ZEPTOMAIL_FROM_NAME')) ?: (string)$c['brand'];
    if($c['environment']==='production') {
        $mailReady=$c['mail_mode']==='mail' || ($c['mail_mode']==='sendgrid' && $c['sendgrid_api_key']!=='' && filter_var($c['mail_from'],FILTER_VALIDATE_EMAIL))
            || ($c['mail_mode']==='smtp' && \CueMitra\SmtpMailer::ready($c))
            || ($c['mail_mode']==='zeptomail' && \CueMitra\ZeptoMailer::ready($c));
        if(PHP_VERSION_ID<80300 || !str_starts_with($c['base_url'],'https://') || !$mailReady)throw new \CueMitra\Problem(503,'Production configuration is incomplete.');
        if($c['azure_url']!=='' && !str_starts_with($c['azure_url'],'https://'))throw new \CueMitra\Problem(503,'Production AI configuration is incomplete.');
        if(!empty($c['azure_fallback_enabled']) && (!str_starts_with((string)($c['azure_fallback_url']??''),'https://') || empty($c['azure_fallback_key']) || empty($c['azure_fallback_model'])))throw new \CueMitra\Problem(503,'Backup AI configuration is incomplete.');
    }
    return $c;
}
