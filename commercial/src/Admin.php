<?php
declare(strict_types=1);
namespace CueMitra;

final class AdminReports {
    public function __construct(private Store $s) {}
    public function overview(string $search=''): array {
        $search=trim($search);
        if(strlen($search)>254)throw new Problem(422,'Search must be at most 254 characters.');
        $where=$search===''?'':'WHERE LOCATE(?,u.email)>0';
        $users=$this->s->all("SELECT u.id,u.email,u.role,u.status,u.created_at,u.last_login,
            COALESCE(w.balance,0) AS balance,COALESCE(w.reserved,0) AS reserved,
            (SELECT COUNT(*) FROM purchases p WHERE p.user_id=u.id AND p.status='captured') AS purchase_count,
            (SELECT COALESCE(SUM(p.amount),0) FROM purchases p WHERE p.user_id=u.id AND p.status='captured') AS spent_paise,
            (SELECT COALESCE(SUM(p.tokens),0) FROM purchases p WHERE p.user_id=u.id AND p.status='captured') AS purchased_tokens,
            (SELECT COALESCE(SUM(r.total_tokens),0) FROM usage_requests r WHERE r.user_id=u.id AND r.status='completed') AS tokens_used,
            (SELECT MAX(r.created_at) FROM usage_requests r WHERE r.user_id=u.id) AS last_usage
            FROM users u LEFT JOIN wallets w ON w.user_id=u.id $where ORDER BY u.created_at DESC,u.id DESC LIMIT 200",$search===''?[]:[$search]);
        return [
            'stats'=>$this->s->one("SELECT (SELECT COUNT(*) FROM users) AS accounts,
                (SELECT COUNT(DISTINCT user_id) FROM purchases WHERE status='captured') AS paying_accounts,
                (SELECT COALESCE(SUM(amount),0) FROM purchases WHERE status='captured') AS revenue_paise,
                (SELECT COALESCE(SUM(total_tokens),0) FROM usage_requests WHERE status='completed') AS tokens_used,
                (SELECT COUNT(*) FROM usage_requests WHERE status='reconcile' OR (status='reserved' AND created_at<UNIX_TIMESTAMP()-120)) AS pending_usage"),
            'users'=>$users,
            'purchases'=>$this->purchases(), 'usage'=>$this->usage(),
            'pending'=>$this->s->all("SELECT r.id,u.email,r.status,r.reserved,r.provider_id,r.created_at FROM usage_requests r JOIN users u ON u.id=r.user_id WHERE r.status='reconcile' OR (r.status='reserved' AND r.created_at<UNIX_TIMESTAMP()-120) ORDER BY r.created_at LIMIT 100"),
            'audit'=>$this->s->all('SELECT action,target_id,details,created_at FROM audit ORDER BY created_at DESC LIMIT 100'),
        ];
    }
    private function purchases(string $user=''): array {
        $where=$user===''?'':'WHERE p.user_id=?';
        return $this->s->all("SELECT p.id,u.email,p.order_id,p.payment_id,p.amount,p.currency,p.tokens,p.status,p.created_at FROM purchases p JOIN users u ON u.id=p.user_id $where ORDER BY p.created_at DESC,p.id DESC LIMIT 100",$user===''?[]:[$user]);
    }
    private function usage(string $user=''): array {
        $where=$user===''?'':'WHERE r.user_id=?';
        return $this->s->all("SELECT r.id,u.email,r.purpose,r.input_tokens,r.output_tokens,r.total_tokens,r.status,r.reserved,r.created_at FROM usage_requests r JOIN users u ON u.id=r.user_id $where ORDER BY r.created_at DESC,r.id DESC LIMIT 100",$user===''?[]:[$user]);
    }
    public function account(string $user): array {
        if(!preg_match('/^[a-f0-9]{32}$/',$user))throw new Problem(422,'Choose a valid account.');
        $u=$this->s->one('SELECT id,email FROM users WHERE id=?',[$user]);
        if(!$u)throw new Problem(404,'Account not found.');
        return ['user'=>$u,'wallet'=>$this->s->wallet($user),'purchases'=>$this->purchases($user),'usage'=>$this->usage($user),
            'ledger'=>$this->s->all('SELECT delta,description,reference_key,created_at FROM ledger WHERE user_id=? ORDER BY created_at DESC,id DESC LIMIT 100',[$user])];
    }
}
