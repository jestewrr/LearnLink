using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LearnLink.Migrations
{
    /// <inheritdoc />
    public partial class EnterpriseBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArchivedResources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalResourceId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FileSize = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OwnerId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DateArchived = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecoveryStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchivedResources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchivedResources_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BackupPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FrequencyDays = table.Column<int>(type: "int", nullable: false),
                    RetentionCount = table.Column<int>(type: "int", nullable: false),
                    StorageDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NotifyOnBackup = table.Column<bool>(type: "bit", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUpdatedByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupPolicies_AspNetUsers_LastUpdatedByUserId",
                        column: x => x.LastUpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BackupType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SizeDescription = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StorageLocation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TriggeredByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    TotalSizeMb = table.Column<double>(type: "float", nullable: false),
                    ArchiveFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProgressPercent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupRecords_AspNetUsers_TriggeredByUserId",
                        column: x => x.TriggeredByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BackupItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BackupRecordId = table.Column<int>(type: "int", nullable: false),
                    RepositoryName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    StorageSizeMb = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupItems_BackupRecords_BackupRecordId",
                        column: x => x.BackupRecordId,
                        principalTable: "BackupRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RestoreOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BackupRecordId = table.Column<int>(type: "int", nullable: false),
                    RestoreType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RestoreDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RestoredByUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreOperations_AspNetUsers_RestoredByUserId",
                        column: x => x.RestoredByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RestoreOperations_BackupRecords_BackupRecordId",
                        column: x => x.BackupRecordId,
                        principalTable: "BackupRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedResources_OwnerId",
                table: "ArchivedResources",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupItems_BackupRecordId",
                table: "BackupItems",
                column: "BackupRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupPolicies_LastUpdatedByUserId",
                table: "BackupPolicies",
                column: "LastUpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_TriggeredByUserId",
                table: "BackupRecords",
                column: "TriggeredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreOperations_BackupRecordId",
                table: "RestoreOperations",
                column: "BackupRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreOperations_RestoredByUserId",
                table: "RestoreOperations",
                column: "RestoredByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchivedResources");

            migrationBuilder.DropTable(
                name: "BackupItems");

            migrationBuilder.DropTable(
                name: "BackupPolicies");

            migrationBuilder.DropTable(
                name: "RestoreOperations");

            migrationBuilder.DropTable(
                name: "BackupRecords");
        }
    }
}
