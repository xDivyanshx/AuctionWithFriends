using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionRoom.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerDetailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Assists",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BirthDate",
                table: "Players",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoalsScored",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Minutes",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "News",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NowCost",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PointsPerGame",
                table: "Players",
                type: "numeric(4,1)",
                precision: 4,
                scale: 1,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Starts",
                table: "Players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Players",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Assists",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "BirthDate",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "GoalsScored",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Minutes",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "News",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "NowCost",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "PointsPerGame",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Starts",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Players");
        }
    }
}
