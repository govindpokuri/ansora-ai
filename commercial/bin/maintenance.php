<?php
declare(strict_types=1);
if(PHP_SAPI!=='cli')exit(1);
require dirname(__DIR__).'/bootstrap.php';
$s=new \CueMitra\Store(commercial_config());
foreach(['challenges','sessions','rate_limits'] as $table)$s->run('DELETE FROM '.$table.' WHERE expires_at < ?',[time()]);
echo "Expired sign-in records cleaned. Billing records preserved.\n";
