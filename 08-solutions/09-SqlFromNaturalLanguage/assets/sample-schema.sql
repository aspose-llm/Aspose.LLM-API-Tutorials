-- Sample schema for the SQL-from-Natural-Language tutorial.
-- A small SaaS billing model: regions, accounts, users, invoices, subscriptions.
-- Foreign keys, NOT NULLs, and CHECK constraints give the model useful anchors
-- when interpreting plain-English questions.

CREATE TABLE regions (
    id          SERIAL PRIMARY KEY,
    code        VARCHAR(8)  NOT NULL UNIQUE,           -- e.g. 'NA', 'EU', 'APAC'
    name        VARCHAR(64) NOT NULL,
    currency    CHAR(3)     NOT NULL DEFAULT 'USD'
);

CREATE TABLE accounts (
    id              SERIAL PRIMARY KEY,
    name            VARCHAR(128) NOT NULL,
    region_id       INT          NOT NULL REFERENCES regions(id),
    plan            VARCHAR(16)  NOT NULL CHECK (plan IN ('free', 'pro', 'enterprise')),
    is_paid         BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    churned_at      TIMESTAMP             -- NULL = still active
);

CREATE TABLE users (
    id              SERIAL PRIMARY KEY,
    account_id      INT          NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    email           VARCHAR(254) NOT NULL UNIQUE,
    full_name       VARCHAR(128) NOT NULL,
    role            VARCHAR(16)  NOT NULL CHECK (role IN ('owner', 'admin', 'member')),
    last_login_at   TIMESTAMP,
    created_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE subscriptions (
    id              SERIAL PRIMARY KEY,
    account_id      INT          NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
    plan            VARCHAR(16)  NOT NULL CHECK (plan IN ('free', 'pro', 'enterprise')),
    started_at      DATE         NOT NULL,
    ended_at        DATE,                              -- NULL = active
    monthly_amount  NUMERIC(10, 2) NOT NULL CHECK (monthly_amount >= 0)
);

CREATE TABLE invoices (
    id              SERIAL PRIMARY KEY,
    account_id      INT          NOT NULL REFERENCES accounts(id),
    subscription_id INT                   REFERENCES subscriptions(id),
    issued_at       DATE         NOT NULL,
    paid_at         DATE,                              -- NULL = unpaid / open
    amount          NUMERIC(10, 2) NOT NULL CHECK (amount >= 0),
    currency        CHAR(3)      NOT NULL DEFAULT 'USD',
    status          VARCHAR(16)  NOT NULL CHECK (status IN ('open', 'paid', 'void', 'refunded'))
);

CREATE INDEX idx_accounts_region    ON accounts(region_id);
CREATE INDEX idx_users_account      ON users(account_id);
CREATE INDEX idx_invoices_account   ON invoices(account_id);
CREATE INDEX idx_invoices_paid_at   ON invoices(paid_at);
CREATE INDEX idx_subs_account       ON subscriptions(account_id);
