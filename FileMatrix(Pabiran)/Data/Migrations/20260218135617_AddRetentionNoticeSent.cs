using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileMatrix_Pabiran_.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionNoticeSent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Documents]') AND name = 'ArchivedAt')
                BEGIN
                    ALTER TABLE [Documents] ADD [ArchivedAt] datetime2 NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Documents]') AND name = 'RetentionNoticeSent')
                BEGIN
                    ALTER TABLE [Documents] ADD [RetentionNoticeSent] bit NOT NULL DEFAULT 0;
                END

                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[RetentionPolicies]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE [RetentionPolicies] (
                        [ID] int NOT NULL IDENTITY,
                        [WorkplaceID] int NOT NULL,
                        [AutoArchiveAfterDays] int NULL,
                        [AutoDeleteAfterDays] int NULL,
                        [IsEnabled] bit NOT NULL,
                        [UpdatedAt] datetime2 NOT NULL,
                        CONSTRAINT [PK_RetentionPolicies] PRIMARY KEY ([ID])
                    );
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetentionPolicies");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "RetentionNoticeSent",
                table: "Documents");
        }
    }
}
