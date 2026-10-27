<?php
declare(strict_types=1);
namespace CueMitra;

final class Payments {
    private Store $s; private array $c;
    public function __construct(Store $s,array $c) { $this->s=$s; $this->c=$c; }
    private function remote(string $path,?array $body=null): array {
        $curl=curl_init('https://api.razorpay.com/v1/'.$path);
        $options=[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>20,CURLOPT_CONNECTTIMEOUT=>10,CURLOPT_FOLLOWLOCATION=>false,
            CURLOPT_USERPWD=>$this->c['razorpay_key_id'].':'.$this->c['razorpay_key_secret'],
            CURLOPT_HTTPHEADER=>['Content-Type: application/json','Accept: application/json']];
        if($body!==null) { $options[CURLOPT_POST]=true; $options[CURLOPT_POSTFIELDS]=json_encode($body,JSON_THROW_ON_ERROR); }
        curl_setopt_array($curl,$options); $text=curl_exec($curl); $status=(int)curl_getinfo($curl,CURLINFO_RESPONSE_CODE); curl_close($curl);
        if($status===401)throw new Problem(401,'Payment provider rejected the server credentials.');
        if($text===false || $status<200 || $status>=300)throw new Problem(500,'Payment provider is unavailable. Please try again later.');
        try { $decoded=json_decode($text,true,512,JSON_THROW_ON_ERROR); }
        catch(\JsonException $e) { throw new Problem(500,'Payment provider returned an invalid response. Please try again later.'); }
        if(!is_array($decoded))throw new Problem(500,'Payment provider is unavailable. Please try again later.');
        return $decoded;
    }
    public function ready(): bool {
        $id=(string)($this->c['razorpay_key_id']??''); $secret=(string)($this->c['razorpay_key_secret']??'');
        if(!preg_match('/^rzp_(test|live)_[A-Za-z0-9]+$/',$id) || $secret==='' || (int)$this->c['pack_price_paise']<100)return false;
        $live=str_starts_with($id,'rzp_live_');
        if(($this->c['environment']??'')==='production' && !$live)return false;
        if(($this->c['environment']??'')==='production' || $live) {
            return !empty($this->c['sales_enabled']) && !empty($this->c['razorpay_webhook_secret']) && $this->c['azure_key']!=='' && $this->c['azure_url']!==''
                && $this->c['business_name']!=='' && $this->c['business_address']!=='' && is_file((string)$this->c['download_file']);
        }
        return str_starts_with($id,'rzp_test_');
    }
    public function order(string $user,array $input=[]): array {
        if(!$this->ready())throw new Problem(503,'Purchases are not open yet. Please check back soon.');
        $amount=(int)$this->c['pack_price_paise'];
        if($amount<100)throw new Problem(500,'Pack price is below the payment minimum.');
        if(array_key_exists('amount',$input) && $input['amount']!==$amount)throw new Problem(400,'The token pack price is fixed.');
        $this->s->limit(hash('sha256','orders:'.$user),10,600); $pid=id();
        $this->s->run('INSERT INTO purchases(id,user_id,amount,tokens,created_at) VALUES (?,?,?,?,?)',[$pid,$user,$amount,$this->c['pack_tokens'],time()]);
        try { $created=$this->remote('orders',['amount'=>$amount,'currency'=>'INR','receipt'=>$pid,'notes'=>['purchase_id'=>$pid]]); }
        catch(\Throwable $e) { $this->s->run("UPDATE purchases SET status='failed' WHERE id=?",[$pid]); throw $e; }
        $orderId=(string)($created['id']??'');
        if((int)($created['amount']??0)!==$amount || strtoupper((string)($created['currency']??''))!=='INR' || !preg_match('/^order_[A-Za-z0-9]+$/',$orderId)) {
            $this->s->run("UPDATE purchases SET status='failed' WHERE id=?",[$pid]);
            throw new Problem(500,'Payment order could not be validated.');
        }
        $this->s->run('UPDATE purchases SET order_id=? WHERE id=?',[$orderId,$pid]);
        return ['order_id'=>$orderId,'amount'=>$amount,'currency'=>'INR','key_id'=>$this->c['razorpay_key_id']];
    }
    public function checkoutSignatureValid(string $orderId,string $paymentId,string $signature): bool {
        $secret=(string)($this->c['razorpay_key_secret']??'');
        if($secret==='' || !preg_match('/^order_[A-Za-z0-9]+$/',$orderId) || !preg_match('/^pay_[A-Za-z0-9]+$/',$paymentId) || !preg_match('/^[a-f0-9]{64}$/',$signature))return false;
        return hash_equals(hmac($orderId.'|'.$paymentId,$secret),$signature);
    }
    public function capture(array $payment): bool {
        $orderId=(string)($payment['order_id']??''); $paymentId=(string)($payment['id']??'');
        if(!preg_match('/^order_[A-Za-z0-9]+$/',$orderId) || !preg_match('/^pay_[A-Za-z0-9]+$/',$paymentId))throw new Problem(400,'Payment confirmation is incomplete.');
        if(($payment['status']??'')!=='captured')return false;
        $p=$this->s->one('SELECT * FROM purchases WHERE order_id=?',[$orderId]); if(!$p)return false;
        if((int)($payment['amount']??-1)!==(int)$p['amount'] || strtoupper((string)($payment['currency']??''))!==(string)$p['currency'])throw new Problem(422,'Payment details do not match the purchase.');
        return $this->grant($p,$paymentId);
    }
    private function grant(array $p,string $payment): bool {
        return $this->s->tx(function() use($p,$payment) {
            $this->s->one('SELECT * FROM wallets WHERE user_id=? FOR UPDATE',[$p['user_id']]);
            $locked=$this->s->one('SELECT * FROM purchases WHERE id=? FOR UPDATE',[$p['id']]); if(!$locked)return false;
            if($locked['status']==='captured') { if($locked['payment_id']!==$payment)throw new Problem(409,'Purchase already has a payment.'); return false; }
            if($locked['status']==='refund_review')return false;
            $this->s->run("UPDATE purchases SET payment_id=?,status='captured' WHERE id=?",[$payment,$p['id']]);
            $this->s->run('UPDATE wallets SET balance=balance+? WHERE user_id=?',[(int)$p['tokens'],$p['user_id']]);
            $this->s->run('INSERT INTO ledger(id,user_id,delta,reference_key,description,created_at) VALUES (?,?,?,?,?,?)',[id(),$p['user_id'],(int)$p['tokens'],'purchase:'.$p['id'],'Token pack purchase',time()]);
            return true;
        });
    }
    public function verify(string $user,array $input): array {
        $orderId=(string)($input['razorpay_order_id']??''); $paymentId=(string)($input['razorpay_payment_id']??''); $signature=(string)($input['razorpay_signature']??'');
        if($orderId==='' || $paymentId==='' || $signature==='')throw new Problem(400,'Payment confirmation is incomplete.');
        if(!$this->checkoutSignatureValid($orderId,$paymentId,$signature))throw new Problem(400,'Payment could not be verified.');
        $p=$this->s->one('SELECT * FROM purchases WHERE user_id=? AND order_id=?',[$user,$orderId]); if(!$p)throw new Problem(404,'Purchase not found.');
        $payment=$this->remote('payments/'.rawurlencode($paymentId));
        $this->confirm($user,$orderId,$paymentId,$payment);
        return ['status'=>$this->s->one('SELECT status FROM purchases WHERE id=?',[$p['id']])['status'],'wallet'=>$this->s->wallet($user)];
    }
    public function confirm(string $user,string $orderId,string $paymentId,array $payment): void {
        // Use the server-stored order and the fetched payment, never only the browser result.
        $purchase=$this->s->one('SELECT * FROM purchases WHERE user_id=? AND order_id=?',[$user,$orderId]);
        if(!$purchase)throw new Problem(404,'Purchase not found.');
        if(($payment['order_id']??'')!==$purchase['order_id'] || ($payment['id']??'')!==$paymentId)
            throw new Problem(400,'Payment does not match this order.');
        if(($payment['status']??'')!=='captured')throw new Problem(400,'Payment is not captured.');
        $this->capture($payment);
        $settled=$this->s->one('SELECT status,payment_id FROM purchases WHERE id=?',[$purchase['id']]);
        if(!$settled || $settled['status']!=='captured' || $settled['payment_id']!==$paymentId)throw new Problem(400,'Payment is not captured.');
    }
    public function webhook(string $raw,string $signature): void {
        $secret=(string)($this->c['razorpay_webhook_secret']??''); $expected=$secret===''?'':hmac($raw,$secret);
        if($secret==='' || $signature==='' || strlen($signature)!==strlen($expected) || !hash_equals($expected,$signature))throw new Problem(401,'Webhook signature is invalid.');
        $body=json_decode($raw,true,512,JSON_THROW_ON_ERROR); if(!is_array($body))throw new Problem(422,'JSON object required.');
        $event=(string)($body['event']??'');
        if($event==='payment.captured')$this->capture(is_array($body['payload']['payment']['entity']??null)?$body['payload']['payment']['entity']:[]);
        if($event==='refund.created' || $event==='payment.refunded') {
            $payment=$event==='refund.created'?($body['payload']['refund']['entity']['payment_id']??''):($body['payload']['payment']['entity']['id']??'');
            $this->pauseForRefund(is_string($payment)?$payment:'',$event);
        }
    }
    private function pauseForRefund(string $payment,string $event): void {
        if(!preg_match('/^pay_[A-Za-z0-9]+$/',$payment))return;
        $p=$this->s->one('SELECT * FROM purchases WHERE payment_id=?',[$payment]); if(!$p)return;
        $this->s->tx(function() use($p,$event) {
            $this->s->one('SELECT * FROM wallets WHERE user_id=? FOR UPDATE',[$p['user_id']]);
            $r=$this->s->one('SELECT * FROM purchases WHERE id=? FOR UPDATE',[$p['id']]); if($r['status']==='refund_review')return;
            $this->s->run("UPDATE purchases SET status='refund_review' WHERE id=?",[$p['id']]);
            $this->s->run("UPDATE users SET status='suspended' WHERE id=?",[$p['user_id']]);
            $this->s->audit($p['user_id'],'refund_review',$p['id'],['event'=>$event,'note'=>'Reconcile payment and credits before reactivating account.']);
        });
    }
}
