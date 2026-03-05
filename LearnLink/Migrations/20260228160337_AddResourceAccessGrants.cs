using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearnLink.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceAccessGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AccessExpiresAt",
                table: "Resources",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ResourceAccessGrants",
                columns: table => new
                {
                    GrantId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceAccessGrants", x => x.GrantId);
                    table.ForeignKey(
                        name: "FK_ResourceAccessGrants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResourceAccessGrants_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "ResourceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAccessGrants_ResourceId_UserId",
                table: "ResourceAccessGrants",
                columns: new[] { "ResourceId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceAccessGrants_UserId",
                table: "ResourceAccessGrants",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceAccessGrants");

            migrationBuilder.DropColumn(
                name: "AccessExpiresAt",
                table: "Resources");
        }
    }
}
