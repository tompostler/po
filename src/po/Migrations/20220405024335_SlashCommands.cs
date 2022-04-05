using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace po.Migrations
{
    public partial class SlashCommands : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlashCommands",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsGuildLevel = table.Column<bool>(type: "bit", nullable: false),
                    SuccessfullyRegistered = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequiresChannelEnablement = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlashCommands", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "SlashCommandChannels",
                columns: table => new
                {
                    SlashCommandName = table.Column<string>(type: "nvarchar(32)", nullable: false),
                    GuildId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    ChannelId = table.Column<decimal>(type: "decimal(20,0)", nullable: false),
                    RegistrationDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSDATETIMEOFFSET()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlashCommandChannels", x => new { x.SlashCommandName, x.GuildId, x.ChannelId });
                    table.ForeignKey(
                        name: "FK_SlashCommandChannels_SlashCommands_SlashCommandName",
                        column: x => x.SlashCommandName,
                        principalTable: "SlashCommands",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlashCommandChannels");

            migrationBuilder.DropTable(
                name: "SlashCommands");
        }
    }
}
