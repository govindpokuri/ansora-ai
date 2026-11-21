<?php
declare(strict_types=1);
namespace CueMitra;
require_once __DIR__.'/PaymentEmails.php';

// An administrator attests to actual receipt in the merchant bank account.
// This is not a bank verification API or a customer payment confirmation.
final class ManualPayments {
    private PaymentEmails $emails;
    public function __construct(private Store $s,array $config=[],?PaymentEmails $emails=null) { $this->emails=$emails??new PaymentEmails($s,$config); }
    public function account(string $email): array {
        $email=strtolower(trim($email));
        if(!filter_var($email,FILTER_VALIDATE_EMAIL) || strlen($email)>254)throw new Problem(422,'Enter the customer account email.');
        $user=$this->s->one('SELECT id,email,status FROM users WHERE email=?',[$email]);
        if(!$user)throw new Problem(404,'No account found. Ask the customer to sign in to Ansora first.');
        return ['user'=>$user,'wallet'=>$this->s->wallet($user['id'])];
    }
    public function recent(): array {
        $rows=$this->s->all("SELECT p.id,u.email,p.amount,p.tokens,p.payment_id,p.created_at FROM purchases p JOIN users u ON u.id=p.user_id WHERE p.order_id LIKE 'upi-manual-%' AND p.status='captured' ORDER BY p.created_at DESC,p.id DESC LIMIT 20");
        $statuses=$this->emails->statuses(array_column($rows,'id'));foreach($rows as &$row)$row['email_status']=$statuses[$row['id']];unset($row);return $rows;
    }
    public static function validate(array $input): array {
        $user=$input['user_id']??null; $email=$input['email']??null; $key=$input['request_id']??null; $rrn=$input['bank_reference']??null;
        if(!is_string($user) || !preg_match('/^[a-f0-9]{32}$/',$user))throw new Problem(422,'Choose a valid customer account.');
        if(!is_string($email) || !filter_var($email,FILTER_VALIDATE_EMAIL) || strlen($email)>254)throw new Problem(422,'Enter a valid account email.');
        if(!is_string($key) || !preg_match('/^[a-zA-Z0-9-]{16,64}$/',$key))throw new Problem(422,'The approval request ID is invalid. Review this payment again.');
        if(!is_string($rrn) || !preg_match('/^[0-9]{12}$/',$rrn))throw new Problem(422,'Enter the 12-digit UPI bank reference (RRN), not the app transaction ID.');
        if(($input['bank_received']??false)!==true)throw new Problem(422,'Verify the incoming payment in your merchant bank account first.');
        if(!is_string($input['plan_id']??null))throw new Problem(422,'Choose a token pack.');
        $plan=Payments::plan(['plan_id'=>$input['plan_id']]);
        if(!is_int($input['amount_received_paise']??null) || $input['amount_received_paise']!==$plan['price_paise'])throw new Problem(422,'The received amount must match the selected pack exactly.');
        $received=$input['received_at']??null;
        if(!is_int($received) || $received<1577836800 || $received>time()+300)throw new Problem(422,'Enter the actual payment date and time; it cannot be in the future.');
        return ['user'=>$user,'email'=>strtolower(trim($email)),'key'=>$key,'reference'=>$rrn,'received_at'=>$received,'plan'=>$plan];
    }
    private function repeatResult(array $p,array $v): array {
        if($p['user_id']!==$v['user'] || (int)$p['amount']!==$v['plan']['price_paise'] || (int)$p['tokens']!==$v['plan']['tokens'] || $p['payment_id']!=='upi_'.$v['reference'] || $p['status']!=='captured') {
            throw new Problem(409,'This bank reference or approval ID already belongs to a different payment. No tokens were added.');
        }
        return ['ok'=>true,'credited'=>false,'purchase_id'=>$p['id'],'tokens'=>$v['plan']['tokens'],'wallet'=>$this->s->wallet($v['user'])];
    }
    public function approve(array $actor,array $input): array {
        if(($actor['role']??'')!=='admin' || ($actor['status']??'')!=='active')throw new Problem(403,'Administrator access required.');
        if(!isset($actor['session_created']) || time()-(int)$actor['session_created']>900)throw new Problem(401,'Sign in again with a fresh email code before approving a payment.');
        $v=self::validate($input); $order='upi-manual-'.$v['key']; $payment='upi_'.$v['reference'];
        $this->s->limit(hash('sha256','manual-upi:'.$actor['id']),60,600);
        try {
            $result=$this->s->tx(function() use($actor,$v,$order,$payment) {
                $user=$this->s->one('SELECT id,email,status FROM users WHERE id=? FOR UPDATE',[$v['user']]);
                if(!$user || strtolower($user['email'])!==$v['email'])throw new Problem(409,'The selected account changed. Find the customer again.');
                if($user['status']!=='active')throw new Problem(409,'This customer account is suspended. Review it in the admin portal first.');
                if(!$this->s->one('SELECT user_id FROM wallets WHERE user_id=? FOR UPDATE',[$v['user']]))throw new Problem(404,'Customer wallet not found.');
                foreach([$this->s->one('SELECT * FROM purchases WHERE order_id=?',[$order]),$this->s->one('SELECT * FROM purchases WHERE payment_id=?',[$payment])] as $existing) {
                    if($existing)return $this->repeatResult($existing,$v);
                }
                $id=id(); $now=time(); $plan=$v['plan'];
                $this->s->run("INSERT INTO purchases(id,user_id,order_id,payment_id,amount,currency,tokens,status,created_at) VALUES (?,?,?,?,?,'INR',?,'captured',?)",[$id,$v['user'],$order,$payment,$plan['price_paise'],$plan['tokens'],$now]);
                $this->s->run('UPDATE wallets SET balance=balance+? WHERE user_id=?',[$plan['tokens'],$v['user']]);
                $this->s->run('INSERT INTO ledger(id,user_id,delta,reference_key,description,created_at) VALUES (?,?,?,?,?,?)',[id(),$v['user'],$plan['tokens'],'purchase:'.$id,'UPI payment manually verified by administrator',$now]);
                $this->s->audit($actor['id'],'upi_payment_approved',$v['user'],['purchase_id'=>$id,'plan_id'=>$plan['id'],'bank_reference'=>$v['reference'],'amount_paise'=>$plan['price_paise'],'tokens'=>$plan['tokens'],'received_at'=>$v['received_at'],'verification'=>'admin_checked_merchant_bank']);
                $this->emails->queue($id,$actor['id'],['email'=>strtolower($user['email']),'amount'=>$plan['price_paise'],'tokens'=>$plan['tokens'],'pack'=>$plan['name'],'approved_at'=>$now]);
                return ['ok'=>true,'credited'=>true,'purchase_id'=>$id,'tokens'=>$plan['tokens'],'wallet'=>$this->s->wallet($v['user'])];
            });
        } catch(\PDOException $e) {
            if((string)$e->getCode()!=='23000')throw $e;
            // Concurrent submissions may race across different customer wallets.
            $existing=$this->s->one('SELECT * FROM purchases WHERE payment_id=? OR order_id=?',[$payment,$order]);
            if(!$existing)throw $e;
            $result=$this->repeatResult($existing,$v);
        }
        // Network delivery is strictly after the token-credit transaction commits.
        try { $result['email']=$this->emails->sendOnce($result['purchase_id']); }
        catch(\Throwable $e) { error_log('Ansora receipt dispatch unavailable: '.get_class($e));$result['email']=['status'=>'pending']; }
        return $result;
    }
}
