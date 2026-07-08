-- TimescaleDB initialization script for metrics storage
-- Creates hypertables and continuous aggregates for time-series data

-- Enable TimescaleDB extension
CREATE EXTENSION IF NOT EXISTS timescaledb CASCADE;

-- Create schema for metrics
CREATE SCHEMA IF NOT EXISTS metrics;

-- ============================================================================
-- METRICS HYPERTABLE
-- ============================================================================

-- Main metrics table (hypertable)
CREATE TABLE IF NOT EXISTS metrics.raw_metrics (
    time            TIMESTAMPTZ         NOT NULL,
    metric_name     TEXT                NOT NULL,
    value           DOUBLE PRECISION    NOT NULL,
    -- Resource attributes
    service_name    TEXT,
    service_version TEXT,
    host_name       TEXT,
    environment     TEXT,
    -- Additional labels stored as JSONB for flexibility
    labels          JSONB               DEFAULT '{}',
    -- Metric metadata
    metric_type     TEXT,               -- gauge, counter, histogram, summary
    unit            TEXT
);

-- Convert to hypertable with 1-hour chunks
SELECT create_hypertable(
    'metrics.raw_metrics',
    'time',
    chunk_time_interval => INTERVAL '1 hour',
    if_not_exists => TRUE
);

-- Create indexes for common query patterns
CREATE INDEX IF NOT EXISTS idx_metrics_name_time ON metrics.raw_metrics (metric_name, time DESC);
CREATE INDEX IF NOT EXISTS idx_metrics_service_time ON metrics.raw_metrics (service_name, time DESC);
CREATE INDEX IF NOT EXISTS idx_metrics_host_time ON metrics.raw_metrics (host_name, time DESC);
CREATE INDEX IF NOT EXISTS idx_metrics_labels ON metrics.raw_metrics USING GIN (labels);

-- ============================================================================
-- CONTINUOUS AGGREGATES
-- ============================================================================

-- 1-minute aggregates
CREATE MATERIALIZED VIEW IF NOT EXISTS metrics.metrics_1min
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 minute', time) AS bucket,
    metric_name,
    service_name,
    host_name,
    COUNT(*) as sample_count,
    AVG(value) as avg_value,
    MIN(value) as min_value,
    MAX(value) as max_value,
    STDDEV(value) as stddev_value,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY value) as p50_value,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY value) as p95_value,
    PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY value) as p99_value
FROM metrics.raw_metrics
GROUP BY bucket, metric_name, service_name, host_name
WITH NO DATA;

-- 5-minute aggregates
CREATE MATERIALIZED VIEW IF NOT EXISTS metrics.metrics_5min
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('5 minutes', time) AS bucket,
    metric_name,
    service_name,
    host_name,
    COUNT(*) as sample_count,
    AVG(value) as avg_value,
    MIN(value) as min_value,
    MAX(value) as max_value,
    STDDEV(value) as stddev_value,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY value) as p50_value,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY value) as p95_value,
    PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY value) as p99_value
FROM metrics.raw_metrics
GROUP BY bucket, metric_name, service_name, host_name
WITH NO DATA;

-- 1-hour aggregates
CREATE MATERIALIZED VIEW IF NOT EXISTS metrics.metrics_1hour
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 hour', time) AS bucket,
    metric_name,
    service_name,
    host_name,
    COUNT(*) as sample_count,
    AVG(value) as avg_value,
    MIN(value) as min_value,
    MAX(value) as max_value,
    STDDEV(value) as stddev_value,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY value) as p50_value,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY value) as p95_value,
    PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY value) as p99_value
FROM metrics.raw_metrics
GROUP BY bucket, metric_name, service_name, host_name
WITH NO DATA;

-- 1-day aggregates for long-term analysis
CREATE MATERIALIZED VIEW IF NOT EXISTS metrics.metrics_1day
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 day', time) AS bucket,
    metric_name,
    service_name,
    host_name,
    COUNT(*) as sample_count,
    AVG(value) as avg_value,
    MIN(value) as min_value,
    MAX(value) as max_value,
    STDDEV(value) as stddev_value
