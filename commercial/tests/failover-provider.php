<?php
if(!in_array($_SERVER['REMOTE_ADDR']??'',['127.0.0.1','::1'],true))exit;
$path=parse_url($_SERVER['REQUEST_URI'],PHP_URL_PATH);
$backup=str_starts_with($path,'/backup');
if(($_SERVER['HTTP_API_KEY']??'')!==($backup?'synthetic-backup-key':'synthetic-upstream-key')) { http_response_code(401); exit; }
$input=json_decode(file_get_contents('php://input'),true);
if(in_array($path,['/unavailable','/backup-fail','/bad-request','/rate-limit'],true)) {
    http_response_code($path==='/bad-request'?400:($path==='/rate-limit'?429:503)); echo '{}'; exit;
}
if($path==='/slow')sleep(5);
header('Content-Type: text/event-stream'); header('x-ms-request-id: synthetic-provider');
function event(array $e): void { echo 'data: '.json_encode($e)."\n\n"; if(ob_get_level()>0)ob_flush(); flush(); }
event(['model'=>$input['model']??'unknown','choices'=>[['delta'=>['role'=>'assistant']]]]);
if($path==='/blocked') {
    event(['choices'=>[['delta'=>[],'finish_reason'=>'content_filter']],'usage'=>['prompt_tokens'=>40,'completion_tokens'=>0,'total_tokens'=>40]]);
} elseif($path==='/empty') {
    event(['choices'=>[['delta'=>[],'finish_reason'=>'stop']],'usage'=>['prompt_tokens'=>40,'completion_tokens'=>4,'total_tokens'=>44]]);
} else {
    event(['choices'=>[['delta'=>['content'=>($backup?'BACKUP':'PRIMARY').' max_tokens='.($input['max_tokens']??0)]]]]);
    if($path==='/partial-error')event(['error'=>['code'=>'unavailable']]);
    event(['choices'=>[['delta'=>[],'finish_reason'=>'stop']],'usage'=>['prompt_tokens'=>100,'completion_tokens'=>15,'total_tokens'=>115]]);
}
echo "data: [DONE]\n\n";
