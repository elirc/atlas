using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddEngagements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "engagements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CandidateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CandidateEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                Country = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                RoleTitle = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                AnnualSalary = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                SalaryCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                WeeklyHours = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                CreatedWorkerId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                OfferSentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                AcceptedOn = table.Column<DateOnly>(type: "date", nullable: true),
                OnboardingStartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ActivatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_engagements", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "engagement_checklist_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                EngagementId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_engagement_checklist_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_engagement_checklist_items_engagements_EngagementId",
                    column: x => x.EngagementId,
                    principalTable: "engagements",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_engagement_checklist_items_EngagementId",
            table: "engagement_checklist_items",
            column: "EngagementId");

        migrationBuilder.CreateIndex(
            name: "IX_engagements_OrgId_CreatedAtUtc_Id",
            table: "engagements",
            columns: new[] { "OrgId", "CreatedAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "engagement_checklist_items");

        migrationBuilder.DropTable(
            name: "engagements");
    }
}
