<?php
declare(strict_types=1);
// Creates one unpaid TEST order with the configured provider. Never takes payment.
require dirname(__DIR__).'/bootstrap.php';
$c=commercial_config();
if($c['environment']!=='development' || !str_contains($c['dsn'],'cuemitra_test') || !str_starts_with($c['razorpay_key_id'],'rzp_test_'))exit("This check requires the isolated database and Razorpay test keys.\n");
$s=new \CueMitra\Store($c); $auth=new \CueMitra\Auth($s,$c);
$ticket=$auth->start('razorpay-check-'.\CueMitra\id().'@example.test',\CueMitra\id());
$mail=json_decode(file_get_contents(dirname(__DIR__).'/storage/outbox/'.$ticket['challenge'].'.json'),true);
$session=$auth->verify($ticket['challenge'],$mail['code'],'desktop',\CueMitra\id());
$curl=curl_init('http://127.0.0.1:8181/api/create-order');
curl_setopt_array($curl,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>30,CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>'{}',
    CURLOPT_HTTPHEADER=>['Content-Type: application/json','Authorization: Bearer '.$session['token']]]);
$raw=curl_exec($curl); $status=(int)curl_getinfo($curl,CURLINFO_RESPONSE_CODE); curl_close($curl);
if($status!==200) { echo "Test order creation failed: HTTP $status. Credentials and response were not printed.\n"; exit(1); }
$result=json_decode($raw,true);
if(!is_array($result) || ($result['amount']??0)!==59900 || ($result['currency']??'')!=='INR' || !preg_match('/^order_[A-Za-z0-9]+$/',(string)($result['order_id']??'')) || isset($result['key_secret']))exit("Test order response did not pass validation.\n");
echo "PASS: authenticated create-order endpoint created a real Razorpay TEST order for INR 599 / 1M tokens. No payment was made.\n";
$verify=curl_init('http://127.0.0.1:8181/api/verify-payment');
curl_setopt_array($verify,[CURLOPT_RETURNTRANSFER=>true,CURLOPT_TIMEOUT=>10,CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>'{}',
    CURLOPT_HTTPHEADER=>['Content-Type: application/json','Authorization: Bearer '.$session['token']]]);
curl_exec($verify); $status=(int)curl_getinfo($verify,CURLINFO_RESPONSE_CODE); curl_close($verify);
if($status!==400)exit("Verify-payment alias did not reject missing payment fields.\n");
if($s->wallet($session['user']['id'])['balance']!==0)exit("An unpaid order must not grant tokens.\n");
echo "PASS: verify-payment rejects incomplete confirmation; unpaid orders grant no tokens.\n";
