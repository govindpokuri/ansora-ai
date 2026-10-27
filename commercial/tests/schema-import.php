<?php
declare(strict_types=1);
require dirname(__DIR__).'/bootstrap.php';
$c=commercial_config();
if($c['environment']!=='development' || !str_contains($c['dsn'],'cuemitra_test'))exit(9);
$pdo=new PDO($c['dsn'],$c['db_user'],$c['db_password'],[PDO::ATTR_ERRMODE=>PDO::ERRMODE_EXCEPTION]);
$schema=file_get_contents(dirname(__DIR__).'/database/schema.sql');
$statements=array_values(array_filter(array_map('trim',explode(';',$schema))));
$passed=0;
foreach(['fresh','partial'] as $scenario) {
    $db='ansora_schema_check_'.bin2hex(random_bytes(6));
    try {
        $pdo->exec("CREATE DATABASE `$db` CHARACTER SET latin1 COLLATE latin1_swedish_ci");
        $pdo->exec("USE `$db`");
        if($scenario==='partial') {
            $pdo->exec($statements[0]);
            $failed=false;
            try { $pdo->exec('CREATE TABLE wallets (user_id CHAR(32) PRIMARY KEY, balance BIGINT NOT NULL DEFAULT 0, reserved BIGINT NOT NULL DEFAULT 0, FOREIGN KEY (user_id) REFERENCES users(id)) ENGINE=InnoDB'); }
            catch(PDOException $e) { if(($e->errorInfo[1]??0)!==1005)throw $e; $failed=true; }
            if(!$failed)throw new RuntimeException('Expected original mixed-charset foreign key failure.');
            echo "PASS: reproduced original foreign key failure under a latin1 database default\n"; $passed++;
        }
        foreach($statements as $sql)$pdo->exec($sql);
        foreach($statements as $sql)$pdo->exec($sql);
        $tables=$pdo->query('SHOW TABLES')->fetchAll(PDO::FETCH_COLUMN);
        if(count($tables)!==10)throw new RuntimeException('Expected all ten application tables.');
        $charsets=$pdo->query("SELECT DISTINCT character_set_name FROM information_schema.columns WHERE table_schema=DATABASE() AND character_set_name IS NOT NULL")->fetchAll(PDO::FETCH_COLUMN);
        if($charsets!==['utf8mb4'])throw new RuntimeException('Inconsistent table character sets.');
        $pdo->exec("INSERT INTO users (id,email,created_at) VALUES ('schema-check','schema-check@example.test',0)");
        $pdo->exec("INSERT INTO wallets (user_id) VALUES ('schema-check')");
        $rejected=false;
        try { $pdo->exec("UPDATE wallets SET balance=-1 WHERE user_id='schema-check'"); }
        catch(PDOException $e) { $rejected=true; }
        if(!$rejected)throw new RuntimeException('Wallet nonnegative constraint was not enforced.');
        echo "PASS: $scenario import and reimport create all ten tables with consistent character sets and enforce wallet constraints\n"; $passed++;
    } finally { $pdo->exec("DROP DATABASE IF EXISTS `$db`"); }
}
echo "PASS: $passed schema import checks\n";
