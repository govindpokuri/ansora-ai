<?php
// Loopback test fixture. Never include this in the deployment package.
if(!in_array($_SERVER['REMOTE_ADDR']??'',['127.0.0.1','::1'],true)) { http_response_code(403); exit; }
if(($_SERVER['HTTP_API_KEY']??'')!=='synthetic-upstream-key') { http_response_code(401); echo '{"error":"wrong synthetic key"}'; exit; }
if($_SERVER['REQUEST_URI']==='/reject') { http_response_code(401); echo '{"error":"synthetic rejection"}'; exit; }
header('Content-Type: text/event-stream'); header('x-ms-request-id: synthetic-provider-request');
if($_SERVER['REQUEST_URI']==='/inspect-limit') {
    $request=json_decode(file_get_contents('php://input'),true);
    echo "data: ".json_encode(['choices'=>[['delta'=>['content'=>'Received max_tokens='.($request['max_tokens']??'missing')]]]])."\n\n";
}
echo "data: ".json_encode(['choices'=>[['delta'=>['content'=>'A thread shares memory with other threads in its process.']]]])."\n\n";
echo "data: ".json_encode(['choices'=>[['delta'=>[],'finish_reason'=>'stop']]])."\n\n";
if($_SERVER['REQUEST_URI']!=='/no-usage')echo "data: ".json_encode(['choices'=>[],'usage'=>['prompt_tokens'=>100,'completion_tokens'=>15,'total_tokens'=>115]])."\n\n";
echo "data: [DONE]\n\n";
