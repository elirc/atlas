using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddWorkforce : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_entries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                SubjectType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                DetailsJson = table.Column<string>(type: "text", nullable: false),
                AtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_entries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "employment_records",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                JobTitle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                AnnualSalary = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                SalaryCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                WeeklyHours = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_employment_records", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "workers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ContactEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                Country = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                HiredOn = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workers", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_entries_OrgId_AtUtc_Id",
            table: "audit_entries",
            columns: new[] { "OrgId", "AtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_entries_SubjectType_SubjectId",
            table: "audit_entries",
            columns: new[] { "SubjectType", "SubjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_employment_records_WorkerId_EffectiveFrom",
            table: "employment_records",
            columns: new[] { "WorkerId", "EffectiveFrom" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_workers_OrgId_CreatedAtUtc_Id",
            table: "workers",
            columns: new[] { "OrgId", "CreatedAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_entries");

        migrationBuilder.DropTable(
            name: "employment_records");

        migrationBuilder.DropTable(
            name: "workers");
    }
}
