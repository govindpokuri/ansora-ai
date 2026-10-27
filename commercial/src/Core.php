<?php
declare(strict_types=1);
namespace CueMitra;
require_once __DIR__.'/Smtp.php';
require_once __DIR__.'/ZeptoMail.php';

final class Problem extends \RuntimeException {
    public int $status;
    public function __construct(int $status, string $message) { parent::__construct($message); $this->status=$status; }
}
function id(): string { return bin2hex(random_bytes(16)); }
function json(array $data,int $status=200): void { http_response_code($status); header('Content-Type: application/json; charset=utf-8'); echo json_encode($data,JSON_THROW_ON_ERROR); }
function hmac(string $data,string $secret): string { return hash_hmac('sha256',$data,$secret); }

final class Store {
    public \PDO $db;
    public function __construct(array $config) { $this->db=new \PDO($config['dsn'],$config['db_user'],$config['db_password'],[\PDO::ATTR_ERRMODE=>\PDO::ERRMODE_EXCEPTION,\PDO::ATTR_EMULATE_PREPARES=>false]); }
    public function run(string $sql,array $args=[]): \PDOStatement { $q=$this->db->prepare($sql); $q->execute($args); return $q; }
    public function one(string $sql,array $args=[]): ?array { return $this->run($sql,$args)->fetch(\PDO::FETCH_ASSOC) ?: null; }
    public function all(string $sql,array $args=[]): array { return $this->run($sql,$args)->fetchAll(\PDO::FETCH_ASSOC); }
    public function tx(callable $fn) { $this->db->beginTransaction(); try { $result=$fn(); $this->db->commit(); return $result; } catch(\Throwable $e) { if($this->db->inTransaction())$this->db->rollBack(); throw $e; } }
    public function wallet(string $user): array { $w=$this->one('SELECT balance,reserved FROM wallets WHERE user_id=?',[$user]); if(!$w)throw new Problem(404,'Account not found.'); return ['balance'=>(int)$w['balance'],'reserved'=>(int)$w['reserved'],'available'=>max(0,(int)$w['balance']-(int)$w['reserved'])]; }
    public function limit(string $bucket,int $max,int $seconds): void {
        $this->tx(function() use($bucket,$max,$seconds) {
            $this->run('INSERT IGNORE INTO rate_limits(bucket,hits,expires_at) VALUES (?,0,?)',[$bucket,time()+$seconds]);
            $r=$this->one('SELECT hits,expires_at FROM rate_limits WHERE bucket=? FOR UPDATE',[$bucket]);
            if((int)$r['expires_at']<=time()) { $this->run('UPDATE rate_limits SET hits=0,expires_at=? WHERE bucket=?',[time()+$seconds,$bucket]); $r['hits']=0; }
            if((int)$r['hits'] >= $max) throw new Problem(429,'Too many attempts. Please try again later.');
            $this->run('UPDATE rate_limits SET hits=hits+1 WHERE bucket=?',[$bucket]);
        });
    }
    public function credit(string $user,int $tokens,string $reference,string $description,string $actor=''): bool {
        return $this->tx(function() use($user,$tokens,$reference,$description,$actor) {
            $w=$this->one('SELECT * FROM wallets WHERE user_id=? FOR UPDATE',[$user]);
            if(!$w)throw new Problem(404,'Account not found.');
            if($this->one('SELECT id FROM ledger WHERE reference_key=?',[$reference])) return false;
            if((int)$w['balance']+$tokens<(int)$w['reserved'])throw new Problem(409,'Adjustment would spend reserved credits or create a negative balance.');
            $this->run('UPDATE wallets SET balance=balance+? WHERE user_id=?',[$tokens,$user]);
            $this->run('INSERT INTO ledger(id,user_id,delta,reference_key,description,created_at) VALUES (?,?,?,?,?,?)',[id(),$user,$tokens,$reference,$description,time()]);
            if($actor!=='')$this->audit($actor,'credits',$user,['tokens'=>$tokens,'reason'=>$description,'reference'=>$reference]); return true;
        });
    }
    public function reserve(string $user,string $key,string $hash,string $session,string $purpose,int $tokens): string {
        return $this->tx(function() use($user,$key,$hash,$session,$purpose,$tokens) {
            $w=$this->one('SELECT * FROM wallets WHERE user_id=? FOR UPDATE',[$user]);
            $account=$this->one('SELECT status FROM users WHERE id=?',[$user]); if(!$account || $account['status']!=='active')throw new Problem(403,'This account is suspended.');
            if($this->one('SELECT id FROM usage_requests WHERE user_id=? AND request_key=?',[$user,$key]))throw new Problem(409,'This request has already been submitted. It will not be billed twice.');
            if((int)$w['balance']-(int)$w['reserved']<$tokens)throw new Problem(402,'Not enough available tokens. Top up to continue.');
            $this->run('UPDATE wallets SET reserved=reserved+? WHERE user_id=?',[$tokens,$user]); $rid=id();
            $this->run('INSERT INTO usage_requests(id,user_id,request_key,payload_hash,session_id,purpose,reserved,created_at) VALUES (?,?,?,?,?,?,?,?)',[$rid,$user,$key,$hash,$session,$purpose,$tokens,time()]); return $rid;
        });
    }
    public function settle(string $rid,?array $usage,bool $knownFailure=false,string $provider='',string $actor='',string $reason=''): void {
        // Always lock wallet before usage: same lock ordering as reserve/credit.
        $pre=$this->one('SELECT user_id FROM usage_requests WHERE id=?',[$rid]); if(!$pre)throw new Problem(404,'Request not found.');
        $this->tx(function() use($rid,$usage,$knownFailure,$provider,$pre,$actor,$reason) {
            $this->one('SELECT * FROM wallets WHERE user_id=? FOR UPDATE',[$pre['user_id']]);
            $r=$this->one('SELECT * FROM usage_requests WHERE id=? FOR UPDATE',[$rid]);
            if(!in_array($r['status'],['reserved','reconcile'],true))return;
            if($usage===null && !$knownFailure) { $this->run("UPDATE usage_requests SET status='reconcile',provider_id=? WHERE id=?",[$provider,$rid]); return; }
            $total=$usage===null ? 0 : (int)$usage['total_tokens'];
            if($total<0 || $total>(int)$r['reserved']) { $this->run("UPDATE usage_requests SET status='reconcile',provider_id=? WHERE id=?",[$provider,$rid]); return; }
            $this->run('UPDATE wallets SET reserved=reserved-?,balance=balance-? WHERE user_id=?',[(int)$r['reserved'],$total,$r['user_id']]);
            $this->run('UPDATE usage_requests SET status=?,input_tokens=?,output_tokens=?,total_tokens=?,completed_at=?,provider_id=? WHERE id=?',[$knownFailure?'failed':'completed',$usage['prompt_tokens']??0,$usage['completion_tokens']??0,$total,time(),$provider,$rid]);
            $this->run('INSERT INTO ledger(id,user_id,delta,reference_key,description,created_at) VALUES (?,?,?,?,?,?)',[id(),$r['user_id'],-$total,'usage:'.$rid,$knownFailure?'Failed request; reservation released':'AI '.$r['purpose'],time()]);
            if($actor!=='')$this->audit($actor,'reconcile',$rid,['usage'=>$usage,'reason'=>$reason]);
        });
    }
    public function audit(string $actor,string $action,string $target,array $details): void { $this->run('INSERT INTO audit(id,actor_id,action,target_id,details,created_at) VALUES (?,?,?,?,?,?)',[id(),$actor,$action,$target,json_encode($details,JSON_THROW_ON_ERROR),time()]); }
}

