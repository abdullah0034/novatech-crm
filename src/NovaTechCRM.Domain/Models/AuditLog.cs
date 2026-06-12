namespace NovaTechCRM.Domain.Models;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    Viewed,       // for sensitive records
    Exported,
    StatusChanged,
    LoginSuccess,
    LoginFailed,
    PasswordChanged,
    PermissionGranted,
    PermissionRevoked,
    ApiKeyCreated,
    ApiKeyRevoked,
    BulkOperation
}

public class AuditLog
{
        public long Id { get; set; }  // long for high-volume tables

        public AuditAction Action { get; set; }

        // what was changed
        public string EntityType { get; set; } = string.Empty;  // e.g. "Invoice", "Customer"
        public string EntityId { get; set; } = string.Empty;

        // who did it
        public string? UserId { get; set; }
        public string? UserEmail { get; set; }
        public string? UserRole { get; set; }

        // context
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? RequestId { get; set; }
        public string? SessionId { get; set; }

        // what changed — stored as JSON diffs
        public string? OldValuesJson { get; set; }
        public string? NewValuesJson { get; set; }

        // human readable summary
        public string? Description { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

        // partition key for time-based sharding
        public int PartitionMonth { get; set; }

        // additional metadata
        public string? MetadataJson { get; set; }
    }

    // For bulk insert — we batch audit logs before writing
    public class AuditLogBatch
    {
        public List<AuditLog> Entries { get; set; } = new();
        public DateTime BatchedAt { get; set; } = DateTime.UtcNow;
        public string? CorrelationId { get; set; }
    }

    // used when querying audit history for a specific entity
    public class EntityAuditHistory
    {
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public List<AuditLog> History { get; set; } = new();
        public int TotalCount { get; set; }
    }