FROM metrics.raw_metrics
GROUP BY bucket, metric_name, service_name, host_name
WITH NO DATA;

-- ============================================================================
-- REFRESH POLICIES FOR CONTINUOUS AGGREGATES
-- ============================================================================

SELECT add_continuous_aggregate_policy('metrics.metrics_1min',
    start_offset => INTERVAL '3 hours',
    end_offset => INTERVAL '1 minute',
    schedule_interval => INTERVAL '1 minute',
    if_not_exists => TRUE
);

SELECT add_continuous_aggregate_policy('metrics.metrics_5min',
    start_offset => INTERVAL '12 hours',
    end_offset => INTERVAL '5 minutes',
    schedule_interval => INTERVAL '5 minutes',
    if_not_exists => TRUE
);

SELECT add_continuous_aggregate_policy('metrics.metrics_1hour',
    start_offset => INTERVAL '3 days',
    end_offset => INTERVAL '1 hour',
    schedule_interval => INTERVAL '1 hour',
    if_not_exists => TRUE
);

SELECT add_continuous_aggregate_policy('metrics.metrics_1day',
    start_offset => INTERVAL '30 days',
    end_offset => INTERVAL '1 day',
    schedule_interval => INTERVAL '1 day',
    if_not_exists => TRUE
);

-- ============================================================================
-- RETENTION POLICIES
-- ============================================================================

-- Raw metrics: keep for 7 days
SELECT add_retention_policy('metrics.raw_metrics',
    INTERVAL '7 days',
    if_not_exists => TRUE
);

-- 1-min aggregates: keep for 30 days
SELECT add_retention_policy('metrics.metrics_1min',
    INTERVAL '30 days',
    if_not_exists => TRUE
);

-- 5-min aggregates: keep for 90 days
SELECT add_retention_policy('metrics.metrics_5min',
    INTERVAL '90 days',
    if_not_exists => TRUE
);

-- 1-hour aggregates: keep for 1 year
SELECT add_retention_policy('metrics.metrics_1hour',
    INTERVAL '365 days',
    if_not_exists => TRUE
);

-- 1-day aggregates: keep for 3 years
SELECT add_retention_policy('metrics.metrics_1day',
    INTERVAL '1095 days',
    if_not_exists => TRUE
);

-- ============================================================================
-- COMPRESSION POLICIES
-- ============================================================================

-- Enable compression on raw metrics after 1 day
ALTER TABLE metrics.raw_metrics SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'metric_name, service_name, host_name'
);

SELECT add_compression_policy('metrics.raw_metrics',
    INTERVAL '1 day',
    if_not_exists => TRUE
);

-- ============================================================================
-- ANOMALY DETECTION SUPPORT TABLES
-- ============================================================================

-- Store anomaly scores from ML models
CREATE TABLE IF NOT EXISTS metrics.anomaly_scores (
    time            TIMESTAMPTZ         NOT NULL,
    metric_name     TEXT                NOT NULL,
    service_name    TEXT,
    host_name       TEXT,
    anomaly_score   DOUBLE PRECISION    NOT NULL,  -- 0-100
    is_anomaly      BOOLEAN             NOT NULL DEFAULT FALSE,
    model_name      TEXT,
    model_version   TEXT,
    details         JSONB               DEFAULT '{}'
);

SELECT create_hypertable(
    'metrics.anomaly_scores',
    'time',
    chunk_time_interval => INTERVAL '1 day',
    if_not_exists => TRUE
);

CREATE INDEX IF NOT EXISTS idx_anomaly_metric_time ON metrics.anomaly_scores (metric_name, time DESC);
CREATE INDEX IF NOT EXISTS idx_anomaly_is_anomaly ON metrics.anomaly_scores (is_anomaly, time DESC) WHERE is_anomaly = TRUE;

-- ============================================================================
-- FORECASTS TABLE
-- ============================================================================

