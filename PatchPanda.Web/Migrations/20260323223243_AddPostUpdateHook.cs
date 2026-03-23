using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchPanda.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPostUpdateHook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PostUpdateHook",
                table: "Containers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PostUpdateHook",
                table: "Containers");
        }
    }
}
