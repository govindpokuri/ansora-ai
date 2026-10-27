<?php
// Standalone transport integration test; synthetic Store avoids touching customer accounts.
declare(strict_types=1);
namespace CueMitra;
final class Problem extends \RuntimeException { public function __construct(int $status,string $message) { parent::__construct($message,$status); } }
final class Store {
    public ?array $settled=null;
    public function limit(...$args): void {}
    public function reserve(...$args): string { return 'synthetic-request'; }
    public function settle(string $id,?array $usage,bool $failure,string $provider): void { $this->settled=$usage; }
    public function wallet(string $user): array { return ['available'=>99885]; }
}
require dirname(__DIR__).'/src/Ai.php';
$store=new Store();
$config=['azure_url'=>'http://127.0.0.1:28182/stream','azure_key'=>'synthetic-upstream-key','azure_model'=>'synthetic-model','max_output_tokens'=>4096,'upstream_timeout'=>5];
$input=['messages'=>[['role'=>'user','content'=>'Explain threads']],'max_tokens'=>4096];
if(isset($argv[1])) {
    $config['azure_url']='http://127.0.0.1:28182/inspect-limit'; $config['azure_model_family']='gpt-4.1-mini';
    if($argv[1]==='code')$input['messages'][0]['content']='Write a C# palindrome function';
    if($argv[1]==='resume')$input['purpose']='resume';
}
ob_start();
$ai=new Ai($store,$config);
$ai->stream('synthetic-user',$input,'synthetic-request-123456');
ob_end_flush();
if(($store->settled['total_tokens']??null)!==115)throw new \RuntimeException('Usage settlement changed');
