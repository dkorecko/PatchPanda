using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchPanda.Web.Migrations
{
    /// <inheritdoc />
    public partial class ErrorHandling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UpdateAttempts",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContainerId = table.Column<int>(type: "INTEGER", nullable: false),
                    StackId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StdOut = table.Column<string>(type: "TEXT", nullable: true),
                    StdErr = table.Column<string>(type: "TEXT", nullable: true),
                    FailedCommand = table.Column<string>(type: "TEXT", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    UsedPlan = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpdateAttempts", x => x.Id);
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UpdateAttempts");
        }
    }
}
