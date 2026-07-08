#!/bin/bash
# Script to create Kafka topics for the log monitoring system
# Can be run standalone or as part of Docker initialization

set -e

KAFKA_BOOTSTRAP=${KAFKA_BOOTSTRAP:-"localhost:9093"}
REPLICATION_FACTOR=${REPLICATION_FACTOR:-1}

echo "Creating Kafka topics on ${KAFKA_BOOTSTRAP}..."

# Function to create topic with error handling
create_topic() {
    local topic=$1
    local partitions=$2
    local retention_ms=$3
    local extra_config=${4:-""}
    
    echo "Creating topic: ${topic} (partitions: ${partitions}, retention: ${retention_ms}ms)"
    
    kafka-topics.sh --create --if-not-exists \
        --bootstrap-server "${KAFKA_BOOTSTRAP}" \
        --topic "${topic}" \
        --partitions "${partitions}" \
        --replication-factor "${REPLICATION_FACTOR}" \
        --config retention.ms="${retention_ms}" \
        --config compression.type=snappy \
        --config cleanup.policy=delete \
        ${extra_config} || echo "Topic ${topic} may already exist"
}

# Telemetry topics
create_topic "telemetry-metrics" 12 604800000   # 7 days retention
create_topic "telemetry-logs" 12 604800000      # 7 days retention
create_topic "telemetry-traces" 6 259200000     # 3 days retention

# Processing topics
create_topic "alerts" 3 604800000               # 7 days retention
create_topic "enriched-logs" 12 604800000       # 7 days retention
create_topic "anomalies" 3 1209600000           # 14 days retention

# Dead letter queue
create_topic "dlq" 3 1209600000                 # 14 days retention

echo ""
echo "Listing all topics:"
kafka-topics.sh --list --bootstrap-server "${KAFKA_BOOTSTRAP}"

echo ""
echo "Topic creation completed successfully!"
