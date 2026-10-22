using CleanArchitectureBase.Application.Comments.Dto;
using CleanArchitectureBase.Domain.Entities;

namespace CleanArchitectureBase.Application.Common.Mappings;

public class AutoMapperProfile : Profile
{
    public AutoMapperProfile()
    {
        CreateMap<Comment, CommentDto>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id))
            .ForMember(dest => dest.ParentId, opt => opt.MapFrom(src => src.ParentId))
            .ForMember(dest => dest.Content, opt => opt.MapFrom(src => src.Content))
            .ForMember(dest => dest.CreatedAt, opt => opt.MapFrom(src => src.Created))
            .ForMember(dest => dest.ReplyCount, opt => opt.MapFrom(src => src.ChildComments != null ? src.ChildComments.Count : 0))
            .ForMember(dest => dest.ChildComments, opt => opt.MapFrom(src => src.ChildComments))
            .ForMember(dest => dest.CreateBy, opt => opt.MapFrom(src => new UserCreateCommentDto
            {
                UserId = src.CreatedByUser!.Id, 
                FullName = src.CreatedByUser!.FullName,
                AvatarId = src.CreatedByUser!.AvatarMediaId
            }));
    }
}
