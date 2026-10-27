CREATE TABLE IF NOT EXISTS users (
 id CHAR(32) PRIMARY KEY, email VARCHAR(254) NOT NULL UNIQUE,
 role VARCHAR(16) NOT NULL DEFAULT 'member', status VARCHAR(16) NOT NULL DEFAULT 'active',
 created_at BIGINT NOT NULL, last_login BIGINT NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS wallets (
 user_id CHAR(32) PRIMARY KEY, balance BIGINT NOT NULL DEFAULT 0, reserved BIGINT NOT NULL DEFAULT 0,
 FOREIGN KEY (user_id) REFERENCES users(id),
 CONSTRAINT chk_wallet_balance CHECK (balance >= 0),
 CONSTRAINT chk_wallet_reserved CHECK (reserved >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS consents (
 id CHAR(32) PRIMARY KEY, user_id CHAR(32) NOT NULL, policy_version VARCHAR(30) NOT NULL,
 client VARCHAR(16) NOT NULL, accepted_at BIGINT NOT NULL, FOREIGN KEY(user_id) REFERENCES users(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS challenges (
 id CHAR(64) PRIMARY KEY, email VARCHAR(254) NOT NULL, code_hash CHAR(64) NOT NULL,
 expires_at BIGINT NOT NULL, attempts INT NOT NULL DEFAULT 0, created_at BIGINT NOT NULL,
 INDEX(email,created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS sessions (
 token_hash CHAR(64) PRIMARY KEY, user_id CHAR(32) NOT NULL, client VARCHAR(16) NOT NULL,
 expires_at BIGINT NOT NULL, created_at BIGINT NOT NULL,
 FOREIGN KEY (user_id) REFERENCES users(id), INDEX(user_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS rate_limits (
 bucket CHAR(64) PRIMARY KEY, hits INT NOT NULL, expires_at BIGINT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS purchases (
 id CHAR(32) PRIMARY KEY, user_id CHAR(32) NOT NULL, order_id VARCHAR(100) UNIQUE,
 payment_id VARCHAR(100) UNIQUE, amount BIGINT NOT NULL, currency CHAR(3) NOT NULL DEFAULT 'INR',
 tokens BIGINT NOT NULL, status VARCHAR(20) NOT NULL DEFAULT 'created', created_at BIGINT NOT NULL,
 FOREIGN KEY (user_id) REFERENCES users(id), INDEX(user_id,created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS usage_requests (
 id CHAR(32) PRIMARY KEY, user_id CHAR(32) NOT NULL, request_key VARCHAR(64) NOT NULL,
 payload_hash CHAR(64) NOT NULL, session_id VARCHAR(64) NOT NULL, purpose VARCHAR(16) NOT NULL,
 reserved BIGINT NOT NULL, input_tokens BIGINT, output_tokens BIGINT, total_tokens BIGINT,
 status VARCHAR(20) NOT NULL DEFAULT 'reserved', provider_id VARCHAR(150) NOT NULL DEFAULT '',
 created_at BIGINT NOT NULL, completed_at BIGINT, UNIQUE(user_id,request_key),
 FOREIGN KEY (user_id) REFERENCES users(id), INDEX(user_id,created_at), INDEX(status,created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS ledger (
 id CHAR(32) PRIMARY KEY, user_id CHAR(32) NOT NULL, delta BIGINT NOT NULL,
 reference_key VARCHAR(150) NOT NULL UNIQUE, description VARCHAR(255) NOT NULL,
 created_at BIGINT NOT NULL, FOREIGN KEY (user_id) REFERENCES users(id), INDEX(user_id,created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS audit (
 id CHAR(32) PRIMARY KEY, actor_id CHAR(32), action VARCHAR(60) NOT NULL,
 target_id VARCHAR(100) NOT NULL, details TEXT NOT NULL, created_at BIGINT NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
