using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddInvoicing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "contractor_invoices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                ContractorId = table.Column<Guid>(type: "uuid", nullable: false),
                Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SubmittedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SubmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                HrApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                HrApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                AdminApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                AdminApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                VendorReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                PaidAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                FailureReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                LastAttemptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_contractor_invoices", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "contractors",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                Country = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                PayoutCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_contractors", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "invoice_lines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                ContractorInvoiceId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invoice_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_invoice_lines_contractor_invoices_ContractorInvoiceId",
                    column: x => x.ContractorInvoiceId,
                    principalTable: "contractor_invoices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_contractor_invoices_OrgId_CreatedAtUtc_Id",
            table: "contractor_invoices",
            columns: new[] { "OrgId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_contractors_OrgId_CreatedAtUtc_Id",
            table: "contractors",
            columns: new[] { "OrgId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_invoice_lines_ContractorInvoiceId",
            table: "invoice_lines",
            column: "ContractorInvoiceId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "contractors");

        migrationBuilder.DropTable(
            name: "invoice_lines");

        migrationBuilder.DropTable(
            name: "contractor_invoices");
    }
}
