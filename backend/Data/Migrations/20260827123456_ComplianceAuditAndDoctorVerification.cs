using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HelloDoctor.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ComplianceAuditAndDoctorVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Role",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "IsAdministrator",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MedicalLicenseNumber",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Verification",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<string>(
                name: "VerificationNote",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedBy",
                table: "Users",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            // Bu migration'dan önce kayıtlı hekimler doğrulanmamış sayılır.
            // Güvenli taraf bu: doğrulanmamış hesabın hasta karşısına hekim
            // olarak çıkmaması, mevcut hesapların çalışmaya devam etmesinden
            // daha önemli. Yönetici tek tek onaylayacak.
            migrationBuilder.Sql(
                "UPDATE \"Users\" SET \"Verification\" = 'Pending' WHERE \"Role\" = 'Doctor';");

            migrationBuilder.CreateTable(
                name: "AccessLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActorId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsentRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    DocumentKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DocumentVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Granted = table.Column<bool>(type: "boolean", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role_Verification",
                table: "Users",
                columns: new[] { "Role", "Verification" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_At",
                table: "AccessLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_SubjectId_At",
                table: "AccessLogs",
                columns: new[] { "SubjectId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_UserId_DocumentKey_At",
                table: "ConsentRecords",
                columns: new[] { "UserId", "DocumentKey", "At" });

            // Bu migration'dan önce kayıtlı hekimler doğrulanmamış sayılır.
            // Güvenli taraf bu: doğrulanmamış bir hesabın hasta karşısına hekim
            // olarak çıkmaması, mevcut hesapların kesintisiz devam etmesinden
            // önemli. Yönetici tek tek onaylayacak.
            migrationBuilder.Sql(
                @"UPDATE ""Users"" SET ""Verification"" = 'Pending' WHERE ""Role"" = 'Doctor';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessLogs");

            migrationBuilder.DropTable(
                name: "ConsentRecords");

            migrationBuilder.DropIndex(
                name: "IX_Users_Role_Verification",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsAdministrator",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MedicalLicenseNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Verification",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerificationNote",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "VerifiedBy",
                table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Role",
                table: "Users",
                column: "Role");
        }
    }
}
