using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileMatrix_Pabiran_.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicAccessLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[WorkplaceInvitations]') AND name = 'Email')
                OR (SELECT max_length FROM sys.columns WHERE object_id = OBJECT_ID(N'[WorkplaceInvitations]') AND name = 'Email') != 510 -- 255 chars * 2 bytes
                BEGIN
                    ALTER TABLE [WorkplaceInvitations] ALTER COLUMN [Email] nvarchar(255) NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[WorkplaceInvitations]') AND name = 'UsageCount')
                BEGIN
                    ALTER TABLE [WorkplaceInvitations] ADD [UsageCount] int NOT NULL DEFAULT 0;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[WorkplaceInvitations]') AND name = 'UsageLimit')
                BEGIN
                    ALTER TABLE [WorkplaceInvitations] ADD [UsageLimit] int NULL;
                END

                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Documents]') AND name = 'PublicAccessLevel')
                BEGIN
                    ALTER TABLE [Documents] ADD [PublicAccessLevel] nvarchar(max) NOT NULL DEFAULT 'Viewer';
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UsageCount",
                table: "WorkplaceInvitations");

            migrationBuilder.DropColumn(
                name: "UsageLimit",
                table: "WorkplaceInvitations");

            migrationBuilder.DropColumn(
                name: "PublicAccessLevel",
                table: "Documents");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "WorkplaceInvitations",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);
        }
    }
}
