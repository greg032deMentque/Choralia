using AutoMapper;
using ChoraleBackEnd.Data.Entities;

namespace ChoraleBackEnd.ViewModels.SongLists;

public sealed class SongListSongViewModel
{
    public Guid? Id { get; set; }
    public Guid SongListId { get; set; }
    public Guid SongId { get; set; }
    public int Position { get; set; }
    public string? SongTitle { get; set; }
}

public sealed class SongListSongViewModelMappingProfile : Profile
{
    public SongListSongViewModelMappingProfile()
    {
        CreateMap<SongListSong, SongListSongViewModel>()
            .ForMember(dest => dest.SongTitle, opt => opt.MapFrom(src =>
                src.Song != null ? src.Song.Title : null));

        CreateMap<SongListSongViewModel, SongListSong>()
            .ForMember(dest => dest.SongList, opt => opt.Ignore())
            .ForMember(dest => dest.Song, opt => opt.Ignore());
    }
}
