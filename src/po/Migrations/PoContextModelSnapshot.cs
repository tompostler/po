﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using po.DataAccess;

#nullable disable

namespace po.Migrations
{
    [DbContext(typeof(PoContext))]
    partial class PoContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "6.0.2")
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder, 1L, 1);

            modelBuilder.Entity("po.Models.SlashCommand", b =>
                {
                    b.Property<string>("Name")
                        .HasMaxLength(32)
                        .HasColumnType("nvarchar(32)");

                    b.Property<decimal>("Id")
                        .HasColumnType("decimal(20,0)");

                    b.Property<bool>("IsGuildLevel")
                        .HasColumnType("bit");

                    b.Property<bool>("RequiresChannelEnablement")
                        .HasColumnType("bit");

                    b.Property<DateTimeOffset?>("SuccessfullyRegistered")
                        .HasColumnType("datetimeoffset");

                    b.Property<int>("Version")
                        .HasColumnType("int");

                    b.HasKey("Name");

                    b.ToTable("SlashCommands");
                });

            modelBuilder.Entity("po.Models.SlashCommandChannel", b =>
                {
                    b.Property<string>("SlashCommandName")
                        .HasColumnType("nvarchar(32)");

                    b.Property<decimal>("GuildId")
                        .HasColumnType("decimal(20,0)");

                    b.Property<decimal>("ChannelId")
                        .HasColumnType("decimal(20,0)");

                    b.Property<DateTimeOffset>("RegistrationDate")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("datetimeoffset")
                        .HasDefaultValueSql("SYSDATETIMEOFFSET()");

                    b.HasKey("SlashCommandName", "GuildId", "ChannelId");

                    b.ToTable("SlashCommandChannels");
                });

            modelBuilder.Entity("po.Models.SlashCommandChannel", b =>
                {
                    b.HasOne("po.Models.SlashCommand", null)
                        .WithMany("EnabledChannels")
                        .HasForeignKey("SlashCommandName")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("po.Models.SlashCommand", b =>
                {
                    b.Navigation("EnabledChannels");
                });
#pragma warning restore 612, 618
        }
    }
}
