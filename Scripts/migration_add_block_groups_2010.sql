-- Run once to add 2010 vintage table for trend-based forecast (existing DBs).
USE alabama_walkability;

CREATE TABLE IF NOT EXISTS block_groups_2010 (
    fips CHAR(12) NOT NULL PRIMARY KEY,
    state_fips CHAR(2) NOT NULL,
    county_fips CHAR(3) NOT NULL,
    tract_fips CHAR(11) NOT NULL,
    walkability_score DECIMAL(10,4) NOT NULL,
    population INT NOT NULL DEFAULT 0,
    housing_units INT NOT NULL DEFAULT 0,
    INDEX idx_state (state_fips)
);
