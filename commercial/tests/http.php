<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
$c=commercial_config(); if($c['environment']!=='development' || !str_contains($c['dsn'],'cuemitra_test'))exit(9);
$s=new \CueMitra\Store($c); $passed=0;
function check(bool $condition,string $name): void { global $passed; if(!$condition)throw new RuntimeException('FAIL: '.$name); $passed++; echo 'PASS: '.$name."\n"; }
function request(string $route,?array $body=null,string $token='',array $extra=[]): array {
    $curl=curl_init('http://127.0.0.1:8181/api/'.$route); $headers=['Content-Type: application/json'];
    if($token!=='')$headers[]='Authorization: Bearer '.$token;
    foreach($extra as $line)$headers[]=$line;
    curl_setopt_array($curl,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>15,CURLOPT_HTTPHEADER=>$headers]);
    if($body!==null) { curl_setopt($curl,CURLOPT_POST,true); curl_setopt($curl,CURLOPT_POSTFIELDS,json_encode($body)); }
    $text=curl_exec($curl); $status=curl_getinfo($curl,CURLINFO_RESPONSE_CODE); $type=curl_getinfo($curl,CURLINFO_CONTENT_TYPE); curl_close($curl); return [$status,$text,$type];
}
check(request('account')[0]===401,'anonymous HTTP clients cannot read account balances');
check(request('auth/start',['email'=>'x@example.test','terms_accepted'=>true],'',['Origin: https://untrusted.example'])[0]===403,'cross-origin cookie requests are rejected');
check(request('auth/start',['email'=>'x@example.test'],'',['X-Client: desktop'])[0]===422,'account creation requires explicit terms acceptance');
$email='http-'.\CueMitra\id().'@example.test';
[$status,$text]=request('auth/start',['email'=>$email,'terms_accepted'=>true],'',['X-Client: desktop']); check($status===200,'desktop requests an email sign-in code');
$challenge=json_decode($text,true)['challenge']; $mail=json_decode(file_get_contents(dirname(__DIR__).'/storage/outbox/'.$challenge.'.json'),true);
[$status,$text]=request('auth/verify',['challenge'=>$challenge,'code'=>$mail['code']],'',['X-Client: desktop']); check($status===200,'desktop verifies email and receives a session');
$session=json_decode($text,true); $token=$session['token']; $uid=$session['user']['id'];
check(request('admin/overview',null,$token)[0]===403,'customer cannot call the admin HTTP endpoint');
check(request('payments/order',[],'',['Origin: '.$c['base_url']])[0]===401,'anonymous same-origin clients cannot create a payment order');
if(!str_starts_with((string)($c['razorpay_key_id']??''),'rzp_test_')) check(request('payments/order',[],$token)[0]===503,'checkout stays disabled until Razorpay test credentials are configured');
check(request('download',null,$token)[0]===402,'an unpaid account cannot download the Windows installer');
$pid=\CueMitra\id(); $orderId='order_'.$pid; $paymentId='pay_'.$pid;
$s->run('INSERT INTO purchases(id,user_id,order_id,amount,tokens,created_at) VALUES (?,?,?,?,?,?)',[$pid,$uid,$orderId,59900,1000000,time()]);
$event=['event'=>'payment.captured','payload'=>['payment'=>['entity'=>['id'=>$paymentId,'order_id'=>$orderId,'status'=>'captured','amount'=>59900,'currency'=>'INR']]]];
$raw=json_encode($event); $hook=(string)($c['razorpay_webhook_secret']??'');
if($hook!=='') { $signature=\CueMitra\hmac($raw,$hook); check(request('payments/webhook',$event,'',['X-Razorpay-Signature: '.$signature])[0]===200,'signed Razorpay webhook reaches the billing endpoint'); request('payments/webhook',$event,'',['X-Razorpay-Signature: '.$signature]); }
else { check(request('payments/webhook',$event,'',['X-Razorpay-Signature: invalid'])[0]===401,'unsigned payment webhooks cannot grant credits'); $s->credit($uid,1000000,'purchase:'.$pid,'Token pack purchase'); $s->run("UPDATE purchases SET payment_id=?,status='captured' WHERE id=?",[$paymentId,$pid]); }
[$status,$text]=request('account',null,$token); check(json_decode($text,true)['wallet']['balance']===1000000,'purchase balance is available to the desktop account exactly once');
$original=$c; $c['azure_url']='http://127.0.0.1:8182/stream'; $c['azure_key']='synthetic-upstream-key';
function writeConfig(array $c): void { unset($c['razorpay_key_id'],$c['razorpay_key_secret'],$c['razorpay_webhook_secret'],$c['sendgrid_api_key']); file_put_contents(dirname(__DIR__).'/config.local.php',"<?php\nreturn ".var_export($c,true).";\n"); }
try {
    $installerFixture=dirname(__DIR__).'/storage/synthetic-installer-'.\CueMitra\id().'.exe';
    file_put_contents($installerFixture,'Synthetic installer download fixture');
    $c['download_file']=$installerFixture; $c['download_name']='AnsoraAI-Setup.exe'; writeConfig($c);
    [$downloadStatus,$downloadBody,$downloadType]=request('download',null,$token);
    check($downloadStatus===200 && $downloadBody==='Synthetic installer download fixture' && $downloadType==='application/octet-stream','paid downloads deliver the installer through the protected binary endpoint');
    writeConfig($c); $requestId=\CueMitra\id();
    $body=['messages'=>[['role'=>'user','content'=>'Explain threads.']],'max_tokens'=>512,'session_id'=>'synthetic-call','purpose'=>'answer'];
    [$status,$text]=request('chat/completions',$body,$token,['X-Request-Id: '.$requestId]);
    check($status===200 && str_contains($text,'A thread shares memory') && str_contains($text,'"total_tokens":115'),'backend streams provider answer and verified token usage');
    check(request('chat/completions',$body,$token,['X-Request-Id: '.$requestId])[0]===409,'replayed HTTP request cannot generate or bill again');
    check($s->wallet($uid)['balance']===999885 && $s->wallet($uid)['reserved']===0,'streamed provider usage debits the server balance');
    $c['azure_url']='http://127.0.0.1:8182/no-usage'; writeConfig($c);
    [$status,$text]=request('chat/completions',$body,$token,['X-Request-Id: '.\CueMitra\id()]);
    check(str_contains($text,'pending reconciliation') && $s->wallet($uid)['reserved']>0,'missing usage produces a visible error and keeps reserved credits');
    [$status,$text]=request('history',null,$token); $history=json_decode($text,true);
    check(count($history['usage'])===2 && $history['usage'][0]['session_id']==='synthetic-call','customer history groups metered requests by desktop session');
    check(request('auth/logout',[],$token)[0]===200 && request('account',null,$token)[0]===401,'desktop sign-out revokes the server session');
} finally { writeConfig($original); if(isset($installerFixture) && is_file($installerFixture))unlink($installerFixture); }
echo "PASS: $passed HTTP integration checks\n";
