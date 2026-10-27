<?php
if(PHP_SAPI!=='cli')exit(1);
$file=dirname(__DIR__).'/config.local.php';
if(is_file($file)) { fwrite(STDERR,"Configuration already exists; it was not overwritten.\n"); exit(1); }
$c=require dirname(__DIR__).'/config.example.php';
$c=array_replace($c,['environment'=>'development','base_url'=>'http://127.0.0.1:8181','support_email'=>'support@example.test',
    'dsn'=>'mysql:host=127.0.0.1;port=3317;dbname=cuemitra_test;charset=utf8mb4','db_user'=>'root','db_password'=>'',
    'app_secret'=>bin2hex(random_bytes(32)),'admin_emails'=>['owner@example.test'],'mail_mode'=>'log','mail_from'=>'accounts@example.test',
    'razorpay_webhook_secret'=>bin2hex(random_bytes(32))]);
file_put_contents($file,"<?php\nreturn ".var_export($c,true).";\n");echo "Local development configuration created. No secrets printed.\n";
