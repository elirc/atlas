using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCompliance : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "worker_documents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Reference = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_worker_documents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_worker_documents_OrgId_ExpiresOn",
            table: "worker_documents",
            columns: new[] { "OrgId", "ExpiresOn" });

        migrationBuilder.CreateIndex(
            name: "IX_worker_documents_WorkerId_OrgId",
            table: "worker_documents",
            columns: new[] { "WorkerId", "OrgId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "worker_documents");
    }
}
