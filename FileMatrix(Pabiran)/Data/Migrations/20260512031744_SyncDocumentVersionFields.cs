using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileMatrix_Pabiran_.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncDocumentVersionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IntegrationApiKey",
                table: "Workplaces",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleBackupFolderID",
                table: "Workplaces",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleDriveAccessToken",
                table: "Workplaces",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleDriveRefreshToken",
                table: "Workplaces",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GoogleDriveTokenExpiry",
                table: "Workplaces",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalPublicID",
                table: "DocumentVersions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RestoredFromVersionID",
                table: "DocumentVersions",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "WorkplaceID",
                table: "AuditLogs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "SystemInfrastructureTasks",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastRun = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastResult = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemInfrastructureTasks", x => x.ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_RestoredFromVersionID",
                table: "DocumentVersions",
                column: "RestoredFromVersionID");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentVersions_DocumentVersions_RestoredFromVersionID",
                table: "DocumentVersions",
                column: "RestoredFromVersionID",
                principalTable: "DocumentVersions",
                principalColumn: "VersionID");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentVersions_DocumentVersions_RestoredFromVersionID",
                table: "DocumentVersions");

            migrationBuilder.DropTable(
                name: "SystemInfrastructureTasks");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_RestoredFromVersionID",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "GoogleBackupFolderID",
                table: "Workplaces");

            migrationBuilder.DropColumn(
                name: "GoogleDriveAccessToken",
                table: "Workplaces");

            migrationBuilder.DropColumn(
                name: "GoogleDriveRefreshToken",
                table: "Workplaces");

            migrationBuilder.DropColumn(
                name: "GoogleDriveTokenExpiry",
                table: "Workplaces");

            migrationBuilder.DropColumn(
                name: "ExternalPublicID",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "RestoredFromVersionID",
                table: "DocumentVersions");

            migrationBuilder.AlterColumn<string>(
                name: "IntegrationApiKey",
                table: "Workplaces",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "WorkplaceID",
                table: "AuditLogs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
