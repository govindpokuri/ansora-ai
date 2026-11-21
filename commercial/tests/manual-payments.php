<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
require dirname(__DIR__).'/src/ManualPayments.php';
use CueMitra\{Store,ManualPayments,Problem};
$dsn='mysql:host=127.0.0.1;port=3318;dbname=cuemitra_upi_test;charset=utf8mb4';
if(getenv('ANSORA_UPI_TEST_DSN')!==$dsn) { fwrite(STDERR,"Set ANSORA_UPI_TEST_DSN to the isolated loopback test database on port 3318.\n"); exit(1); }
$c=['dsn'=>$dsn,'db_user'=>'root','db_password'=>'','environment'=>'development','mail_mode'=>'log','brand'=>'Ansora AI','base_url'=>'http://127.0.0.1:8199','support_email'=>'support@example.test'];
$s=new Store($c); $manual=new ManualPayments($s,$c);
if(($argv[1]??'')==='--worker') {
    $fixture=json_decode(file_get_contents($argv[2]),true,512,JSON_THROW_ON_ERROR);
    try { echo json_encode($manual->approve($fixture['actor'],$fixture['input'])); }
    catch(Problem $e) { echo json_encode(['error'=>$e->status]); }
    exit;
}
$passed=0;
function check(bool $ok,string $name): void { global $passed;if(!$ok)throw new RuntimeException('FAIL: '.$name);$passed++;echo 'PASS: '.$name."\n"; }
function reject(callable $fn,int $status,string $name): void { try{$fn();}catch(Problem $e){check($e->status===$status,$name);return;}throw new RuntimeException('FAIL: '.$name); }
$admin=\CueMitra\id();$user=\CueMitra\id();$other=\CueMitra\id();$email='upi-'.$user.'@example.test';
$actor=['id'=>$admin,'role'=>'admin','status'=>'active','session_created'=>time()];
$body=['user_id'=>$user,'email'=>$email,'plan_id'=>'standard','amount_received_paise'=>99900,'bank_reference'=>(string)random_int(100000000000,899999999990),'request_id'=>\CueMitra\id(),'received_at'=>time(),'bank_received'=>true];
$tokens=[];
try {
    foreach([[$admin,'admin-'.$admin.'@example.test','admin'],[$user,$email,'user'],[$other,'other-'.$other.'@example.test','user']] as [$id,$address,$role]) {
        $s->run('INSERT INTO users(id,email,role,created_at) VALUES (?,?,?,?)',[$id,$address,$role,time()]);
        $s->run('INSERT INTO wallets(user_id) VALUES (?)',[$id]);
    }
    $selected=$manual->account(strtoupper($email));check($selected['user']['id']===$user,'email lookup normalizes case and returns the correct account');
    reject(fn()=>$manual->account('missing@example.test'),404,'missing account cannot receive credits');
    reject(fn()=>$manual->approve(array_replace($actor,['role'=>'user']),$body),403,'customers cannot approve payments');
    reject(fn()=>$manual->approve(array_replace($actor,['session_created'=>time()-1000]),$body),401,'approval requires recent administrator verification');
    foreach([
        ['bank_received'=>false],['amount_received_paise'=>59900],['amount_received_paise'=>'99900'],['bank_reference'=>'123'],['plan_id'=>'unlimited'],['received_at'=>time()+600],['request_id'=>'short']
    ] as $change)reject(fn()=>$manual->approve($actor,array_replace($body,$change)),isset($change['plan_id'])?400:422,'invalid approval rejected: '.key($change));
    reject(fn()=>$manual->approve($actor,array_replace($body,['email'=>'other@example.test'])),409,'email/account mismatch cannot credit the wrong customer');
    $s->run('UPDATE users SET email=? WHERE id=?',[strtoupper($email),$user]);
    $result=$manual->approve($actor,$body+['tokens'=>999999999]);
    check($result['credited'] && $s->wallet($user)['available']===1000000,'approved payment credits only the server-owned pack amount');
    check(!$manual->approve($actor,$body)['credited'] && $s->wallet($user)['balance']===1000000,'double tap does not add tokens twice');
    check(!$manual->approve($actor,array_replace($body,['request_id'=>\CueMitra\id()]))['credited'],'a new request ID cannot reuse the same bank reference');
    reject(fn()=>$manual->approve($actor,array_replace($body,['bank_reference'=>(string)((int)$body['bank_reference']+1)])),409,'one approval ID cannot be reused for a different payment');
    reject(fn()=>$manual->approve($actor,array_replace($body,['user_id'=>$other,'email'=>'other-'.$other.'@example.test','request_id'=>\CueMitra\id()])),409,'the same UPI reference cannot credit a second account');
    check((int)$s->one("SELECT COUNT(*) AS n FROM purchases WHERE user_id=? AND status='captured'",[$user])['n']===1,'manual approval is recorded as one captured purchase');
    check((int)$s->one('SELECT COUNT(*) AS n FROM ledger WHERE user_id=?',[$user])['n']===1,'exactly one token-ledger entry is recorded');
    check((int)$s->one("SELECT COUNT(*) AS n FROM audit WHERE actor_id=? AND action='upi_payment_approved'",[$admin])['n']===1,'approval records the administrator and bank-verification audit');
    $s->run("UPDATE users SET status='suspended' WHERE id=?",[$other]);
    reject(fn()=>$manual->approve($actor,array_replace($body,['user_id'=>$other,'email'=>'other-'.$other.'@example.test','request_id'=>\CueMitra\id(),'bank_reference'=>(string)((int)$body['bank_reference']+2)])),409,'suspended accounts cannot receive manual purchases');
    $s->run("UPDATE users SET status='active' WHERE id=?",[$other]);
    foreach([['starter',59900,500000],['plus',249900,3000000]] as $i=>[$plan,$amount,$credit]) {
        $manual->approve($actor,array_replace($body,['plan_id'=>$plan,'amount_received_paise'=>$amount,'bank_reference'=>(string)((int)$body['bank_reference']+3+$i),'request_id'=>\CueMitra\id()]));
        check($s->wallet($user)['balance']===1000000+($i===0?500000:3500000),$plan.' credits the correct token pack');
    }
    $s->run("CREATE TRIGGER upi_test_audit_failure BEFORE INSERT ON audit FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Synthetic audit failure'");
    $before=$s->wallet($user)['balance'];
    $rollback=array_replace($body,['request_id'=>\CueMitra\id(),'bank_reference'=>(string)((int)$body['bank_reference']+6)]);
    try{$manual->approve($actor,$rollback);throw new RuntimeException('Audit failure accepted');}catch(PDOException $e){}
    $s->run('DROP TRIGGER upi_test_audit_failure');
    check($s->wallet($user)['balance']===$before && !$s->one('SELECT id FROM purchases WHERE payment_id=?',['upi_'.$rollback['bank_reference']]),'audit failure rolls back both purchase and wallet credit');
    $raceBefore=$s->wallet($user)['balance']+$s->wallet($other)['balance']; $workers=[]; $files=[];
    try {
        foreach([[$user,$email],[$other,'other-'.$other.'@example.test']] as [$id,$address]) {
            $file=tempnam(sys_get_temp_dir(),'ansora-upi-test-');$files[]=$file;
            file_put_contents($file,json_encode(['actor'=>$actor,'input'=>array_replace($body,['user_id'=>$id,'email'=>$address,'request_id'=>\CueMitra\id(),'bank_reference'=>(string)((int)$body['bank_reference']+8)])]));
            $process=proc_open([PHP_BINARY,__FILE__,'--worker',$file],[0=>['pipe','r'],1=>['pipe','w'],2=>['pipe','w']],$pipes);
            if(!is_resource($process))throw new RuntimeException('Could not start the isolated concurrency worker');
            fclose($pipes[0]);$workers[]=[$process,$pipes];
        }
        $results=[];
        foreach($workers as [$process,$pipes]) {
            $results[]=json_decode(stream_get_contents($pipes[1]),true);$errors=stream_get_contents($pipes[2]);fclose($pipes[1]);fclose($pipes[2]);
            check(proc_close($process)===0 && $errors==='','isolated approval worker exits cleanly');
        }
        check(count(array_filter($results,fn($r)=>($r['credited']??false)===true))===1 && count(array_filter($results,fn($r)=>($r['error']??0)===409))===1,'concurrent approvals for different customers cannot spend the same bank reference twice');
        check($s->wallet($user)['balance']+$s->wallet($other)['balance']===$raceBefore+1000000,'concurrent approval race adds exactly one pack in total');
    } finally { foreach($files as $file)if(is_file($file))unlink($file); }
    $before=$s->wallet($user)['balance'];
    if(isset($argv[1])) {
        if($argv[1]!=='http://127.0.0.1:8199')throw new RuntimeException('Only the isolated HTTP test server is permitted');
        foreach([[$admin,'admin'],[$user,'user']] as [$id,$name]) {
            $tokens[$name]=bin2hex(random_bytes(32));
            $s->run('INSERT INTO sessions(token_hash,user_id,client,expires_at,created_at) VALUES (?,?,?,?,?)',[hash('sha256',$tokens[$name]),$id,'browser',time()+600,time()]);
        }
        $http=static function(string $route,string $token='',?array $input=null,bool $cookie=false,string $origin='http://127.0.0.1:8199'): array {
            $curl=curl_init('http://127.0.0.1:8199/api/'.$route);$headers=['Content-Type: application/json','Origin: '.$origin];
            if($token!=='')$headers[]=($cookie?'Cookie: cuemitra_session=':'Authorization: Bearer ').$token;
            curl_setopt_array($curl,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>10,CURLOPT_HTTPHEADER=>$headers]);
            if($input!==null)curl_setopt_array($curl,[CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>json_encode($input)]);
            $text=curl_exec($curl);$status=(int)curl_getinfo($curl,CURLINFO_RESPONSE_CODE);curl_close($curl);return [$status,json_decode((string)$text,true)];
        };
        check($http('admin/upi/recent')[0]===401,'anonymous sessions cannot read payment history');
        check($http('admin/upi/recent',$tokens['user'])[0]===403,'customer sessions cannot read admin payment history');
        check($http('admin/upi/confirm',$tokens['user'],$body)[0]===403,'customer sessions cannot approve payments through HTTP');
        check($http('admin/upi/confirm',$tokens['admin'],$body,true,'https://untrusted.example')[0]===403,'cross-origin cookie approval is rejected');
        $incoming=array_replace($body,['request_id'=>\CueMitra\id(),'bank_reference'=>(string)((int)$body['bank_reference']+7)]);
        [$status,$paid]=$http('admin/upi/confirm',$tokens['admin'],$incoming,true);
        check($status===200 && $paid['credited'] && $paid['wallet']['available']===$before+1000000,'authenticated same-origin approval credits the wallet immediately');
        check($http('admin/upi/confirm',$tokens['admin'],$incoming,true)[1]['credited']===false,'HTTP replay is idempotent');
        check($http('account',$tokens['user'])[1]['wallet']['available']===$before+1000000,'customer account API immediately reflects the credited tokens');
        check($http('history',$tokens['user'])[1]['purchases'][0]['status']==='captured','customer purchase history includes the manual payment');
        $s->run('UPDATE sessions SET created_at=? WHERE token_hash=?',[time()-1000,hash('sha256',$tokens['admin'])]);
        check($http('admin/upi/confirm',$tokens['admin'],$incoming,true)[0]===401,'stale administrator HTTP sessions cannot approve payments');
    }
} finally {
    try{$s->run('DROP TRIGGER IF EXISTS upi_test_audit_failure');}catch(Throwable $e){}
    foreach([$user,$other,$admin] as $id) {
        $s->run('DELETE FROM audit WHERE actor_id=? OR target_id=?',[$id,$id]);
        foreach(['sessions','ledger','usage_requests','purchases','wallets','users'] as $table)$s->run('DELETE FROM '.$table.' WHERE '.($table==='users'?'id':'user_id').'=?',[$id]);
    }
}
echo "PASS: $passed manual-payment checks.\n";
