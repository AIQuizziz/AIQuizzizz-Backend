using System.Reflection;
using CleanArchitectureBase.Application.Common.Interfaces;
using CleanArchitectureBase.Domain.Entities;
using System.Linq.Dynamic.Core;
using CleanArchitectureBase.Application.Classes.Service;
using CleanArchitectureBase.Application.Common.Exceptions;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Security;
using CleanArchitectureBase.Domain.Constants;

namespace CleanArchitectureBase.Application.Classes;

public class StudentSearchResultDto
{
    public required Guid StudentId { get; set; }
    public string? FullName { get; set; }
    public required string Email { get; set; }
    public string? Position { get; set; }
    public Guid? AvatarId { get; set; }
}

[Authorize]
public class SearchStudentInClassQuery : IRequest<PaginatedList<StudentSearchResultDto>>
{
    /// <summary>
    /// Id of the class want to retrieve students
    /// </summary>
    public required Guid ClassId { get; set; }
    public string? Keyword { get; set; }
    public string? FieldName { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 5;
}

public class SearchStudentInClassQueryValidator : AbstractValidator<SearchStudentInClassQuery>
{
    public SearchStudentInClassQueryValidator()
    {
        RuleFor(x => x.ClassId)
            .NotEmpty().WithMessage("ClassId không được để trống");
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage("Số trang phải lớn hơn hoặc bằng 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Kích thước trang phải từ 1 đến 100");
    }
}

public class SearchStudentInClassQueryHandler : IRequestHandler<SearchStudentInClassQuery, PaginatedList<StudentSearchResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IClassService _classService;


    public SearchStudentInClassQueryHandler(IApplicationDbContext context, IClassService classService)
    {
        _context = context;
        _classService = classService;
    }
    
    /// <summary>
    /// The function searches for students in a class based on a keyword and field name, returning a paginated list of student details
    /// </summary>
    /// <param name="rq">Request contains ClassId, Keyword, FieldName, PageNumber, and PageSize information</param>
    /// <param name="cancellationToken">Token to cancel the task</param>
    public async Task<PaginatedList<StudentSearchResultDto>> Handle(SearchStudentInClassQuery rq, CancellationToken cancellationToken)
    {
        var classById = await _context.Classes
            .Where(x => x.Id == rq.ClassId)
            .FirstOrDefaultAsync(cancellationToken);
        if (classById == null)
            throw new ErrorCodeException(ErrorCodes.CLASS_NOTFOUND, "Không tìm thấy lớp");
        
        var isUserInClass = await _classService.IsUserInClass(rq.ClassId);
        if (!isUserInClass)
            throw new ErrorCodeException(ErrorCodes.NOT_FOUND_USER_IN_CLASS, "User không có trong lớp");
        
        var listStudent = _context.ClassUsers
            .Include(x => x.User)
            .Where(s => s.ClassId.Equals(rq.ClassId) && s.ShareMode != ClassShareMode.Owner);
        
        if (!string.IsNullOrEmpty(rq.Keyword) && !string.IsNullOrEmpty(rq.FieldName))
        {
            var property = typeof(User).GetProperty(rq.FieldName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != typeof(string))
                throw new ErrorCodeException(ErrorCodes.FIELD_NAME_NOT_FOUND, "Trường tìm kiếm không hợp lệ hoặc không phải kiểu string");

            string query = $"User.{rq.FieldName}.ToLower().Contains(@0)"; 
            listStudent = listStudent.Where(query, rq.Keyword.ToLower()); 
        }
        
        return await PaginatedList<StudentSearchResultDto>.CreateAsync(
            listStudent.Select(st => new StudentSearchResultDto
            {
                StudentId = st.UserId,
                Email = st.User.Email,
                FullName = st.User.FullName,
                AvatarId = st.User.AvatarMediaId,
                Position = st.ShareMode.ToString()
            }).AsQueryable(),
            rq.PageNumber,
            rq.PageSize
        );
    }
}
