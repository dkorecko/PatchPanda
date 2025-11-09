using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchPanda.Web.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VersionNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Prerelease = table.Column<bool>(type: "INTEGER", nullable: false),
                    Breaking = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Notified = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ignored = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MultiContainerApps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiContainerApps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StackName = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigFile = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Containers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentSha = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubRepo = table.Column<string>(type: "TEXT", nullable: true),
                    OverrideGitHubRepo = table.Column<string>(type: "TEXT", nullable: true),
                    SecondaryGitHubRepos = table.Column<string>(type: "TEXT", nullable: true),
                    Uptime = table.Column<string>(type: "TEXT", nullable: false),
                    TargetImage = table.Column<string>(type: "TEXT", nullable: false),
                    Regex = table.Column<string>(type: "TEXT", nullable: true),
                    GitHubVersionRegex = table.Column<string>(type: "TEXT", nullable: true),
                    MultiContainerAppId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsSecondary = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastVersionCheck = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StackId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Containers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Containers_MultiContainerApps_MultiContainerAppId",
                        column: x => x.MultiContainerAppId,
                        principalTable: "MultiContainerApps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Containers_Stacks_StackId",
                        column: x => x.StackId,
                        principalTable: "Stacks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppVersionContainer",
                columns: table => new
                {
                    ApplicationsId = table.Column<int>(type: "INTEGER", nullable: false),
                    NewerVersionsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppVersionContainer", x => new { x.ApplicationsId, x.NewerVersionsId });
                    table.ForeignKey(
                        name: "FK_AppVersionContainer_AppVersions_NewerVersionsId",
                        column: x => x.NewerVersionsId,
                        principalTable: "AppVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppVersionContainer_Containers_ApplicationsId",
                        column: x => x.ApplicationsId,
                        principalTable: "Containers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppVersionContainer_NewerVersionsId",
                table: "AppVersionContainer",
                column: "NewerVersionsId");

            migrationBuilder.CreateIndex(
                name: "IX_Containers_MultiContainerAppId",
                table: "Containers",
                column: "MultiContainerAppId");

            migrationBuilder.CreateIndex(
                name: "IX_Containers_StackId",
                table: "Containers",
                column: "StackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppVersionContainer");

            migrationBuilder.DropTable(
                name: "AppVersions");

            migrationBuilder.DropTable(
                name: "Containers");

            migrationBuilder.DropTable(
                name: "MultiContainerApps");

            migrationBuilder.DropTable(
                name: "Stacks");
        }
    }
}
