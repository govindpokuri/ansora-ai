<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
require dirname(__DIR__).'/src/Admin.php';
use CueMitra\{Store,AdminReports,Problem};
$c=commercial_config();
if($c['environment']!=='development' || !str_contains($c['dsn'],'cuemitra_test'))exit("Requires isolated development database.\n");
$s=new Store($c); $reports=new AdminReports($s); $passed=0;
function check(bool $ok,string $name): void { global $passed; if(!$ok)throw new RuntimeException('FAIL: '.$name); $passed++; echo 'PASS: '.$name."\n"; }
$uid=\CueMitra\id(); $other=\CueMitra\id(); $email='admin-report-'.$uid.'@example.test';
try {
    foreach([[$uid,$email],[$other,'other-'.$other.'@example.test']] as [$id,$address]) {
        $s->run('INSERT INTO users(id,email,created_at) VALUES (?,?,?)',[$id,$address,time()]);
        $s->run('INSERT INTO wallets(user_id) VALUES (?)',[$id]);
    }
    foreach([['captured',59900,1000000],['created',99900,2000000]] as [$status,$amount,$tokens]) {
        $s->run('INSERT INTO purchases(id,user_id,order_id,amount,tokens,status,created_at) VALUES (?,?,?,?,?,?,?)',[\CueMitra\id(),$uid,'order_'.\CueMitra\id(),$amount,$tokens,$status,time()]);
    }
    $s->credit($uid,1000000,'fixture:'.$uid,'Captured purchase fixture');
    $s->credit($other,500,'fixture:'.$other,'Other fixture');
    $rid=$s->reserve($uid,\CueMitra\id(),hash('sha256','fixture'),'test','answer',1000);
    $s->settle($rid,['prompt_tokens'=>150,'completion_tokens'=>50,'total_tokens'=>200]);
    $held=$s->reserve($uid,\CueMitra\id(),hash('sha256','pending'),'test','answer',3000);
    $s->settle($held,null);
    $overview=$reports->overview($email); $user=$overview['users'][0];
    check(count($overview['users'])===1 && $user['id']===$uid,'email search isolates selected account');
    check((int)$user['purchase_count']===1 && (int)$user['spent_paise']===59900 && (int)$user['purchased_tokens']===1000000,'only captured purchases count toward paid totals');
    check((int)$user['tokens_used']===200 && (int)$user['reserved']===3000,'completed usage and pending reservations stay separate');
    $detail=$reports->account($uid);
    check(count($detail['purchases'])===2 && count($detail['usage'])===2,'account history shows captured and pending records');
    check($detail['wallet']['available']===996800,'available balance excludes held tokens');
    foreach(array_merge($detail['purchases'],$detail['usage']) as $record)check($record['email']===$email,'history does not leak another account into selected results');
    $reference='admin:'.\CueMitra\id();
    check($s->credit($uid,50000,$reference,'Support bonus',$other),'administrator can grant extra tokens');
    check(!$s->credit($uid,50000,$reference,'Support bonus',$other),'retry cannot grant the same bonus twice');
    $detail=$reports->account($uid);
    check($detail['wallet']['available']===1046800,'manual bonus immediately changes available balance');
    check((int)$reports->overview($email)['users'][0]['purchased_tokens']===1000000,'manual bonus does not inflate purchased tokens');
    check(count($s->all('SELECT id FROM audit WHERE target_id=? AND action=?',[$uid,'credits']))===1,'manual bonus records an audit entry');
    try {$s->credit($uid,-1046801,'admin:'.\CueMitra\id(),'Invalid removal',$other);throw new RuntimeException('Reserved credits removed');}catch(Problem $e){check($e->status===409,'removal cannot consume reserved credits');}
    check($reports->overview("' OR 1=1 --")['users']===[],'search input is safely parameterized');
    try {$reports->account('../config.local.php');throw new RuntimeException('Invalid ID accepted');}catch(Problem $e){check($e->status===422,'invalid account IDs are rejected');}
    if(isset($argv[1])) {
        $base=$argv[1];
        if($base!=='http://127.0.0.1:8198')throw new RuntimeException('Only the dedicated local test server is allowed.');
        $request=static function(string $route,string $token='',?array $body=null) use($base): array {
            $curl=curl_init($base.'/api/'.$route); $headers=['Content-Type: application/json'];
            if($token!=='')$headers[]='Authorization: Bearer '.$token;
            curl_setopt_array($curl,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>10,CURLOPT_HTTPHEADER=>$headers]);
            if($body!==null)curl_setopt_array($curl,[CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>json_encode($body)]);
            $text=curl_exec($curl); $status=(int)curl_getinfo($curl,CURLINFO_RESPONSE_CODE);curl_close($curl);
            return [$status,json_decode((string)$text,true)];
        };
        $adminToken=bin2hex(random_bytes(32)); $memberToken=bin2hex(random_bytes(32));
        $s->run("UPDATE users SET role='admin' WHERE id=?",[$other]);
        foreach([[$other,$adminToken],[$uid,$memberToken]] as [$id,$token])$s->run('INSERT INTO sessions(token_hash,user_id,client,expires_at,created_at) VALUES (?,?,?,?,?)',[hash('sha256',$token),$id,'desktop',time()+600,time()]);
        check($request('admin/overview')[0]===401,'anonymous requests cannot read admin reports');
        check($request('admin/account?user_id='.$other,$memberToken)[0]===403,'customer sessions cannot read another account report');
        [$status,$data]=$request('admin/overview?q='.rawurlencode($email),$adminToken);
        check($status===200 && $data['users'][0]['email']===$email,'admin overview API returns searched account');
        [$status,$data]=$request('admin/account?user_id='.$uid,$adminToken);
        check($status===200 && count($data['purchases'])===2,'admin account API returns purchase details');
        $body=['user_id'=>$uid,'tokens'=>1000,'reason'=>'HTTP support bonus','request_id'=>\CueMitra\id()];
        check($request('admin/credits',$memberToken,$body)[0]===403,'customer sessions cannot assign tokens');
        $before=$s->wallet($uid)['balance'];
        check($request('admin/credits',$adminToken,$body)[0]===200,'administrator can assign tokens through API');
        check($request('admin/credits',$adminToken,$body)[0]===200 && $s->wallet($uid)['balance']===$before+1000,'API retry does not duplicate a grant');
        $s->run('UPDATE sessions SET created_at=? WHERE token_hash=?',[time()-1000,hash('sha256',$adminToken)]);
        $body['request_id']=\CueMitra\id();
        check($request('admin/credits',$adminToken,$body)[0]===401,'stale administrator verification cannot assign tokens');
    }
} finally {
    foreach([$uid,$other] as $id) {
        $s->run('DELETE FROM audit WHERE target_id=? OR actor_id=?',[$id,$id]);
        foreach(['sessions','ledger','usage_requests','purchases','wallets','users'] as $table)$s->run('DELETE FROM '.$table.' WHERE '.($table==='users'?'id':'user_id').'=?',[$id]);
    }
}
echo "PASS: $passed admin report and credit checks\n";
