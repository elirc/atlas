using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "client_orgs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_client_orgs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "countries",
            columns: table => new
            {
                Code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_countries", x => x.Code);
            });

        migrationBuilder.CreateIndex(
            name: "IX_client_orgs_CreatedAtUtc_Id",
            table: "client_orgs",
            columns: new[] { "CreatedAtUtc", "Id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "client_orgs");

        migrationBuilder.DropTable(
            name: "countries");
    }
}