final class Auth {
    private Store $s; private array $c;
    public function __construct(Store $s,array $c) { $this->s=$s; $this->c=$c; }
    public function start(string $email,string $ip): array {
        $email=strtolower(trim($email)); if(strlen($email)>254 || !filter_var($email,FILTER_VALIDATE_EMAIL))throw new Problem(422,'Enter a valid email address.');
        $this->s->limit(hmac('start:'.$ip,$this->c['app_secret']),10,600);
        $this->s->limit(hmac('email:'.$email,$this->c['app_secret']),1,60);
        $challenge=bin2hex(random_bytes(32)); $code=str_pad((string)random_int(0,999999),6,'0',STR_PAD_LEFT);
        $this->s->run('INSERT INTO challenges(id,email,code_hash,expires_at,created_at) VALUES (?,?,?,?,?)',[$challenge,$email,hmac($challenge.':'.$code,$this->c['app_secret']),time()+600,time()]);
        $mail=$this->signInMail($code);
        $subject=$mail['subject'];
        $localOnly=$this->c['environment']==='development' && ($this->c['mail_mode']==='log' || str_ends_with($email,'@example.test'));
        if($localOnly) {
            $path=dirname(__DIR__).'/storage/outbox'; if(!is_dir($path))mkdir($path,0700,true);
            file_put_contents($path.'/'.$challenge.'.json',json_encode(['email'=>$email,'code'=>$code]),LOCK_EX);
        } elseif($this->c['mail_mode']==='sendgrid') {
            $this->sendgrid($email,$subject,$mail['plain'],$mail['html']);
        } elseif($this->c['mail_mode']==='smtp') {
            SmtpMailer::send($this->c,$email,$subject,$mail['plain'],$mail['html']);
        } elseif($this->c['mail_mode']==='zeptomail') {
            ZeptoMailer::send($this->c,$email,$subject,$mail['plain'],$mail['html']);
        } else {
            $from=$this->c['mail_from']; if(!filter_var($from,FILTER_VALIDATE_EMAIL))throw new Problem(503,'Email delivery is not configured.');
            $boundary='ansora-'.bin2hex(random_bytes(8));
            $headers='From: '.$from."\r\nMIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=\"".$boundary.'"';
            $body="--".$boundary."\r\nContent-Type: text/plain; charset=UTF-8\r\n\r\n".$mail['plain']."\r\n--".$boundary."\r\nContent-Type: text/html; charset=UTF-8\r\n\r\n".$mail['html']."\r\n--".$boundary."--";
            if(!mail($email,$subject,$body,$headers))throw new Problem(503,'Email delivery failed. Please try again later.');
        }
        return ['challenge'=>$challenge,'message'=>$localOnly?'Development code saved in the local outbox; no email was sent.':'Email request accepted. Check your inbox and spam folder for the six-digit code; it expires in 10 minutes.'];
    }
    private function signInMail(string $code): array {
        $name=str_replace(["\r","\n"],'',(string)$this->c['brand']);
        $brand=htmlspecialchars($name,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
        $safe=htmlspecialchars($code,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
        $base=rtrim((string)($this->c['base_url']??'https://example.com'),'/');
        if(!filter_var($base,FILTER_VALIDATE_URL) || !in_array(parse_url($base,PHP_URL_SCHEME),['http','https'],true))$base='https://example.com';
        $logo=htmlspecialchars($base.'/assets/ansora-mark.png',ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
        $support=(string)($this->c['support_email']??'contact@example.com');
        if(!filter_var($support,FILTER_VALIDATE_EMAIL))$support='contact@example.com';
        $support=htmlspecialchars($support,ENT_QUOTES|ENT_SUBSTITUTE,'UTF-8');
        $subject='Your one-time '.$name.' sign-in code is '.$code.'.';
        $plain="Your one-time code is: ".$code."\n\nEnter this verification code in ".$name." to complete your sign-in.\nThe code expires in 10 minutes and can only be used once. Never share it with anyone.\n\nIf you did not request this code, ignore this email. No action is needed.\nNeed help? ".$support."\n";
        $html=<<<HTML
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Your sign-in code</title></head>
<body style="margin:0;padding:0;background:#f5f5f5;color:#111111;font-family:Arial,Helvetica,sans-serif;">
<div style="display:none;font-size:1px;line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;mso-hide:all;">Your verification code is {$safe}. Use it to sign in to {$brand}. Expires in 10 minutes.</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#f5f5f5"><tr><td align="center" style="padding:24px 16px 40px;">
<!--[if mso]><table role="presentation" width="640" cellpadding="0" cellspacing="0"><tr><td><![endif]-->
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:640px;">
<tr><td style="padding:8px 0 24px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td style="font-size:24px;font-weight:bold;color:#111111;"><img src="{$logo}" alt="" width="44" height="30" style="vertical-align:middle;border:0;">&nbsp; {$brand}</td><td align="right" style="font-size:13px;line-height:20px;color:#555555;">Need help?<br><a href="mailto:{$support}" style="color:#075e63;text-decoration:underline;">Contact us</a></td></tr></table></td></tr>
<tr><td bgcolor="#ffffff" style="padding:36px 28px 40px;background:#ffffff;">
<p style="margin:0 0 16px;font-size:25px;line-height:34px;font-weight:bold;">Your one-time sign-in code is:</p>
<p style="margin:0 0 26px;font-family:Arial,Helvetica,sans-serif;font-size:42px;line-height:54px;font-weight:bold;color:#111111;">{$safe}</p>
<p style="margin:0 0 16px;font-size:16px;line-height:26px;">Enter this verification code in {$brand} to complete your sign-in.</p>
<p style="margin:0 0 28px;font-size:16px;line-height:26px;">This code expires in <strong>10 minutes</strong> and can only be used once. Never share it with anyone.</p>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td style="border-top:1px solid #e6e6e6;padding-top:24px;">
<p style="margin:0 0 8px;font-size:16px;line-height:24px;font-weight:bold;">Didn't request this code?</p>
<p style="margin:0;font-size:14px;line-height:23px;color:#555555;">You can safely ignore this email. No action is needed.</p>
</td></tr></table></td></tr>
<tr><td style="padding:24px 4px 0;font-size:12px;line-height:20px;color:#666666;">Every Question. Your Best Answer.<br>This is an automated sign-in email from {$brand}. For help, contact <a href="mailto:{$support}" style="color:#075e63;">{$support}</a>.</td></tr>
</table><!--[if mso]></td></tr></table><![endif]-->
</td></tr></table></body></html>
HTML;
        return ['subject'=>$subject,'plain'=>$plain,'html'=>$html];
    }
    private function sendgrid(string $email,string $subject,string $plain,string $html): void {
        $key=(string)($this->c['sendgrid_api_key']??''); $from=(string)($this->c['mail_from']??'');
        $name=str_replace(["\r","\n"],'',(string)($this->c['sendgrid_from_name']??$this->c['brand']));
        if($key==='' || !filter_var($from,FILTER_VALIDATE_EMAIL))throw new Problem(503,'Email delivery is not configured.');
        $body=json_encode(['personalizations'=>[['to'=>[['email'=>$email]]]],'from'=>['email'=>$from,'name'=>$name!==''?$name:(string)$this->c['brand']],'subject'=>$subject,'content'=>[['type'=>'text/plain','value'=>$plain],['type'=>'text/html','value'=>$html]]],JSON_THROW_ON_ERROR);
        $curl=curl_init('https://api.sendgrid.com/v3/mail/send');
        curl_setopt_array($curl,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>20,CURLOPT_CONNECTTIMEOUT=>10,CURLOPT_FOLLOWLOCATION=>false,CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>$body,CURLOPT_HTTPHEADER=>['Authorization: Bearer '.$key,'Content-Type: application/json']]);
        $text=curl_exec($curl); $status=(int)curl_getinfo($curl,CURLINFO_RESPONSE_CODE); curl_close($curl);
        if($text===false || $status<200 || $status>=300)throw new Problem(503,'Email delivery failed. Please try again later.');
    }
    public function verify(string $challenge,string $code,string $client,string $ip): array {
        $this->s->limit(hmac('verify:'.$ip,$this->c['app_secret']),30,600);
        $result=$this->s->tx(function() use($challenge,$code,$client) {
            $r=$this->s->one('SELECT * FROM challenges WHERE id=? FOR UPDATE',[$challenge]);
            if(!$r || (int)$r['expires_at']<=time() || (int)$r['attempts']>=5) return ['error'=>true];
            $this->s->run('UPDATE challenges SET attempts=attempts+1 WHERE id=?',[$challenge]);
            if(!preg_match('/^[0-9]{6}$/',$code) || !hash_equals($r['code_hash'],hmac($challenge.':'.$code,$this->c['app_secret'])))return ['error'=>true];
            $this->s->run('DELETE FROM challenges WHERE id=?',[$challenge]);
            $uid=id(); $role=in_array($r['email'],$this->c['admin_emails'],true)?'admin':'member';
            $this->s->run('INSERT IGNORE INTO users(id,email,role,created_at) VALUES (?,?,?,?)',[$uid,$r['email'],$role,time()]);
            $u=$this->s->one('SELECT * FROM users WHERE email=? FOR UPDATE',[$r['email']]);
            if($u['status']!=='active')return ['suspended'=>true];
            $this->s->run('INSERT IGNORE INTO wallets(user_id) VALUES (?)',[$u['id']]);
            $this->s->run('INSERT INTO consents(id,user_id,policy_version,client,accepted_at) VALUES (?,?,?,?,?)',[id(),$u['id'],$this->c['privacy_version'],$client,time()]);
            $this->s->run('UPDATE users SET last_login=? WHERE id=?',[time(),$u['id']]);
            $token=bin2hex(random_bytes(32)); $expires=time()+($client==='desktop'?30:7)*86400;
            $this->s->run('INSERT INTO sessions(token_hash,user_id,client,expires_at,created_at) VALUES (?,?,?,?,?)',[hash('sha256',$token),$u['id'],$client,$expires,time()]);
            return ['token'=>$token,'expires_at'=>$expires,'user'=>['id'=>$u['id'],'email'=>$u['email'],'role'=>$u['role']]];
        });
        if(isset($result['suspended']))throw new Problem(403,'This account is suspended. Contact support.');
        if(isset($result['error']))throw new Problem(401,'Code is invalid, expired, or has too many attempts. Request a new code.');
        return $result;
    }
    public function user(string $token,bool $admin=false): array {
        if(!preg_match('/^[a-f0-9]{64}$/',$token))throw new Problem(401,'Please sign in.');
        $u=$this->s->one('SELECT u.*,s.created_at AS session_created FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=? AND s.expires_at>?',[hash('sha256',$token),time()]);
        if(!$u)throw new Problem(401,'Your session expired. Please sign in again.');
        if($u['status']!=='active')throw new Problem(403,'This account is suspended.');
        if($admin && $u['role']!=='admin')throw new Problem(403,'Administrator access required.');
        return $u;
    }
}
