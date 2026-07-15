using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddPayroll : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "pay_cycles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                Country = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                Frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pay_cycles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payroll_runs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                PayCycleId = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExecutedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payroll_runs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "payslips",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RunId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                gross_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                gross_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                net_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                net_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                employer_cost_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                employer_cost_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payslips", x => x.Id);
                table.ForeignKey(
                    name: "FK_payslips_payroll_runs_RunId",
                    column: x => x.RunId,
                    principalTable: "payroll_runs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "payslip_lines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                PayslipId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_payslip_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_payslip_lines_payslips_PayslipId",
                    column: x => x.PayslipId,
                    principalTable: "payslips",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_pay_cycles_OrgId_Country_Frequency",
            table: "pay_cycles",
            columns: new[] { "OrgId", "Country", "Frequency" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payroll_runs_OrgId_CreatedAtUtc_Id",
            table: "payroll_runs",
            columns: new[] { "OrgId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_payroll_runs_PayCycleId_PeriodStart",
            table: "payroll_runs",
            columns: new[] { "PayCycleId", "PeriodStart" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_payslip_lines_PayslipId",
            table: "payslip_lines",
            column: "PayslipId");

        migrationBuilder.CreateIndex(
            name: "IX_payslips_RunId",
            table: "payslips",
            column: "RunId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "pay_cycles");

        migrationBuilder.DropTable(
            name: "payslip_lines");

        migrationBuilder.DropTable(
            name: "payslips");

        migrationBuilder.DropTable(
            name: "payroll_runs");
    }
}
