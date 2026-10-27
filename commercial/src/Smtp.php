<?php
declare(strict_types=1);
namespace CueMitra;
use PHPMailer\PHPMailer\PHPMailer;

final class SmtpMailer {
    public static function ready(array $c): bool {
        $host=(string)($c['smtp_host']??''); $port=(string)($c['smtp_port']??'');
        return preg_match('/\A[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?\z/i',$host)===1
            && ctype_digit($port) && (int)$port>0 && (int)$port<=65535
            && in_array($c['smtp_encryption']??'', ['tls','ssl'],true)
            && ($c['smtp_username']??'')!=='' && ($c['smtp_password']??'')!==''
            && filter_var($c['mail_from']??'',FILTER_VALIDATE_EMAIL)!==false;
    }
    private static function compose(array $c,string $to,string $subject,string $plain,string $html): PHPMailer {
        if(!self::ready($c) || !filter_var($to,FILTER_VALIDATE_EMAIL))throw new Problem(503,'SMTP email delivery is not configured.');
        foreach (['PHPMailerException.php', 'PHPMailer.php', 'PHPMailerSMTP.php'] as $file) {
            $path=__DIR__.'/'.$file;
            if(!is_file($path))throw new Problem(503,'SMTP transport is unavailable on this server.');
            require_once $path;
        }
        $from=(string)$c['mail_from']; $name=(string)($c['mail_from_name']??$c['brand']);
        $name=str_replace(["\r","\n"],'',$name); $subject=str_replace(["\r","\n"],'',$subject);
        $mail=new PHPMailer(true); $mail->isSMTP();
        $mail->Host=$c['smtp_host']; $mail->Port=(int)$c['smtp_port'];
        $mail->SMTPAuth=true; $mail->Username=$c['smtp_username']; $mail->Password=$c['smtp_password'];
        $mail->SMTPSecure=$c['smtp_encryption']==='ssl'?PHPMailer::ENCRYPTION_SMTPS:PHPMailer::ENCRYPTION_STARTTLS;
        $mail->Timeout=30; $mail->Timelimit=30; $mail->SMTPDebug=0;
        $mail->SMTPOptions=['ssl'=>['verify_peer'=>true,'verify_peer_name'=>true,'allow_self_signed'=>false]];
        $ca=(string)ini_get('curl.cainfo');
        if($ca!=='' && is_file($ca))$mail->SMTPOptions['ssl']['cafile']=$ca;
        $mail->Encoding=PHPMailer::ENCODING_BASE64; $mail->CharSet=PHPMailer::CHARSET_UTF8;
        $mail->setFrom($from,$name); $mail->addAddress($to); $mail->isHTML(true);
        $mail->Subject=$subject; $mail->Body=$html; $mail->AltBody=$plain;
        return $mail;
    }
    public static function message(array $c,string $to,string $subject,string $plain,string $html): string {
        $mail=self::compose($c,$to,$subject,$plain,$html); $mail->preSend(); return $mail->getSentMIMEMessage();
    }
    public static function send(array $c,string $to,string $subject,string $plain,string $html): void {
        $mail=self::compose($c,$to,$subject,$plain,$html);
        try { $mail->send(); } catch(\Throwable $e) {
            $error=$mail->getSMTPInstance()->getError();
            // Do not expose authentication data, OTPs or provider transcripts.
            error_log('Ansora SMTP send failed: smtp='.(int)($error['smtp_code']??0));
            throw new Problem(503,'Email delivery failed. Please try again later.');
        }
    }
}
