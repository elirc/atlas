using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddExpenses : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "expense_claims",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                ReceiptUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                ReceiptFilename = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DecisionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ReimbursedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_expense_claims", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_expense_claims_OrgId_CreatedAtUtc_Id",
            table: "expense_claims",
            columns: new[] { "OrgId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_expense_claims_OrgId_Status",
            table: "expense_claims",
            columns: new[] { "OrgId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "expense_claims");
    }
}
