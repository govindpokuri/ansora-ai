<?php
declare(strict_types=1);
namespace CueMitra;

// Append-only receipt outbox in the existing audit table. No schema migration.
// The durable claim prevents automatic re-sends after an ambiguous provider timeout.
final class PaymentEmails {
    private ?\Closure $transport;
    public function __construct(private Store $s,private array $c,?callable $transport=null) {
        $this->transport=$transport===null?null:\Closure::fromCallable($transport);
    }
    private static function key(string $purchase,string $stage): string { return substr(hash('sha256','payment-receipt:'.$purchase.':'.$stage),0,32); }
    private function event(string $purchase,string $actor,string $stage,array $details=[]): void {
        $this->s->run('INSERT INTO audit(id,actor_id,action,target_id,details,created_at) VALUES (?,?,?,?,?,?)',
            [self::key($purchase,$stage),$actor,'payment_email_'.$stage,$purchase,json_encode($details,JSON_THROW_ON_ERROR),time()]);
    }
    public function queue(string $purchase,string $actor,array $data): void {
        if(!$this->s->db->inTransaction())throw new \LogicException('Receipt queueing must be in the purchase transaction.');
        $this->event($purchase,$actor,'queued',$data+['purchase_id'=>$purchase]);
    }
    public function statuses(array $purchases): array {
        $result=[];$keys=[];
        foreach($purchases as $id) { $result[$id]='not_queued';foreach(['queued','claimed','accepted','unconfirmed'] as $stage)$keys[]=self::key($id,$stage); }
        if(!$keys)return $result;
        $rows=$this->s->all('SELECT target_id,action FROM audit WHERE id IN ('.implode(',',array_fill(0,count($keys),'?')).')',$keys);
        $events=[];foreach($rows as $row)$events[$row['target_id']][$row['action']]=true;
        foreach($events as $id=>$found) {
            $result[$id]=isset($found['payment_email_accepted'])?'accepted':(isset($found['payment_email_unconfirmed'])?'unconfirmed':(isset($found['payment_email_claimed'])?'pending':'queued'));
        }
        return $result;
    }
    public function sendOnce(string $purchase): array {
        $queued=$this->s->one('SELECT * FROM audit WHERE id=?',[self::key($purchase,'queued')]);
        if(!$queued)return ['status'=>'not_queued']; // Never email historical purchases on replay.
        $claimed=$this->s->tx(function() use($purchase,$queued) {
            $this->s->one('SELECT id FROM audit WHERE id=? FOR UPDATE',[$queued['id']]);
            if($this->s->one('SELECT id FROM audit WHERE id=?',[self::key($purchase,'claimed')]))return false;
            $this->event($purchase,$queued['actor_id'],'claimed');return true;
        });
        if(!$claimed)return ['status'=>$this->statuses([$purchase])[$purchase]];
        try {
            $data=json_decode($queued['details'],true,512,JSON_THROW_ON_ERROR);
            $mail=self::content($this->c,$data);
            if($this->transport!==null)($this->transport)($data['email'],$mail);
            else $this->deliver($data['email'],$mail,$purchase);
            $this->event($purchase,$queued['actor_id'],'accepted');
            return ['status'=>'accepted'];
        } catch(\Throwable $e) {
            // Provider acceptance may be unknown; do not automatically send another copy.
            try { $this->event($purchase,$queued['actor_id'],'unconfirmed',['error_type'=>get_class($e)]); } catch(\Throwable $ignored) {}
            error_log('Ansora payment receipt status unconfirmed: '.get_class($e));
            return ['status'=>'unconfirmed'];
        }
    }
    public static function content(array $c,array $data): array {
        $escape=static fn(string $value): string=>htmlspecialchars($value,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
        $brand=str_replace(["\r","\n"],'',(string)($c['brand']??'Ansora AI'));
        $base=rtrim((string)($c['base_url']??''),'/');
        if(!filter_var($base,FILTER_VALIDATE_URL) || !in_array(parse_url($base,PHP_URL_SCHEME),['http','https'],true))$base='https://www.ansora-ai.com';
        $dashboard=$base.'/dashboard';$logo=$escape($base.'/assets/ansora-mark.png');
        $support=filter_var($c['support_email']??'',FILTER_VALIDATE_EMAIL)?$c['support_email']:'contact@ansora-ai.com';
        $amount=number_format((int)$data['amount']/100,2,'.',',');$tokens=number_format((int)$data['tokens']);
        $reference=strtoupper(substr((string)$data['purchase_id'],0,12));
        $date=(new \DateTimeImmutable('@'.(int)$data['approved_at']))->setTimezone(new \DateTimeZone('Asia/Kolkata'))->format('d M Y, h:i A').' IST';
        $email=(string)$data['email'];$pack=(string)$data['pack'];
        $subject='Payment confirmed — '.$brand.' | INR '.$amount;
        $plain="Thank you for purchasing $brand!\n\nYour UPI payment has been verified and your AI tokens are available.\nAmount: INR $amount\nPack: $pack\nTokens added: $tokens\nAccount email: $email\nReceipt reference: $reference\nApproved: $date\n\nGet started:\n1. Open $dashboard and sign in with $email using an email code.\n2. Download the latest Windows installer from your dashboard and run it.\n3. Open $brand and sign in with the same email. If it is already open, refresh your account balance.\n4. Add and review your resume and coding languages, or skip this step for now.\n5. Select the call audio source and click Start listening. Use assistance where permitted.\n\nInput and output AI usage count toward your token balance.\nNeed help? $support\n\nThis is a payment confirmation, not a tax invoice.\n";
        $b=$escape($brand);$e=$escape($email);$p=$escape($pack);$d=$escape($date);$url=$escape($dashboard);$help=$escape($support);
        $html=<<<HTML
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Payment confirmed</title></head>
<body style="margin:0;background:#eef4f6;color:#172832;font-family:Arial,Helvetica,sans-serif;">
<div style="display:none;max-height:0;overflow:hidden;opacity:0;">Payment confirmed. {$tokens} AI tokens have been added to your {$b} account.</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:28px 12px;"><table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;table-layout:fixed;background:white;border-radius:18px;overflow:hidden;">
<tr><td style="background:#102630;padding:25px 30px;color:#fff;"><img src="{$logo}" width="38" height="38" alt="" style="vertical-align:middle;margin-right:10px;"><strong style="font-size:23px;vertical-align:middle;">{$b}</strong></td></tr>
<tr><td style="padding:32px 30px 16px;"><span style="color:#127865;font-size:12px;font-weight:bold;letter-spacing:1px;">PAYMENT CONFIRMED</span><h1 style="font-size:28px;line-height:1.2;margin:12px 0;">Your tokens are ready.</h1><p style="font-size:16px;line-height:1.7;">Thank you for purchasing {$b}! Your UPI payment has been verified and <strong>{$tokens} AI tokens</strong> have been added to your account.</p></td></tr>
<tr><td style="padding:0 30px;"><table role="presentation" width="100%" cellpadding="9" cellspacing="0" style="table-layout:fixed;background:#f1f7f6;border-radius:12px;font-size:14px;"><tr><td>Amount received</td><td align="right"><strong>₹{$amount}</strong></td></tr><tr><td>Token pack</td><td align="right">{$p}</td></tr><tr><td>AI tokens added</td><td align="right"><strong>{$tokens}</strong></td></tr><tr><td>Account</td><td align="right" style="word-break:break-all;">{$e}</td></tr><tr><td>Receipt reference</td><td align="right">{$reference}</td></tr><tr><td>Approved</td><td align="right">{$d}</td></tr></table></td></tr>
<tr><td style="padding:26px 30px 10px;"><h2 style="font-size:20px;margin:0 0 12px;">Start using {$b}</h2><ol style="padding-left:21px;font-size:15px;line-height:1.8;"><li>Open your dashboard and sign in with <strong style="word-break:break-all;">{$e}</strong>.</li><li>Download the latest <strong>Windows installer</strong> and run it.</li><li>Open {$b} and sign in with the same email. Already open? Refresh your account balance.</li><li>Add and review your resume and coding languages, or skip for now.</li><li>Select the call audio source and click <strong>Start listening</strong>. Use assistance where permitted.</li></ol></td></tr>
<tr><td align="center" style="padding:10px 30px 28px;"><a href="{$url}" style="display:inline-block;background:#127865;color:white;text-decoration:none;font-size:16px;font-weight:bold;padding:15px 25px;border-radius:10px;">Open my dashboard →</a></td></tr>
<tr><td style="padding:22px 30px;background:#f5f8fa;color:#536772;font-size:12px;line-height:1.8;">Input and output AI usage count toward your token balance.<br>Need help? <a href="mailto:{$help}" style="color:#127865;">{$help}</a><br>This is a payment confirmation, not a tax invoice.</td></tr>
</table></td></tr></table></body></html>
HTML;
        return ['subject'=>$subject,'plain'=>$plain,'html'=>$html];
    }
    private function deliver(string $email,array $mail,string $purchase): void {
        if(!filter_var($email,FILTER_VALIDATE_EMAIL))throw new Problem(503,'Receipt recipient is invalid.');
        $c=$this->c;$mode=$c['mail_mode']??'';
        if(($c['environment']??'')==='development' && ($mode==='log' || str_ends_with($email,'@example.test'))) {
            $directory=dirname(__DIR__).'/storage/payment-outbox';
            if(!is_dir($directory) && !mkdir($directory,0700,true) && !is_dir($directory))throw new \RuntimeException('Receipt outbox is unavailable');
            $file=$directory.'/'.$purchase.'.json';
            if(file_put_contents($file,json_encode(['email'=>$email]+$mail,JSON_THROW_ON_ERROR),LOCK_EX)===false)throw new \RuntimeException('Receipt could not be recorded');
            chmod($file,0600);return;
        }
        if($mode==='zeptomail') { ZeptoMailer::send($c,$email,$mail['subject'],$mail['plain'],$mail['html']);return; }
        if($mode==='smtp') { SmtpMailer::send($c,$email,$mail['subject'],$mail['plain'],$mail['html']);return; }
        if($mode==='sendgrid') {
            $key=(string)($c['sendgrid_api_key']??'');$from=(string)($c['mail_from']??'');
            if($key==='' || !filter_var($from,FILTER_VALIDATE_EMAIL))throw new Problem(503,'Receipt email is not configured.');
            $body=['personalizations'=>[['to'=>[['email'=>$email]]]],'from'=>['email'=>$from,'name'=>$c['sendgrid_from_name']??$c['brand']],'subject'=>$mail['subject'],'content'=>[['type'=>'text/plain','value'=>$mail['plain']],['type'=>'text/html','value'=>$mail['html']]]];
            $curl=curl_init('https://api.sendgrid.com/v3/mail/send');
            curl_setopt_array($curl,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>20,CURLOPT_CONNECTTIMEOUT=>10,CURLOPT_FOLLOWLOCATION=>false,CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>json_encode($body,JSON_THROW_ON_ERROR),CURLOPT_HTTPHEADER=>['Authorization: Bearer '.$key,'Content-Type: application/json']]);
            $response=curl_exec($curl);$status=(int)curl_getinfo($curl,CURLINFO_RESPONSE_CODE);curl_close($curl);
            if($response===false || $status<200 || $status>=300)throw new Problem(503,'Receipt email status is unconfirmed.');return;
        }
        if($mode==='mail') {
            $from=(string)($c['mail_from']??'');if(!filter_var($from,FILTER_VALIDATE_EMAIL))throw new Problem(503,'Receipt email is not configured.');
            $boundary='ansora-'.bin2hex(random_bytes(8));
            $headers='From: '.$from."\r\nMIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=\"".$boundary.'"';
            $body="--$boundary\r\nContent-Type: text/plain; charset=UTF-8\r\n\r\n".$mail['plain']."\r\n--$boundary\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n".$mail['html']."\r\n--$boundary--";
            if(!mail($email,$mail['subject'],$body,$headers))throw new Problem(503,'Receipt email status is unconfirmed.');return;
        }
        throw new Problem(503,'Receipt email delivery is not configured.');
    }
}
