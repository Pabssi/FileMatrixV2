using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileMatrix_Pabiran_.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Documents]') AND name = 'IsFavorite')
                BEGIN
                    ALTER TABLE [Documents] ADD [IsFavorite] bit NOT NULL DEFAULT 0;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Documents]') AND name = 'PublicShareToken')
                BEGIN
                    ALTER TABLE [Documents] ADD [PublicShareToken] nvarchar(max) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Documents]') AND name = 'Status')
                BEGIN
                    ALTER TABLE [Documents] ADD [Status] nvarchar(20) NOT NULL DEFAULT 'Published';
                END

                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[DocumentComments]') AND type in (N'U'))
                BEGIN
                    CREATE TABLE [DocumentComments] (
                        [CommentID] int NOT NULL IDENTITY,
                        [DocumentID] int NOT NULL,
                        [UserID] int NOT NULL,
                        [Text] nvarchar(max) NOT NULL,
                        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
                        CONSTRAINT [PK_DocumentComments] PRIMARY KEY ([CommentID]),
                        CONSTRAINT [FK_DocumentComments_Documents_DocumentID] FOREIGN KEY ([DocumentID]) REFERENCES [Documents] ([DocumentID]) ON DELETE CASCADE,
                        CONSTRAINT [FK_DocumentComments_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION
                    );

                    CREATE INDEX [IX_DocumentComments_DocumentID] ON [DocumentComments] ([DocumentID]);
                    CREATE INDEX [IX_DocumentComments_UserID] ON [DocumentComments] ([UserID]);
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentComments");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "PublicShareToken",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Documents");
        }
    }
}
