using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace url_shortening_service.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiresAtToShortUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "ShortUrls",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ShortUrls");
        }
    }
}
