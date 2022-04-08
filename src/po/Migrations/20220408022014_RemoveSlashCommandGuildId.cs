using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po.Migrations
{
    public partial class RemoveSlashCommandGuildId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels");

            migrationBuilder.DropColumn(
                name: "GuildId",
                table: "SlashCommandChannels");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels",
                columns: new[] { "SlashCommandName", "ChannelId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels");

            migrationBuilder.AddColumn<decimal>(
                name: "GuildId",
                table: "SlashCommandChannels",
                type: "decimal(20,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels",
                columns: new[] { "SlashCommandName", "GuildId", "ChannelId" });
        }
    }
}
