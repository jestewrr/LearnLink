using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearnLink.LearnLink.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonCommentsAndDeletionFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "Resources");

            migrationBuilder.AddColumn<DateTime>(
                name: "PendingReviewPreviewedAt",
                table: "Resources",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountDeletionFeedbacks",
                columns: table => new
                {
                    FeedbackId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Feedback = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    UserEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountDeletionFeedbacks", x => x.FeedbackId);
                });

            migrationBuilder.CreateTable(
                name: "LessonComments",
                columns: table => new
                {
                    LessonCommentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LessonId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    DatePosted = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonComments", x => x.LessonCommentId);
                    table.ForeignKey(
                        name: "FK_LessonComments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LessonComments_LessonsLearned_LessonId",
                        column: x => x.LessonId,
                        principalTable: "LessonsLearned",
                        principalColumn: "LessonId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonComments_LessonId",
                table: "LessonComments",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonComments_UserId",
                table: "LessonComments",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountDeletionFeedbacks");

            migrationBuilder.DropTable(
                name: "LessonComments");

            migrationBuilder.DropColumn(
                name: "PendingReviewPreviewedAt",
                table: "Resources");

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "Resources",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
