using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearnLink.LearnLink.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionMetadataTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ResourceVersions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GradeLevel",
                table: "ResourceVersions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Quarter",
                table: "ResourceVersions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceType",
                table: "ResourceVersions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "ResourceVersions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ResourceVersions",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "ResourceVersions");

            migrationBuilder.DropColumn(
                name: "GradeLevel",
                table: "ResourceVersions");

            migrationBuilder.DropColumn(
                name: "Quarter",
                table: "ResourceVersions");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                table: "ResourceVersions");

            migrationBuilder.DropColumn(
                name: "Subject",
                table: "ResourceVersions");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "ResourceVersions");
        }
    }
}
