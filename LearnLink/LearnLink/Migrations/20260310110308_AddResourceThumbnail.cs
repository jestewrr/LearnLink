using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearnLink.LearnLink.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceThumbnail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[Resources]')
                      AND name = 'ThumbnailUrl'
                )
                BEGIN
                    ALTER TABLE [Resources] ADD [ThumbnailUrl] nvarchar(500) NULL;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N'[Resources]')
                      AND name = 'ThumbnailUrl'
                )
                BEGIN
                    ALTER TABLE [Resources] DROP COLUMN [ThumbnailUrl];
                END
            ");
        }
    }
}
