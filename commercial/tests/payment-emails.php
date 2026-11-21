<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
require dirname(__DIR__).'/src/ManualPayments.php';
use CueMitra\{Store,ManualPayments,PaymentEmails,Problem};
$dsn='mysql:host=127.0.0.1;port=3318;dbname=cuemitra_upi_test;charset=utf8mb4';
if(getenv('ANSORA_UPI_TEST_DSN')!==$dsn) { fwrite(STDERR,"Requires the explicitly isolated UPI test database.\n");exit(1); }
$c=['dsn'=>$dsn,'db_user'=>'root','db_password'=>'','environment'=>'development','mail_mode'=>'log','brand'=>'Ansora AI','base_url'=>'http://127.0.0.1:8199','support_email'=>'support@example.test'];
$s=new Store($c);$admin=\CueMitra\id();$user=\CueMitra\id();$email='receipt-'.$user.'@example.test';$passed=0;$purchases=[];
function check(bool $ok,string $name): void { global $passed;if(!$ok)throw new RuntimeException('FAIL: '.$name);$passed++;echo 'PASS: '.$name."\n"; }
$actor=['id'=>$admin,'role'=>'admin','status'=>'active','session_created'=>time()];
$body=['user_id'=>$user,'email'=>$email,'plan_id'=>'standard','amount_received_paise'=>99900,'bank_reference'=>(string)random_int(100000000000,899999999980),'request_id'=>\CueMitra\id(),'received_at'=>time(),'bank_received'=>true];
try {
    foreach([[$admin,'receipt-admin-'.$admin.'@example.test','admin'],[$user,$email,'user']] as [$id,$address,$role]) {
        $s->run('INSERT INTO users(id,email,role,created_at) VALUES (?,?,?,?)',[$id,$address,$role,time()]);$s->run('INSERT INTO wallets(user_id) VALUES (?)',[$id]);
    }
    $sent=[];
    $emails=new PaymentEmails($s,$c,function(string $to,array $mail) use(&$sent,$s,$c,$user) {
        check(!$s->db->inTransaction(),'receipt transport runs after the purchase transaction commits');
        $observer=new Store($c);check($observer->wallet($user)['balance']>=1000000,'a separate connection sees credited tokens before sending');
        $sent[]=[$to,$mail];
    });
    $manual=new ManualPayments($s,$c,$emails);
    $result=$manual->approve($actor,$body);$purchases[]=$result['purchase_id'];
    check($result['credited'] && $result['email']['status']==='accepted','new approval credits tokens and records accepted receipt send');
    check(count($sent)===1 && $sent[0][0]===$email,'only the selected customer account receives the receipt');
    $mail=$sent[0][1];
    check(str_contains($mail['plain'],'INR 999.00') && str_contains($mail['plain'],'1,000,000') && str_contains($mail['plain'],'Standard'),'receipt contains stored payment amount, pack and tokens');
    check(str_contains($mail['plain'],$email) && str_contains($mail['plain'],'/dashboard') && str_contains($mail['plain'],'latest Windows installer') && str_contains($mail['plain'],'Start listening'),'receipt contains account-specific next steps and the current dashboard link');
    check(!str_contains($mail['html'],'.exe') && str_contains($mail['html'],'Open my dashboard'),'email does not hardcode a potentially obsolete installer URL');
    check(!$manual->approve($actor,$body)['credited'] && count($sent)===1,'approval replay does not send a duplicate receipt');
    $manual->approve($actor,array_replace($body,['request_id'=>\CueMitra\id()]));
    check(count($sent)===1,'a new approval ID cannot re-email the same UPI payment');
    check($emails->statuses([$result['purchase_id']])[$result['purchase_id']]==='accepted','accepted mail status is persisted');
    $rows=$manual->recent();check($rows[0]['email_status']==='accepted','admin recent approvals expose the mail status');

    $failCalls=0;
    $failing=new PaymentEmails($s,$c,function() use(&$failCalls) { $failCalls++;throw new RuntimeException('Synthetic provider timeout'); });
    $failedManual=new ManualPayments($s,$c,$failing);
    $before=$s->wallet($user)['balance'];
    $second=array_replace($body,['request_id'=>\CueMitra\id(),'bank_reference'=>(string)((int)$body['bank_reference']+1)]);
    $failed=$failedManual->approve($actor,$second);$purchases[]=$failed['purchase_id'];
    check($failed['credited'] && $failed['email']['status']==='unconfirmed','provider failure is reported separately from payment success');
    check($s->wallet($user)['balance']===$before+1000000,'an email failure cannot roll back credited tokens');
    $failedManual->approve($actor,$second);
    check($failCalls===1,'ambiguous provider failures are not automatically retried');

    $historic=\CueMitra\id();$purchases[]=$historic;
    $old=array_replace($body,['request_id'=>\CueMitra\id(),'bank_reference'=>(string)((int)$body['bank_reference']+2)]);
    $s->run("INSERT INTO purchases(id,user_id,order_id,payment_id,amount,tokens,status,created_at) VALUES (?,?,?,?,?,?,'captured',?)",[$historic,$user,'upi-manual-'.$old['request_id'],'upi_'.$old['bank_reference'],99900,1000000,time()]);
    check($manual->approve($actor,$old)['email']['status']==='not_queued' && count($sent)===1,'historical payments are not mailed retroactively on replay');

    $recover=\CueMitra\id();$purchases[]=$recover;
    $third=array_replace($body,['request_id'=>\CueMitra\id(),'bank_reference'=>(string)((int)$body['bank_reference']+3)]);
    $s->tx(function() use($s,$recover,$user,$third,$emails,$admin,$email) {
        $s->run("INSERT INTO purchases(id,user_id,order_id,payment_id,amount,tokens,status,created_at) VALUES (?,?,?,?,?,?,'captured',?)",[$recover,$user,'upi-manual-'.$third['request_id'],'upi_'.$third['bank_reference'],99900,1000000,time()]);
        $s->run('UPDATE wallets SET balance=balance+? WHERE user_id=?',[1000000,$user]);
        $emails->queue($recover,$admin,['email'=>$email,'amount'=>99900,'tokens'=>1000000,'pack'=>'Standard','approved_at'=>time()]);
    });
    check($emails->statuses([$recover])[$recover]==='queued','receipt queue survives commit before delivery');
    $recovered=$manual->approve($actor,$third);
    check(!$recovered['credited'] && $recovered['email']['status']==='accepted' && count($sent)===2,'retry recovers a queued unsent receipt without re-crediting tokens');

    $s->run("CREATE TRIGGER receipt_test_accepted_failure BEFORE INSERT ON audit FOR EACH ROW BEGIN IF NEW.action='payment_email_accepted' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Synthetic acceptance record failure'; END IF; END");
    $fourth=array_replace($body,['request_id'=>\CueMitra\id(),'bank_reference'=>(string)((int)$body['bank_reference']+4)]);
    $uncertain=$manual->approve($actor,$fourth);$purchases[]=$uncertain['purchase_id'];$count=count($sent);
    $s->run('DROP TRIGGER receipt_test_accepted_failure');
    $manual->approve($actor,$fourth);
    check($uncertain['email']['status']==='unconfirmed' && count($sent)===$count,'accepted-but-unrecorded sends are not sent again automatically');

    $escaped=PaymentEmails::content($c,['email'=>'<script>bad</script>@example.test','amount'=>59900,'tokens'=>500000,'pack'=>'<b>Starter</b>','purchase_id'=>\CueMitra\id(),'approved_at'=>time()]);
    check(!str_contains($escaped['html'],'<script>') && str_contains($escaped['html'],'&lt;b&gt;Starter&lt;/b&gt;'),'receipt HTML escapes stored account and pack text');
    check(str_contains($escaped['plain'],'not a tax invoice'),'payment confirmation is not presented as a tax invoice');
    check(!preg_match('/api[_-]?key|Bearer\s|password|sign-in code is [0-9]{6}/i',$mail['plain']),'receipt includes no provider credentials or sign-in code');
    file_put_contents(dirname(__DIR__,2).'/artifacts/payment-confirmation-email-preview.html',$mail['html']);
} finally {
    $s->run('DROP TRIGGER IF EXISTS receipt_test_accepted_failure');
    foreach([$user,$admin] as $id) {
        $s->run('DELETE FROM audit WHERE actor_id=? OR target_id=?',[$id,$id]);
        foreach(['sessions','ledger','usage_requests','purchases','wallets','users'] as $table)$s->run('DELETE FROM '.$table.' WHERE '.($table==='users'?'id':'user_id').'=?',[$id]);
    }
}
echo "PASS: $passed payment email checks. No external email was sent.\n";
