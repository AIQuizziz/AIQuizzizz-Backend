using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanArchitectureBase.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AvatarMediaId",
                table: "DomainUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DomainUsers_AvatarMediaId",
                table: "DomainUsers",
                column: "AvatarMediaId");

            migrationBuilder.AddForeignKey(
                name: "FK_DomainUsers_Media_AvatarMediaId",
                table: "DomainUsers",
                column: "AvatarMediaId",
                principalTable: "Media",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DomainUsers_Media_AvatarMediaId",
                table: "DomainUsers");

            migrationBuilder.DropIndex(
                name: "IX_DomainUsers_AvatarMediaId",
                table: "DomainUsers");

            migrationBuilder.DropColumn(
                name: "AvatarMediaId",
                table: "DomainUsers");
        }
    }
}
