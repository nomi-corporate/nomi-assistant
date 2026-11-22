using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nomi.Assistant.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioTranscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assistant");

            migrationBuilder.CreateTable(
                name: "audio_uploads",
                schema: "assistant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_uploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audio_transcriptions",
                schema: "assistant",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudioId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_transcriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audio_transcriptions_audio_uploads_AudioId",
                        column: x => x.AudioId,
                        principalSchema: "assistant",
                        principalTable: "audio_uploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audio_transcriptions_AudioId",
                schema: "assistant",
                table: "audio_transcriptions",
                column: "AudioId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audio_transcriptions_ExpiresAt",
                schema: "assistant",
                table: "audio_transcriptions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_audio_transcriptions_OwnerUserId_IdempotencyKey",
                schema: "assistant",
                table: "audio_transcriptions",
                columns: new[] { "OwnerUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audio_uploads_OwnerUserId_Id",
                schema: "assistant",
                table: "audio_uploads",
                columns: new[] { "OwnerUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_audio_uploads_UploadExpiresAt",
                schema: "assistant",
                table: "audio_uploads",
                column: "UploadExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audio_transcriptions",
                schema: "assistant");

            migrationBuilder.DropTable(
                name: "audio_uploads",
                schema: "assistant");
        }
    }
}
