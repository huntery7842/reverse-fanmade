CREATE TABLE save_snapshots (
    account_id TEXT NOT NULL REFERENCES accounts(id),
    slot TEXT NOT NULL,
    revision INTEGER NOT NULL CHECK (revision > 0),
    request_id TEXT NOT NULL,
    request_digest TEXT NOT NULL,
    payload BLOB NOT NULL,
    payload_digest TEXT NOT NULL,
    build_id TEXT NOT NULL,
    format_version TEXT NOT NULL,
    catalogue_id TEXT NOT NULL,
    client_save_hash TEXT,
    battlepass_json TEXT,
    created_at INTEGER NOT NULL,
    PRIMARY KEY (account_id, slot, revision),
    UNIQUE (account_id, slot, request_id)
);
CREATE TABLE save_heads (
    account_id TEXT NOT NULL,
    slot TEXT NOT NULL,
    revision INTEGER NOT NULL,
    PRIMARY KEY (account_id, slot),
    FOREIGN KEY (account_id, slot, revision) REFERENCES save_snapshots(account_id, slot, revision)
);
CREATE TABLE battlepass_progress (
    account_id TEXT NOT NULL,
    slot TEXT NOT NULL,
    revision INTEGER NOT NULL,
    catalogue_id TEXT NOT NULL,
    accumulated_cp INTEGER NOT NULL,
    free_reward_cp_level INTEGER NOT NULL,
    premium_reward_cp_level INTEGER NOT NULL,
    is_current_premium_battle_pass_bought INTEGER NOT NULL CHECK (is_current_premium_battle_pass_bought IN (0, 1)),
    accumulated_star_cp INTEGER NOT NULL,
    last_used_star_level INTEGER NOT NULL,
    stat_star_level_accumlated_cp_total INTEGER NOT NULL,
    stat_star_level_complete_count_tu_reset INTEGER NOT NULL,
    stat_star_level_complete_count_total INTEGER NOT NULL,
    stat_star_level_got_rp_booster_free INTEGER NOT NULL,
    stat_star_level_got_rp_booster_premium INTEGER NOT NULL,
    last_patch_time INTEGER NOT NULL,
    PRIMARY KEY (account_id, slot, revision),
    FOREIGN KEY (account_id, slot, revision) REFERENCES save_snapshots(account_id, slot, revision)
);
CREATE TABLE battlepass_quests (
    account_id TEXT NOT NULL,
    slot TEXT NOT NULL,
    revision INTEGER NOT NULL,
    ordinal INTEGER NOT NULL CHECK (ordinal BETWEEN 0 AND 8),
    ui_index INTEGER NOT NULL,
    quest_id TEXT NOT NULL,
    quest_begin_time INTEGER NOT NULL,
    current_quest_point TEXT NOT NULL,
    is_premium INTEGER NOT NULL CHECK (is_premium IN (0, 1)),
    PRIMARY KEY (account_id, slot, revision, ordinal),
    FOREIGN KEY (account_id, slot, revision) REFERENCES battlepass_progress(account_id, slot, revision)
);
