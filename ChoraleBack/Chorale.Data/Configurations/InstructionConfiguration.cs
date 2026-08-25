using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class InstructionConfiguration : IEntityTypeConfiguration<Instruction>
{
    public void Configure(EntityTypeBuilder<Instruction> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.SongId).IsRequired();
        builder.Property(c => c.VoicePart);
        builder.Property(c => c.Status).IsRequired();
        builder.Property(c => c.Title).HasMaxLength(200);
        builder.Property(c => c.Content).IsRequired();

        builder.HasOne(c => c.Song)
            .WithMany()
            .HasForeignKey(c => c.SongId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Author)
            .WithMany()
            .HasForeignKey(c => c.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            // CK_Instruction_Scope a disparu avec les portees chorale/pupitre/evenement : une
            // cible unique et obligatoire (SongId, garantie par la FK) n'a plus d'exclusivite a
            // faire respecter.

            // 1 = InstructionStatusEnum.Published. Une consigne publiee porte sa date.
            t.HasCheckConstraint("CK_Instruction_Published",
                "[Status] <> 1 OR [PublishedAt] IS NOT NULL");
        });

        // `AND [IsDeleted] = 0` : convention du projet — les lignes soft-deletees ne
        // gonflent pas les index de lecture. SongId n'est plus nullable, le filtre
        // `IS NOT NULL` d'origine n'a donc plus d'objet.
        builder.HasIndex(c => c.SongId)
            .HasFilter("[IsDeleted] = 0");

        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}
