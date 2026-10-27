<?php
require dirname(__DIR__).'/bootstrap.php';
$c=commercial_config(); if($c['environment']!=='development' || !str_contains($c['dsn'],'cuemitra_test'))exit(9);
$s=new \CueMitra\Store($c);
try { $s->reserve($argv[1],bin2hex(random_bytes(16)),hash('sha256','concurrent'),'test','answer',60000); echo "reserved\n"; exit(0); }
catch(\CueMitra\Problem $e) { if($e->status===402) { echo "blocked\n"; exit(2); } throw $e; }
