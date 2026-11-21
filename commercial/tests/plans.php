<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
use CueMitra\{Payments,Problem};
function expect_plan(bool $condition,string $message): void {
    if(!$condition)throw new RuntimeException($message);
    echo 'PASS: '.$message."\n";
}
foreach([['starter',500000,59900,5],['standard',1000000,99900,10],['plus',3000000,249900,30]] as [$id,$tokens,$price,$interviews]) {
    $plan=Payments::plan(['plan_id'=>$id,'amount'=>$price]);
    expect_plan($plan['tokens']===$tokens && $plan['price_paise']===$price && $plan['estimated_interviews']===$interviews,$id.' uses the published price, credit and interview estimate');
    try { Payments::plan(['plan_id'=>$id,'amount'=>100]); throw new RuntimeException('Accepted a tampered price'); }
    catch(Problem $e) { expect_plan($e->status===400,$id.' rejects client price tampering'); }
}
expect_plan(Payments::plan()['id']==='standard','older clients without a plan select Standard');
foreach(['unknown','',[],123] as $id) {
    try { Payments::plan(['plan_id'=>$id]); throw new RuntimeException('Accepted an invalid plan'); }
    catch(Problem $e) { expect_plan($e->status===400,'invalid plan rejected'); }
}
