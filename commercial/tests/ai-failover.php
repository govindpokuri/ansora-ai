<?php
declare(strict_types=1);
namespace CueMitra;
final class Problem extends \RuntimeException { public function __construct(int $status,string $message) { parent::__construct($message,$status); } }
final class Store {
    public array $reservations=[],$settlements=[];
    public function limit(...$args): void {}
    public function reserve(string $user,string $key,string $hash,string $session,string $purpose,int $tokens): string {
        foreach($this->reservations as $r)if($r['key']===$key)throw new Problem(409,'Duplicate request');
        $id='request-'.count($this->reservations); $this->reservations[]=['id'=>$id,'key'=>$key,'tokens'=>$tokens]; return $id;
    }
    public function settle(string $id,?array $usage,bool $failure,string $provider): void { $this->settlements[]=['id'=>$id,'usage'=>$usage,'failed'=>$failure,'provider'=>$provider]; }
    public function wallet(string $user): array { return ['available'=>999999]; }
}
require dirname(__DIR__).'/src/Ai.php';
$scenario=$argv[1]??'healthy';
$paths=['healthy'=>'/success','outage'=>'/unavailable','rate'=>'/rate-limit','empty'=>'/empty','slow'=>'/slow','blocked'=>'/blocked','bad'=>'/bad-request','partial'=>'/partial-error','both'=>'/unavailable','cooldown'=>'/unavailable','disabled'=>'/unavailable','low-limit'=>'/unavailable','duplicate'=>'/success','continuation'=>'/success'];
$store=new Store(); $directory=dirname(__DIR__,2).'/artifacts/ai-health-test-'.getmypid();
$config=['azure_url'=>'http://127.0.0.1:28182'.$paths[$scenario],'azure_key'=>'synthetic-upstream-key','azure_model'=>'gpt-4.1-mini','azure_model_family'=>'gpt-4.1-mini',
    'max_output_tokens'=>4096,'upstream_timeout'=>10,'azure_fallback_enabled'=>$scenario!=='disabled','azure_fallback_url'=>'http://127.0.0.1:28183'.($scenario==='both'?'/backup-fail':'/backup'),
    'azure_fallback_key'=>'synthetic-backup-key','azure_fallback_model'=>'gpt-oss-120b','azure_fallback_cooldown'=>$scenario==='cooldown'?30:0,
    'azure_first_text_timeout'=>1,'azure_fallback_first_text_timeout'=>4,'azure_fallback_max_tokens'=>4096,'ai_health_directory'=>$directory];
$ai=new Ai($store,$config); $input=['messages'=>[['role'=>'user','content'=>'Explain threads']],'max_tokens'=>4096];
if($scenario==='low-limit') { $config['max_output_tokens']=320; $input['max_tokens']=320; $ai=new Ai($store,$config); }
if($scenario==='continuation')$input['continuation_model']='gpt-oss-120b';
$start=microtime(true); $error=null;
ob_start();
try { $ai->stream('test-user',$input,'test-logical-request-12345'); } catch(Problem $e) { $error=$e->getCode(); }
if($scenario==='duplicate')try { $ai->stream('test-user',$input,'test-logical-request-12345'); } catch(Problem $e) { $error=$e->getCode(); }
if($scenario==='cooldown') {
    $ai->stream('test-user',$input,'test-logical-request-67890');
    // Expire the cooldown, then prove that the primary is probed again.
    foreach(glob($directory.'/*.json') as $file)file_put_contents($file,'{"retry_after":0}');
    $ai->stream('test-user',$input,'test-logical-request-99999');
}
ob_end_flush();
foreach(glob($directory.'/*.json') as $file)unlink($file); if(is_dir($directory))rmdir($directory);
echo 'TEST_RESULT='.json_encode(['scenario'=>$scenario,'error'=>$error,'elapsed'=>microtime(true)-$start,'reservations'=>$store->reservations,'settlements'=>$store->settlements])."\n";
