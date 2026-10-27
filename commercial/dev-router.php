<?php
$path=parse_url($_SERVER['REQUEST_URI'],PHP_URL_PATH);
putenv('CUEMITRA_APP_ROOT='.__DIR__);
if(str_starts_with($path,'/api/')) { $_GET['route']=substr($path,5); require __DIR__.'/public/api.php'; return true; }
$pages=['/privacy'=>'/privacy.html','/terms'=>'/terms.html','/refunds'=>'/refunds.html'];
if(isset($pages[$path])) { readfile(__DIR__.'/public'.$pages[$path]); return true; }
if(in_array($path,['/','/dashboard','/admin'],true)) { readfile(__DIR__.'/public/index.html'); return true; }
return false;