-- Store forecasted values from ML models
CREATE TABLE IF NOT EXISTS metrics.forecasts (
    created_at      TIMESTAMPTZ         NOT NULL DEFAULT NOW(),
    forecast_time   TIMESTAMPTZ         NOT NULL,  -- The time being forecasted
    metric_name     TEXT                NOT NULL,
    service_name    TEXT,
    host_name       TEXT,
    predicted_value DOUBLE PRECISION    NOT NULL,
    lower_bound     DOUBLE PRECISION,              -- Confidence interval lower
    upper_bound     DOUBLE PRECISION,              -- Confidence interval upper
    confidence      DOUBLE PRECISION,              -- Confidence level (e.g., 0.95)
    model_name      TEXT,
    model_version   TEXT
);

SELECT create_hypertable(
    'metrics.forecasts',
    'forecast_time',
    chunk_time_interval => INTERVAL '1 day',
    if_not_exists => TRUE
);

CREATE INDEX IF NOT EXISTS idx_forecast_metric_time ON metrics.forecasts (metric_name, forecast_time DESC);

-- ============================================================================
-- HELPER FUNCTIONS
-- ============================================================================

-- Function to get metric statistics for a time range
CREATE OR REPLACE FUNCTION metrics.get_metric_stats(
    p_metric_name TEXT,
    p_start_time TIMESTAMPTZ,
    p_end_time TIMESTAMPTZ,
    p_service_name TEXT DEFAULT NULL,
    p_host_name TEXT DEFAULT NULL
)
RETURNS TABLE (
    metric_name TEXT,
    sample_count BIGINT,
    avg_value DOUBLE PRECISION,
    min_value DOUBLE PRECISION,
    max_value DOUBLE PRECISION,
    stddev_value DOUBLE PRECISION
)
LANGUAGE SQL STABLE AS $$
    SELECT
        metric_name,
        COUNT(*)::BIGINT as sample_count,
        AVG(value) as avg_value,
        MIN(value) as min_value,
        MAX(value) as max_value,
        STDDEV(value) as stddev_value
    FROM metrics.raw_metrics
    WHERE metric_name = p_metric_name
      AND time >= p_start_time
      AND time < p_end_time
      AND (p_service_name IS NULL OR service_name = p_service_name)
      AND (p_host_name IS NULL OR host_name = p_host_name)
    GROUP BY metric_name;
$$;

-- Function to detect if current value is anomalous based on historical stats
CREATE OR REPLACE FUNCTION metrics.is_anomalous(
    p_metric_name TEXT,
    p_current_value DOUBLE PRECISION,
    p_lookback_hours INTEGER DEFAULT 24,
    p_zscore_threshold DOUBLE PRECISION DEFAULT 3.0
)
RETURNS BOOLEAN
LANGUAGE SQL STABLE AS $$
    SELECT CASE
        WHEN stats.stddev_value = 0 OR stats.stddev_value IS NULL THEN FALSE
        ELSE ABS(p_current_value - stats.avg_value) / stats.stddev_value > p_zscore_threshold
    END
    FROM (
        SELECT AVG(value) as avg_value, STDDEV(value) as stddev_value
        FROM metrics.raw_metrics
        WHERE metric_name = p_metric_name
          AND time >= NOW() - (p_lookback_hours || ' hours')::INTERVAL
    ) stats;
$$;

-- ============================================================================
-- VIEWS FOR GRAFANA
-- ============================================================================

-- Latest value for each metric (for gauges)
CREATE OR REPLACE VIEW metrics.latest_metrics AS
SELECT DISTINCT ON (metric_name, service_name, host_name)
    time,
    metric_name,
    value,
    service_name,
    host_name,
    labels
FROM metrics.raw_metrics
ORDER BY metric_name, service_name, host_name, time DESC;

-- Grant permissions
GRANT USAGE ON SCHEMA metrics TO monitoring;
GRANT SELECT, INSERT ON ALL TABLES IN SCHEMA metrics TO monitoring;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA metrics TO monitoring;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA metrics TO monitoring;

-- Log completion
DO $$
BEGIN
    RAISE NOTICE 'TimescaleDB initialization completed successfully!';
END $$;
