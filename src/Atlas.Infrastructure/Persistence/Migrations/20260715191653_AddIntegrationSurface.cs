using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddIntegrationSurface : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "api_keys",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                DisplayPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ScopeCsv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_api_keys", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "idempotency_records",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StatusCode = table.Column<int>(type: "integer", nullable: false),
                ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                ResponseBody = table.Column<byte[]>(type: "bytea", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_idempotency_records", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                SubjectType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DispatchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "webhook_deliveries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                ResponseStatusCode = table.Column<int>(type: "integer", nullable: true),
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_webhook_deliveries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "webhook_subscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                Secret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                EventFilter = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PausedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_webhook_subscriptions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_api_keys_KeyHash",
            table: "api_keys",
            column: "KeyHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_api_keys_OrgId",
            table: "api_keys",
            column: "OrgId");

        migrationBuilder.CreateIndex(
            name: "IX_idempotency_records_OrgId_Key",
            table: "idempotency_records",
            columns: new[] { "OrgId", "Key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_DispatchedAtUtc_NextAttemptAtUtc",
            table: "outbox_messages",
            columns: new[] { "DispatchedAtUtc", "NextAttemptAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_webhook_deliveries_OrgId_CreatedAtUtc_Id",
            table: "webhook_deliveries",
            columns: new[] { "OrgId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_webhook_deliveries_SubscriptionId",
            table: "webhook_deliveries",
            column: "SubscriptionId");

        migrationBuilder.CreateIndex(
            name: "IX_webhook_subscriptions_OrgId_IsActive",
            table: "webhook_subscriptions",
            columns: new[] { "OrgId", "IsActive" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "api_keys");

        migrationBuilder.DropTable(
            name: "idempotency_records");

        migrationBuilder.DropTable(
            name: "outbox_messages");

        migrationBuilder.DropTable(
            name: "webhook_deliveries");

        migrationBuilder.DropTable(
            name: "webhook_subscriptions");
    }
}
