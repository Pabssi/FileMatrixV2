using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileMatrix_Pabiran_.Migrations
{
    /// <inheritdoc />
    public partial class AddInvitationDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoleID",
                table: "WorkplaceInvitations",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTime>(
                name: "UsedAt",
                table: "WorkplaceInvitations",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoleID",
                table: "WorkplaceInvitations");

            migrationBuilder.DropColumn(
                name: "UsedAt",
                table: "WorkplaceInvitations");
        }
    }
}
