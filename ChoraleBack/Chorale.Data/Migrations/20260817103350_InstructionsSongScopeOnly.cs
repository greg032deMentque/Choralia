using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoraleBackEnd.Data.Migrations
{
    /// <summary>
    /// Reduit la consigne a une cible unique : le chant. Les portees chorale, pupitre et
    /// evenement sont supprimees du modele (decision produit, Spec/chorale/10-decisions.md).
    /// </summary>
    /// <remarks>
    /// MIGRATION AVEC PERTE DE DONNEES, ASSUMEE. Toute consigne qui n'etait pas rattachee a un
    /// chant (portee chorale, pupitre ou evenement) est SUPPRIMEE par le DELETE en tete de
    /// <see cref="Up"/>. Ce DELETE n'est pas optionnel : sans lui, l'ALTER COLUMN qui rend
    /// `SongId` obligatoire echoue sur toute base contenant de telles lignes — et le
    /// `defaultValue` que l'echafaudage EF propose a leur place les remplirait d'un GUID vide
    /// violant la cle etrangere.
    ///
    /// <see cref="Down"/> recree le schema mais PAS les lignes supprimees : le retour arriere
    /// est structurel, jamais integral.
    /// </remarks>
    public partial class InstructionsSongScopeOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AVANT toute modification de colonne : voir les remarques de la classe.
            migrationBuilder.Sql("DELETE FROM [Instructions] WHERE [SongId] IS NULL");

            migrationBuilder.DropForeignKey(
                name: "FK_Instructions_Choirs_ChoirId",
                table: "Instructions");

            migrationBuilder.DropForeignKey(
                name: "FK_Instructions_Events_EventId",
                table: "Instructions");

            migrationBuilder.DropIndex(
                name: "IX_Instructions_ChoirId_Scope",
                table: "Instructions");

            migrationBuilder.DropIndex(
                name: "IX_Instructions_EventId",
                table: "Instructions");

            migrationBuilder.DropIndex(
                name: "IX_Instructions_SongId",
                table: "Instructions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Instruction_Scope",
                table: "Instructions");

            migrationBuilder.DropColumn(
                name: "ChoirId",
                table: "Instructions");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "Instructions");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "Instructions");

            // Pas de defaultValue : les lignes sans chant ont ete supprimees ci-dessus, aucune
            // valeur de repli n'est donc necessaire — et un GUID vide violerait la FK.
            migrationBuilder.AlterColumn<Guid>(
                name: "SongId",
                table: "Instructions",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Instructions_SongId",
                table: "Instructions",
                column: "SongId",
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Instructions_SongId",
                table: "Instructions");

            migrationBuilder.AlterColumn<Guid>(
                name: "SongId",
                table: "Instructions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "ChoirId",
                table: "Instructions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "Instructions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "Instructions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Instructions_ChoirId_Scope",
                table: "Instructions",
                columns: new[] { "ChoirId", "Scope" },
                filter: "[ChoirId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Instructions_EventId",
                table: "Instructions",
                column: "EventId",
                filter: "[EventId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Instructions_SongId",
                table: "Instructions",
                column: "SongId",
                filter: "[SongId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Instruction_Scope",
                table: "Instructions",
                sql: "([Scope] = 0 AND [ChoirId] IS NOT NULL AND [VoicePart] IS NULL\r\n    AND [SongId] IS NULL AND [EventId] IS NULL)\r\nOR ([Scope] = 1 AND [ChoirId] IS NOT NULL AND [VoicePart] IS NOT NULL\r\n    AND [SongId] IS NULL AND [EventId] IS NULL)\r\nOR ([Scope] = 2 AND [SongId] IS NOT NULL\r\n    AND [EventId] IS NULL AND [ChoirId] IS NULL)\r\nOR ([Scope] = 3 AND [EventId] IS NOT NULL\r\n    AND [SongId] IS NULL AND [VoicePart] IS NULL AND [ChoirId] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Instructions_Choirs_ChoirId",
                table: "Instructions",
                column: "ChoirId",
                principalTable: "Choirs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Instructions_Events_EventId",
                table: "Instructions",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
