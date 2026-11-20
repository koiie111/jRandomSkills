CREATE TABLE IF NOT EXISTS jrandomskills_hud_intro (
    steam_id BIGINT UNSIGNED NOT NULL,
    acknowledged_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (steam_id)
) ENGINE=InnoDB;
