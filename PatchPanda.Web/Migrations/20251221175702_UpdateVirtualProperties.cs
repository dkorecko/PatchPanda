using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PatchPanda.Web.Migrations
{
    /// <inheritdoc />
    public partial class UpdateVirtualProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UpdateAttempts_ContainerId",
                table: "UpdateAttempts",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_UpdateAttempts_StackId",
                table: "UpdateAttempts",
                column: "StackId");

            migrationBuilder.AddForeignKey(
                name: "FK_UpdateAttempts_Containers_ContainerId",
                table: "UpdateAttempts",
                column: "ContainerId",
                principalTable: "Containers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UpdateAttempts_Stacks_StackId",
                table: "UpdateAttempts",
                column: "StackId",
                principalTable: "Stacks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UpdateAttempts_Containers_ContainerId",
                table: "UpdateAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_UpdateAttempts_Stacks_StackId",
                table: "UpdateAttempts");

            migrationBuilder.DropIndex(
                name: "IX_UpdateAttempts_ContainerId",
                table: "UpdateAttempts");

            migrationBuilder.DropIndex(
                name: "IX_UpdateAttempts_StackId",
                table: "UpdateAttempts");
        }
    }
}
