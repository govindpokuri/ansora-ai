<?php
declare(strict_types=1);
use CueMitra\{Store,Auth,Payments,Ai,Problem};
header('X-Content-Type-Options: nosniff'); header('Cache-Control: no-store'); header('Referrer-Policy: same-origin');
header("Content-Security-Policy: default-src 'none'; frame-ancestors 'none'");
ini_set('display_errors','0');
$root=getenv('CUEMITRA_APP_ROOT') ?: dirname(__DIR__).'/cuemitra-private';
try {
    if(!is_file($root.'/bootstrap.php')) { http_response_code(503); header('Content-Type: application/json'); echo '{"error":"Server installation is incomplete."}'; exit; }
    require_once $root.'/bootstrap.php'; $c=commercial_config();
    require_once $root.'/src/Admin.php';
    $route=trim((string)($_GET['route']??''),'/'); $method=$_SERVER['REQUEST_METHOD']??'GET';
    $token=''; $authorization=$_SERVER['HTTP_AUTHORIZATION']??$_SERVER['REDIRECT_HTTP_AUTHORIZATION']??'';
    if(str_starts_with($authorization,'Bearer '))$token=substr($authorization,7); else $token=$_COOKIE['cuemitra_session']??'';
    $bearer=str_starts_with($authorization,'Bearer ');
    if($method==='POST' && $route!=='payments/webhook' && !$bearer) {
        $origin=$_SERVER['HTTP_ORIGIN']??'';
        // Desktop auth is unauthenticated but rate-limited; browser cookies never bypass origin validation.
        $desktopAuth=in_array($route,['auth/start','auth/verify'],true) && ($_SERVER['HTTP_X_CLIENT']??'')==='desktop' && !isset($_COOKIE['cuemitra_session']);
        if(!$desktopAuth && !hash_equals(rtrim($c['base_url'],'/'),rtrim($origin,'/')))throw new Problem(403,'Request origin is invalid.');
    }
    if((int)($_SERVER['CONTENT_LENGTH']??0)>512000)throw new Problem(413,'Request is too large.');
    $raw=file_get_contents('php://input',false,null,0,512001); if(strlen($raw)>512000)throw new Problem(413,'Request is too large.');
    $input=$raw===''?[]:json_decode($raw,true,32,JSON_THROW_ON_ERROR); if(!is_array($input))throw new Problem(422,'JSON object required.');
    $s=new Store($c); $auth=new Auth($s,$c); $payments=new Payments($s,$c);
    $respond=static function(array $data,int $status=200): void { \CueMitra\json($data,$status); };
    if($route==='config' && $method==='GET') { $respond(['brand'=>$c['brand'],'support_email'=>$c['support_email'],'price_paise'=>$c['pack_price_paise'],'pack_tokens'=>$c['pack_tokens'],'sales_enabled'=>$payments->ready(),'business_name'=>$c['business_name'],'business_address'=>$c['business_address']]); exit; }
    if($route==='health' && $method==='GET') { $s->one('SELECT 1 AS ok'); $respond(['ok'=>true]); exit; }
    $ip=$_SERVER['REMOTE_ADDR']??'unknown';
    if($route==='auth/start' && $method==='POST') { if(($input['terms_accepted']??false)!==true)throw new Problem(422,'Read and accept the terms and privacy notice before signing in.'); $respond($auth->start((string)($input['email']??''),$ip)); exit; }
    if($route==='auth/verify' && $method==='POST') {
        $client=($_SERVER['HTTP_X_CLIENT']??'')==='desktop'?'desktop':'browser';
        $result=$auth->verify((string)($input['challenge']??''),(string)($input['code']??''),$client,$ip);
        if($client==='browser') { setcookie('cuemitra_session',$result['token'],['expires'=>$result['expires_at'],'path'=>'/','secure'=>$c['environment']==='production','httponly'=>true,'samesite'=>'Strict']); unset($result['token']); }
        $respond($result); exit;
    }
    if($route==='payments/webhook' && $method==='POST') { $payments->webhook($raw,$_SERVER['HTTP_X_RAZORPAY_SIGNATURE']??''); $respond(['ok'=>true]); exit; }
    $u=$auth->user($token,str_starts_with($route,'admin/'));
    if($route==='auth/logout' && $method==='POST') { $s->run('DELETE FROM sessions WHERE token_hash=?',[hash('sha256',$token)]); setcookie('cuemitra_session','',['expires'=>1,'path'=>'/','secure'=>$c['environment']==='production','httponly'=>true,'samesite'=>'Strict']); $respond(['ok'=>true]); exit; }
    if($route==='account' && $method==='GET') { $respond(['user'=>['id'=>$u['id'],'email'=>$u['email'],'role'=>$u['role']],'wallet'=>$s->wallet($u['id']),'ai_configured'=>$c['azure_key']!=='' && $c['azure_url']!=='']); exit; }
    if($route==='history' && $method==='GET') {
        $respond(['usage'=>$s->all('SELECT id,session_id,purpose,input_tokens,output_tokens,total_tokens,status,reserved,created_at FROM usage_requests WHERE user_id=? ORDER BY created_at DESC LIMIT 100',[$u['id']]),'purchases'=>$s->all('SELECT id,order_id,payment_id,amount,currency,tokens,status,created_at FROM purchases WHERE user_id=? ORDER BY created_at DESC LIMIT 100',[$u['id']]),'ledger'=>$s->all('SELECT delta,description,created_at FROM ledger WHERE user_id=? ORDER BY created_at DESC LIMIT 100',[$u['id']])]); exit;
    }
    if(($route==='payments/order' || $route==='create-order') && $method==='POST') {
        if(array_key_exists('amount',$input) && (!is_int($input['amount']) || $input['amount']<100))throw new Problem(400,'Amount must be at least 100 paise.');
        $respond($payments->order($u['id'],$input)); exit;
    }
    if(($route==='payments/verify' || $route==='verify-payment') && $method==='POST') { $respond($payments->verify($u['id'],$input)); exit; }
    if($route==='chat/completions' && $method==='POST') { (new Ai($s,$c))->stream($u['id'],$input,$_SERVER['HTTP_X_REQUEST_ID']??''); exit; }
    if($route==='download' && $method==='GET') {
        if(!$s->one("SELECT id FROM purchases WHERE user_id=? AND status='captured' LIMIT 1",[$u['id']]) && $s->wallet($u['id'])['balance']<=0 && $u['role']!=='admin')throw new Problem(402,'Purchase a token pack to download the Windows app.');
        if(!is_file($c['download_file']))throw new Problem(503,'The commercial download is not available yet.');
        header('Content-Type: '.(strtolower(pathinfo($c['download_file'],PATHINFO_EXTENSION))==='exe'?'application/octet-stream':'application/zip')); header('Content-Disposition: attachment; filename="'.preg_replace('/[^a-zA-Z0-9_.-]/','',$c['download_name']).'"'); header('Content-Length: '.filesize($c['download_file'])); readfile($c['download_file']); exit;
    }
    if($route==='admin/overview' && $method==='GET') {
        $respond((new \CueMitra\AdminReports($s))->overview((string)($_GET['q']??''))); exit;
    }
    if($route==='admin/account' && $method==='GET') {
        $respond((new \CueMitra\AdminReports($s))->account((string)($_GET['user_id']??''))); exit;
    }
    if(str_starts_with($route,'admin/') && $method==='POST') {
        if(time()-(int)$u['session_created']>900)throw new Problem(401,'For this administrator action, sign in again with a fresh email code.');
        $reason=trim((string)($input['reason']??'')); if(strlen($reason)<5 || strlen($reason)>250)throw new Problem(422,'Provide a reason (5–250 characters).');
        $target=(string)($input['user_id']??'');
        if($route==='admin/status') {
            if($target===$u['id'])throw new Problem(409,'You cannot suspend your own administrator account.');
            $status=$input['status']??''; if(!in_array($status,['active','suspended'],true))throw new Problem(422,'Invalid account status.');
            if(!$s->one('SELECT id FROM users WHERE id=?',[$target]))throw new Problem(404,'Account not found.');
            $s->tx(function() use($s,$status,$target,$u,$reason) { $s->run('UPDATE users SET status=? WHERE id=?',[$status,$target]); $s->audit($u['id'],'account_status',$target,['status'=>$status,'reason'=>$reason]); }); $respond(['ok'=>true]); exit;
        }
        if($route==='admin/credits') {
            $delta=$input['tokens']??0; $key=$input['request_id']??'';
            if(!is_int($delta) || $delta===0 || abs($delta)>5000000 || !preg_match('/^[a-zA-Z0-9-]{16,64}$/',$key))throw new Problem(422,'Invalid credit adjustment.');
            // Ledger reference protects duplicate submissions. Audit remains tied to the adjustment key.
            $s->credit($target,$delta,'admin:'.$key,$reason,$u['id']); $respond(['ok'=>true]); exit;
        }
        if($route==='admin/reconcile') {
            $rid=(string)($input['request_id']??''); $r=$s->one('SELECT * FROM usage_requests WHERE id=?',[$rid]);
            if(!$r || !in_array($r['status'],['reserved','reconcile'],true) || (int)$r['created_at']>time()-120)throw new Problem(409,'Request is missing, completed or still in progress.');
            $usage=Ai::usage($input['usage']??null); if(!$usage || $usage['total_tokens']>(int)$r['reserved'])throw new Problem(422,'Enter verified input/output/total usage within the reservation.');
            $s->settle($rid,$usage,false,$r['provider_id'],$u['id'],$reason); $respond(['ok'=>true]); exit;
        }
    }
    throw new Problem(404,'Endpoint not found.');
} catch(Problem $e) { \CueMitra\json(['error'=>$e->getMessage()],$e->status); }
catch(\JsonException $e) { \CueMitra\json(['error'=>'Invalid JSON request.'],422); }
catch(\Throwable $e) { error_log('CueMitra request failed: '.get_class($e)); http_response_code(503); header('Content-Type: application/json'); echo '{"error":"Service temporarily unavailable. Please try again later."}'; }
