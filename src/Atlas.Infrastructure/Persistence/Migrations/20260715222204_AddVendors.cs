using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddVendors : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SelectedVendor",
            table: "payroll_runs",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            // Backfill existing runs to AtlasNative — the only vendor that existed before S12, and the
            // correct historical answer for any cycle already computed.
            defaultValue: "AtlasNative");

        migrationBuilder.CreateTable(
            name: "vendor_assignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                Country = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                Vendor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_vendor_assignments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "vendor_comparison_reports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrgId = table.Column<Guid>(type: "uuid", nullable: false),
                RunId = table.Column<Guid>(type: "uuid", nullable: false),
                Country = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                PrimaryVendor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ShadowVendor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ToleranceMinorUnits = table.Column<int>(type: "integer", nullable: false),
                WorkerCount = table.Column<int>(type: "integer", nullable: false),
                DivergentCount = table.Column<int>(type: "integer", nullable: false),
                MaxAbsNetDeltaMinor = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_vendor_comparison_reports", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "vendor_comparison_lines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                WorkerId = table.Column<Guid>(type: "uuid", nullable: false),
                GrossDeltaMinor = table.Column<long>(type: "bigint", nullable: false),
                NetDeltaMinor = table.Column<long>(type: "bigint", nullable: false),
                EmployerCostDeltaMinor = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                Divergent = table.Column<bool>(type: "boolean", nullable: false),
                VendorComparisonReportId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_vendor_comparison_lines", x => x.Id);
                table.ForeignKey(
                    name: "FK_vendor_comparison_lines_vendor_comparison_reports_VendorCom~",
                    column: x => x.VendorComparisonReportId,
                    principalTable: "vendor_comparison_reports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_vendor_assignments_OrgId_Country_EffectiveFrom",
            table: "vendor_assignments",
            columns: new[] { "OrgId", "Country", "EffectiveFrom" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_vendor_comparison_lines_VendorComparisonReportId",
            table: "vendor_comparison_lines",
            column: "VendorComparisonReportId");

        migrationBuilder.CreateIndex(
            name: "IX_vendor_comparison_reports_OrgId_CreatedAtUtc_Id",
            table: "vendor_comparison_reports",
            columns: new[] { "OrgId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_vendor_comparison_reports_RunId_CreatedAtUtc",
            table: "vendor_comparison_reports",
            columns: new[] { "RunId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "vendor_assignments");

        migrationBuilder.DropTable(
            name: "vendor_comparison_lines");

        migrationBuilder.DropTable(
            name: "vendor_comparison_reports");

        migrationBuilder.DropColumn(
            name: "SelectedVendor",
            table: "payroll_runs");
    }
}
