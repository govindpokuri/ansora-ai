<?php
declare(strict_types=1);
namespace CueMitra;

final class Ai {
    private Store $s; private array $c;
    public function __construct(Store $s,array $c) { $this->s=$s; $this->c=$c; }
    public static function outputLimit(array $config,array $input,array $messages): int {
        $requested=max(64,(int)($input['max_tokens']??4096));
        $ceiling=max(64,(int)$config['max_output_tokens']);
        // Deployment aliases are arbitrary: explicitly configured family takes precedence.
        $family=strtolower(trim((string)($config['azure_model_family']??''))) ?: strtolower((string)$config['azure_model']);
        if(!preg_match('/^gpt-4\.1-mini(?:-\d{4}-\d{2}-\d{2})?$/',$family))return min($ceiling,$requested);
        if(($input['purpose']??'answer')==='resume')return min($ceiling,$requested,1500);
        $question=''; foreach($messages as $message)if($message['role']==='user')$question=$message['content'];
        if(is_string($input['original_question']??null))$question=$input['original_question'];
        $codingMode=false;
        // Current desktop uses a structured current question; ignore its recent context.
        $jsonStart=strpos($question,'{');
        $structured=$jsonStart===false?null:json_decode(substr($question,$jsonStart),true);
        if(is_array($structured) && is_string($structured['currentQuestion']??null))$question=$structured['currentQuestion'];
        elseif(($marker=strpos($question,"Pasted question / problem:\n"))!==false) {
            $codingMode=str_starts_with($question,'Solve this coding problem or fix the supplied code.');
            $question=substr($question,$marker+strlen("Pasted question / problem:\n"));
        } elseif(preg_match_all('/^(?:Caller|Call):\s*(.+)$/m',$question,$turns))$question=end($turns[1]);
        $noCode=(bool)preg_match("/\\b(?:no|without)\\s+(?:any\\s+)?code\\b|\\b(?:do not|don't|dont)\\s+(?:write|include|provide|show|give)\\b.{0,30}\\bcode\\b/i",$question);
        $asksCode=$codingMode || (bool)preg_match('/\bimplement\b|^\s*(?:write|code)\b|\b(?:write|generate|provide|show|give|return|create|fix|debug|correct|refactor|complete|solve)\b.{0,100}\b(?:code|program|function|method|script|query|solution|implementation|algorithm)\b/is',$question);
        return min($ceiling,$requested,(!$noCode && $asksCode)?1600:320);
    }
    public static function usage($value): ?array {
        if(!is_array($value))return null;
        foreach(['prompt_tokens','completion_tokens','total_tokens'] as $k)if(!isset($value[$k]) || !is_int($value[$k]) || $value[$k]<0)return null;
        if($value['total_tokens']!==$value['prompt_tokens']+$value['completion_tokens'])return null;
        return $value;
    }
    public function stream(string $user,array $input,string $key): void {
        if($this->c['azure_url']==='' || $this->c['azure_key']==='')throw new Problem(503,'AI service is not configured.');
        if(!preg_match('/^[a-zA-Z0-9-]{16,64}$/',$key))throw new Problem(422,'A valid request ID is required.');
        $messages=$input['messages']??[]; if(!is_array($messages) || count($messages)<1 || count($messages)>8)throw new Problem(422,'Invalid conversation.');
        $bytes=0; $safe=[];
        foreach($messages as $m) {
            if(!is_array($m) || !in_array($m['role']??'',['system','user','assistant'],true) || !is_string($m['content']??null))throw new Problem(422,'Invalid message.');
            $bytes+=strlen($m['content']); $safe[]=['role'=>$m['role'],'content'=>$m['content']];
        }
        if($bytes<1 || $bytes>160000)throw new Problem(422,'Conversation is too large.');
        $session=(string)($input['session_id']??''); if(!preg_match('/^[a-zA-Z0-9-]{0,64}$/',$session))throw new Problem(422,'Invalid session ID.');
        $purpose=($input['purpose']??'answer')==='resume'?'resume':'answer';
        $this->s->limit(hash('sha256','ai:'.$user),30,60);
        $started=false;
        $emit=function(string $data) use(&$started): void {
            if(!$started) { header('Content-Type: text/event-stream'); header('Cache-Control: no-cache'); header('X-Accel-Buffering: no'); $started=true; }
            echo 'data: '.$data."\n\n"; if(ob_get_level()>0)@ob_flush(); flush();
        };
        $fallback=!empty($this->c['azure_fallback_enabled']) && !empty($this->c['azure_fallback_url']) && !empty($this->c['azure_fallback_key']) && !empty($this->c['azure_fallback_model']);
        $backup=array_replace($this->c,[
            'azure_url'=>$this->c['azure_fallback_url']??'', 'azure_key'=>$this->c['azure_fallback_key']??'',
            'azure_model'=>$this->c['azure_fallback_model']??'gpt-oss-120b', 'azure_model_family'=>'gpt-oss-120b'
        ]);
        $cooling=$fallback && ($this->coolingDown() || ($input['continuation_model']??'')===$backup['azure_model']);
        $routes=$cooling?[$backup]:($fallback?[$this->c,$backup]:[$this->c]);
        $budget=max(1,(int)$this->c['upstream_timeout']); $deadline=microtime(true)+$budget;
        ignore_user_abort(true); @set_time_limit($budget+10);
        $sum=null; $pending=false; $answer=false; $last=null;
        foreach($routes as $index=>$route) {
            $isBackup=$cooling || $index>0;
            $remaining=(int)floor($deadline-microtime(true)); if($remaining<1)break;
            // The reasoning backup gets its own budget, never the mini model's 320-token cap.
            $max=$isBackup?max(64,(int)($this->c['azure_fallback_max_tokens']??4096)):self::outputLimit($route,$input,$safe);
            $encoded=json_encode(['model'=>$route['azure_model'],'messages'=>$safe,'max_tokens'=>$max,'temperature'=>0.1,'stream'=>true,'stream_options'=>['include_usage'=>true]],JSON_THROW_ON_ERROR);
            $requestKey=$index===0?$key:hash('sha256',$key.':backup');
            // Each provider attempt has a separate durable reservation and settlement.
            $rid=$this->s->reserve($user,$requestKey,hash('sha256',$encoded),$session,$purpose,$bytes+2048+$max);
            $firstText=$isBackup?($this->c['azure_fallback_first_text_timeout']??20):($fallback?($this->c['azure_first_text_timeout']??8):$remaining);
            $last=$this->attempt($route,$encoded,$rid,$remaining,max(1,(float)$firstText),$emit);
            $knownFailure=$last['status']>=400 || in_array($last['errno'],[1,3,5,6,7,35,60],true);
            $this->s->settle($rid,$last['usage'],$knownFailure,substr($route['azure_model'].':'.$last['provider'],0,150));
            if($last['usage']!==null)$sum=self::addUsage($sum,$last['usage']);
            elseif(!$knownFailure)$pending=true;
            $answer=$last['visible'];
            $blocked=$last['blocked'];
            $failed=$last['ok']===false || $last['stream_error'] || $last['status']>=400 || !$answer;
            $canFallback=$last['errno']!==0 || $last['stream_error'] || ($last['status']>=200 && $last['status']<300) || in_array($last['status'],[401,404,408,429],true) || $last['status']>=500;
            if(!$isBackup && $fallback) {
                if(!$failed)$this->markHealth(false);
                elseif(!$answer && !$blocked && $canFallback)$this->markHealth(true);
            }
            if($answer || $blocked || !$fallback || $isBackup)break;
            if(!$canFallback)break;
            // No provider text has reached the user; one backup attempt can safely take over.
        }
        if(!$answer) {
            if(!$started)throw new Problem(503,'AI could not return an answer. Check account usage before retrying.');
            $emit(json_encode(['error'=>['message'=>'AI could not complete this answer. Check account usage before retrying.']],JSON_THROW_ON_ERROR));
        }
        if($sum!==null)$emit(json_encode(['choices'=>[],'usage'=>$sum,'usage_incomplete'=>$pending,'wallet'=>$this->s->wallet($user)],JSON_THROW_ON_ERROR));
        if($sum===null || ($last!==null && $answer && ($last['ok']===false || $last['stream_error'])))
            $emit(json_encode(['error'=>['message'=>'The response ended without complete verified usage or answer. Pending credits require reconciliation.']],JSON_THROW_ON_ERROR));
        elseif($pending)$emit(json_encode(['notice'=>'An earlier provider attempt has unverified usage; its credits remain reserved for reconciliation.','usage_incomplete'=>true],JSON_THROW_ON_ERROR));
        $emit('[DONE]');
    }
    private static function addUsage(?array $sum,array $usage): array {
        if($sum===null)return $usage;
        foreach(['prompt_tokens','completion_tokens','total_tokens'] as $field)$sum[$field]+=$usage[$field];
        foreach(['prompt_tokens_details','completion_tokens_details'] as $field) {
            if(!is_array($sum[$field]??null) || !is_array($usage[$field]??null)) { unset($sum[$field]); continue; }
            foreach($sum[$field] as $name=>$value) {
                if(is_int($value) && is_int($usage[$field][$name]??null))$sum[$field][$name]+=$usage[$field][$name];
                else unset($sum[$field][$name]);
            }
        }
        return $sum;
    }
    private function healthFile(): string {
        return ($this->c['ai_health_directory']??dirname(__DIR__).'/storage').'/ai-health-'.hash('sha256',$this->c['azure_url']).'.json';
    }
    private function coolingDown(): bool {
        $file=@fopen($this->healthFile(),'rb'); if(!$file)return false;
        flock($file,LOCK_SH); $data=json_decode(stream_get_contents($file),true); flock($file,LOCK_UN); fclose($file);
        return (int)($data['retry_after']??0)>time();
    }
    private function markHealth(bool $failed): void {
        $seconds=max(0,(int)($this->c['azure_fallback_cooldown']??30)); if($seconds===0)return;
        $path=$this->healthFile(); if(!is_dir(dirname($path)))@mkdir(dirname($path),0700,true);
        // Private, best-effort state. Failure to persist does not prevent this request's fallback.
        $file=@fopen($path,'c+'); if(!$file)return;
        flock($file,LOCK_EX); ftruncate($file,0); fwrite($file,json_encode(['retry_after'=>$failed?time()+$seconds:0])); fflush($file); flock($file,LOCK_UN); fclose($file);
    }
    private function attempt(array $route,string $encoded,string $rid,int $timeout,float $firstText,callable $emit): array {
        $result=['usage'=>null,'provider'=>'','status'=>0,'visible'=>false,'blocked'=>false,'stream_error'=>false,'ok'=>false,'errno'=>0];
        $buffer=''; $model=$route['azure_model']; $began=microtime(true);
        $consume=function(string $line) use(&$result,&$model,$emit): void {
            if(strncmp($line,'data:',5)!==0)return; $data=trim(substr($line,5)); if($data==='' || $data==='[DONE]')return;
            $event=json_decode($data,true); if(!is_array($event))return;
            if(is_string($event['model']??null))$model=$event['model'];
            if(isset($event['usage']))$result['usage']=self::usage($event['usage'])??$result['usage'];
            if(isset($event['error'])) {
                $result['stream_error']=true;
                if(str_contains(strtolower((string)($event['error']['code']??'')),'content_filter'))$result['blocked']=true;
            }
            foreach($event['choices']??[] as $choice) {
                $content=$choice['delta']['content']??null; $finish=$choice['finish_reason']??null;
                if($finish==='content_filter' || !empty($choice['delta']['refusal']))$result['blocked']=true;
                if(is_string($content) && $content!=='' && ($result['visible'] || trim($content)!=='')) {
                    if(!$result['visible'])$emit(json_encode(['model'=>$model],JSON_THROW_ON_ERROR));
                    $result['visible']=true;
                    $emit(json_encode(['choices'=>[['index'=>0,'delta'=>['content'=>$content],'finish_reason'=>is_string($finish)?$finish:null]]],JSON_THROW_ON_ERROR));
                } elseif($result['visible'] && is_string($finish))$emit(json_encode(['choices'=>[['index'=>0,'delta'=>(object)[],'finish_reason'=>$finish]]],JSON_THROW_ON_ERROR));
            }
        };
        $curl=curl_init($route['azure_url']);
        $progressOption=defined('CURLOPT_XFERINFOFUNCTION')?constant('CURLOPT_XFERINFOFUNCTION'):CURLOPT_PROGRESSFUNCTION;
        curl_setopt_array($curl,[CURLOPT_POST=>true,CURLOPT_POSTFIELDS=>$encoded,CURLOPT_FOLLOWLOCATION=>false,
            CURLOPT_CONNECTTIMEOUT=>min(4,$timeout),CURLOPT_TIMEOUT=>$timeout,CURLOPT_NOPROGRESS=>false,
            $progressOption=>function() use(&$result,$began,$firstText): int { return !$result['visible'] && microtime(true)-$began>=$firstText?1:0; },
            CURLOPT_HTTPHEADER=>['Content-Type: application/json','api-key: '.$route['azure_key'],'extra-parameters: pass-through','x-ms-client-request-id: '.$rid],
            CURLOPT_HEADERFUNCTION=>function($curl,string $line) use(&$result): int {
                if(preg_match('/^HTTP\/\S+\s+(\d+)/',$line,$m))$result['status']=(int)$m[1];
                if(preg_match('/^(?:x-ms-request-id|apim-request-id):\s*([^\r\n]+)/i',$line,$m))$result['provider']=substr(trim($m[1]),0,150);
                return strlen($line);
            },
            CURLOPT_WRITEFUNCTION=>function($curl,string $chunk) use(&$buffer,&$result,$consume): int {
                if($result['status']<200 || $result['status']>=300)return strlen($chunk);
                $buffer.=$chunk; if(strlen($buffer)>1024*1024)return 0;
                while(($position=strpos($buffer,"\n"))!==false) { $line=rtrim(substr($buffer,0,$position),"\r"); $buffer=substr($buffer,$position+1); $consume($line); }
                return strlen($chunk);
            }]);
        $result['ok']=curl_exec($curl); $result['errno']=curl_errno($curl); $result['status']=curl_getinfo($curl,CURLINFO_RESPONSE_CODE); curl_close($curl);
        if($buffer!=='')$consume($buffer);
        return $result;
    }
}
