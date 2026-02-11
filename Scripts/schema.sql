-- Alabama Walkability API schema
-- Run against MySQL 8.0+ (spatial support required)
-- EPA Walkability Index: GEOID = state(2) + county(3) + tract(6) + block group(1) = 12 chars

CREATE DATABASE IF NOT EXISTS alabama_walkability;
USE alabama_walkability;

CREATE TABLE counties (
    state_fips CHAR(2) NOT NULL,
    fips CHAR(3) NOT NULL,
    name VARCHAR(100) NOT NULL,
    avg_walkability DECIMAL(10,4) NOT NULL DEFAULT 0,
    block_group_count INT NOT NULL DEFAULT 0,
    population INT NOT NULL DEFAULT 0,
    PRIMARY KEY (state_fips, fips)
);

CREATE TABLE block_groups (
    fips CHAR(12) NOT NULL PRIMARY KEY,
    state_fips CHAR(2) NOT NULL,
    county_fips CHAR(3) NOT NULL,
    tract_fips CHAR(11) NOT NULL,
    walkability_score DECIMAL(10,4) NOT NULL,
    population INT NOT NULL DEFAULT 0,
    housing_units INT NOT NULL DEFAULT 0,
    geometry GEOMETRY SRID 4326,
    INDEX idx_state (state_fips),
    INDEX idx_county (state_fips, county_fips)
);


-- ETL will populate from EPA SLD CSV:
-- GEOID -> fips, extract state/county/tract from GEOID
-- NatWalkInd -> walkability_score
-- D1B -> population, D1A -> housing_units (or equivalent SLD columns)
