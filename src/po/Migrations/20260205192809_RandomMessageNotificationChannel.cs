using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po.Migrations
{
    /// <inheritdoc />
    public partial class RandomMessageNotificationChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChannelId",
                table: "RandomMessages");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ChannelId",
                table: "RandomMessages",
                type: "decimal(20,0)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
