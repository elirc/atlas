using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddQuotes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "quotes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                Country = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                Gross_Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Gross_Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                TotalEmployerCost_Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                TotalEmployerCost_Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                DisplayTotal_Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                DisplayTotal_Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quotes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "quote_lines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Amount_Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Amount_Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                QuoteId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quote_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_quote_lines_quotes_QuoteId",
                    column: x => x.QuoteId,
                    principalTable: "quotes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_quote_lines_QuoteId",
            table: "quote_lines",
            column: "QuoteId");

        migrationBuilder.CreateIndex(
            name: "IX_quotes_OrgId_CreatedAtUtc_Id",
            table: "quotes",
            columns: new[] { "OrgId", "CreatedAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "quote_lines");

        migrationBuilder.DropTable(
            name: "quotes");
    }
}
