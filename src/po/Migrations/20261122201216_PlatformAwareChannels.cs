using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po.Migrations
{
    /// <inheritdoc />
    public partial class PlatformAwareChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels");

            migrationBuilder.AlterColumn<string>(
                name: "ChannelId",
                table: "SlashCommandChannels",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(20,0)");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "SlashCommandChannels",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                // Every row before this migration is a Discord channel.
                defaultValue: "discord");

            migrationBuilder.AlterColumn<string>(
                name: "ChannelId",
                table: "ScheduledMessages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(20,0)");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "ScheduledMessages",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                // Every row before this migration is a Discord channel.
                defaultValue: "discord");

            migrationBuilder.AlterColumn<string>(
                name: "ChannelId",
                table: "ScheduledBlobs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(20,0)");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "ScheduledBlobs",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                // Every row before this migration is a Discord channel.
                defaultValue: "discord");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels",
                columns: new[] { "SlashCommandName", "Platform", "ChannelId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Other platforms' channel ids aren't numbers, so they can't survive the conversion back to decimal.
            migrationBuilder.Sql("DELETE FROM SlashCommandChannels WHERE Platform <> 'discord'");
            migrationBuilder.Sql("DELETE FROM ScheduledMessages WHERE Platform <> 'discord'");
            migrationBuilder.Sql("DELETE FROM ScheduledBlobs WHERE Platform <> 'discord'");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "SlashCommandChannels");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "ScheduledMessages");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "ScheduledBlobs");

            migrationBuilder.AlterColumn<decimal>(
                name: "ChannelId",
                table: "SlashCommandChannels",
                type: "decimal(20,0)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<decimal>(
                name: "ChannelId",
                table: "ScheduledMessages",
                type: "decimal(20,0)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<decimal>(
                name: "ChannelId",
                table: "ScheduledBlobs",
                type: "decimal(20,0)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AddPrimaryKey(
                name: "PK_SlashCommandChannels",
                table: "SlashCommandChannels",
                columns: new[] { "SlashCommandName", "ChannelId" });
        }
    }
}
