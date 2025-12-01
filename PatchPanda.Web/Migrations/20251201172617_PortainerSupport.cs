using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchPanda.Web.Migrations
{
    /// <inheritdoc />
    public partial class PortainerSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ConfigFile",
                table: "Stacks",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<bool>(
                name: "PortainerManaged",
                table: "Stacks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PortainerManaged",
                table: "Stacks");

            migrationBuilder.AlterColumn<string>(
                name: "ConfigFile",
                table: "Stacks",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
