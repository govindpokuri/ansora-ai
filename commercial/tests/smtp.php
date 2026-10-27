<?php
declare(strict_types=1);
require dirname(__DIR__).'/src/Core.php';
use CueMitra\SmtpMailer;
$c=['brand'=>'Ansora AI','smtp_host'=>'smtp.zeptomail.com','smtp_port'=>'587','smtp_encryption'=>'tls','smtp_username'=>'test-user','smtp_password'=>'not-a-real-password','mail_from'=>'noreply@example.com'];
function expect(bool $ok,string $message): void { if(!$ok)throw new RuntimeException($message); }
expect(SmtpMailer::ready($c),'Valid TLS config rejected.');
expect(SmtpMailer::ready(array_replace($c,['smtp_encryption'=>'ssl','smtp_port'=>'465'])),'Valid implicit TLS config rejected.');
foreach(['smtp_host'=>'smtp.example.com/path','smtp_port'=>'0','smtp_encryption'=>'none','smtp_password'=>'','mail_from'=>"a@example.com\r\nBcc: x@example.com"] as $key=>$value)expect(!SmtpMailer::ready(array_replace($c,[$key=>$value])),'Invalid '.$key.' accepted.');
$plain="Code: 123456\nUnicode: ✓"; $html='<p>123456 ✓</p>';
$mail=SmtpMailer::message($c,'recipient@example.com',"Sign in\r\nBcc: injected@example.com",$plain,$html);
expect(!str_contains($mail,"\r\nBcc:"),'Header injection.');
expect(!str_contains($mail,$c['smtp_password']),'Credential in message.');
expect(str_contains($mail,chunk_split(base64_encode($plain),76,"\r\n")),'Plain MIME part missing.');
expect(str_contains($mail,chunk_split(base64_encode($html),76,"\r\n")),'HTML MIME part missing.');
expect(!preg_match('/(?<!\r)\n/',$mail),'MIME has bare LF.');
try { SmtpMailer::message($c,"recipient@example.com\r\nRCPT TO:<other@example.com>",'Test',$plain,$html); throw new RuntimeException('Recipient injection accepted.'); } catch(CueMitra\Problem $e) { expect($e->status===503,'Incorrect status.'); }
echo "SMTP configuration, MIME and injection checks passed.\n";
