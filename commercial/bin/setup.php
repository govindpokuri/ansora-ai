<?php
// CLI only: import schema. Configuration and secrets must remain outside public_html.
if(PHP_SAPI!=='cli')exit(1);
require dirname(__DIR__).'/bootstrap.php';
$c=commercial_config(); $s=new \CueMitra\Store($c);
$s->db->exec(file_get_contents(dirname(__DIR__).'/database/schema.sql'));
echo "Database schema installed. No secrets printed.\n";
