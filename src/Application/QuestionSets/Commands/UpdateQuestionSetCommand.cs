    using System.Text.Json.Serialization;
    using CleanArchitectureBase.Application.Common.Exceptions;
    using CleanArchitectureBase.Application.Common.Interfaces;
    using CleanArchitectureBase.Application.Common.Security;
    using CleanArchitectureBase.Application.QuestionSets.Dtos;
    using CleanArchitectureBase.Application.QuestionSets.Services;
    using CleanArchitectureBase.Domain.Constants;
    using CleanArchitectureBase.Application.Questions.Utils;
    using CleanArchitectureBase.Domain.Entities;

    namespace CleanArchitectureBase.Application.QuestionSets.Commands;

    [Authorize]
    public class UpdateQuestionSetCommand : IRequest<Guid>
    {
        [JsonIgnore]
        public Guid QuestionSetId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<CreateUpdateQuestionDto> CreateUpdateQuestions { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public List<Guid> DeleteQuestionIds { get; set; } = new();
    }

    public class UpdateQuestionSetCommandValidator : AbstractValidator<UpdateQuestionSetCommand>
    {
        public UpdateQuestionSetCommandValidator()
        {
            RuleFor(x => x.QuestionSetId)
                .NotEmpty();
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Tên bộ câu hỏi không được để trống")
                .MaximumLength(200).WithMessage("Tên bộ câu hỏi không được vượt quá 200 ký tự");

            RuleFor(x => x.Description)
                .MaximumLength(500).WithMessage("Mô tả không được vượt quá 500 ký tự");

            RuleFor(x => x.CreateUpdateQuestions)
                .Must(q => q != null && q.Count <= 500)
                .WithMessage("Bộ câu hỏi không được vượt quá 500 câu");

            RuleFor(x => x.DeleteQuestionIds)
                .Must(q => q.Count <= 500)
                .WithMessage("Không thể xóa toàn bộ câu hỏi");

            RuleFor(x => x)
                .Must(x => x.CreateUpdateQuestions.Any() || x.DeleteQuestionIds.Any())
                .WithMessage("Phải có ít nhất một câu hỏi được thêm/cập nhật hoặc xóa.");

            RuleForEach(x => x.CreateUpdateQuestions)
                .SetValidator((command, question) => new CreateUpdateQuestionDto.QuestionCreateDtoValidator());

            RuleFor(x => x.Tags)
                .Must(tags => tags.Count <= 10)
                .WithMessage("Không được nhập quá 10 tag.");

            RuleForEach(x => x.Tags)
                .NotEmpty().WithMessage("Tag không được để trống.")
                .MaximumLength(50).WithMessage("Mỗi tag không được vượt quá 50 ký tự.");
        }
    }

    public class UpdateQuestionSetCommandHandler : IRequestHandler<UpdateQuestionSetCommand, Guid>
    {
        private readonly IApplicationDbContext _dbContext;
        private readonly IQuestionSetService _questionSetService;
        private readonly IUser _currentUser;

        public UpdateQuestionSetCommandHandler(
            IApplicationDbContext dbContext,
            IQuestionSetService questionSetService,
            IUser currentUser)
        {
            _dbContext = dbContext;
            _questionSetService = questionSetService;
            _currentUser = currentUser;
        }

        public async Task<Guid> Handle(UpdateQuestionSetCommand request, CancellationToken cancellationToken)
        {
            var questionSet = await _questionSetService.GetActiveQuestionSet(request.QuestionSetId, cancellationToken);
                    
            if (questionSet == null)
                throw new ErrorCodeException(ErrorCodes.QUESTION_SET_NOT_FOUND);
        
            if (!await _questionSetService.CanUserEditQuestionSet(_currentUser.UserId!.Value, questionSet.Id))
                throw new ErrorCodeException(ErrorCodes.COMMON_FORBIDDEN);

            // Cập nhật thông tin
            questionSet.Name = request.Name!;
            questionSet.Description = request.Description!;

            var updateIds = request.CreateUpdateQuestions
                .Where(q => q.QuestionId.HasValue)
                .Select(q => q.QuestionId!.Value)
                .ToList();

            var targetIds = request.DeleteQuestionIds
                .Union(updateIds)
                .ToList();

            var existingQuestions = await _dbContext.Questions
                .Where(q =>
                    q.QuestionSetId == request.QuestionSetId
                    && targetIds.Contains(q.Id)
                    && q.IsDeleted == false
                )
                .ToListAsync(cancellationToken);

            // Xóa các câu hỏi
            var questionsToDelete = existingQuestions
                .Where(q => request.DeleteQuestionIds.Contains(q.Id))
                .ToList();

            var questionCountAdd = 0;

            foreach (var question in questionsToDelete)
            {
                questionCountAdd--;
                question.IsDeleted = true;
            }

            var mediaMap = await _dbContext.ResolveQuestionMediaAsync(request.CreateUpdateQuestions, _currentUser.UserId!.Value,
                cancellationToken);

            // Thêm hoặc cập nhật câu hỏi
            // Vị trí = index trong mảng gửi lên (cả câu cũ lẫn câu mới)
            var questionOrder = 0;
            foreach (var dto in request.CreateUpdateQuestions)
            {
                var position = questionOrder++;
                var type = Enum.Parse<QuestionType>(dto.Type!);
                var dataJson = CreateUpdateQuestionDto.Serializer.Serialize(dto);

                if (dto.QuestionId.HasValue)
                {
                    var existing = existingQuestions.FirstOrDefault(q => q.Id == dto.QuestionId.Value);
                    if (existing != null)
                    {
                        existing.Type = type;
                        existing.QuestionText = dto.QuestionText;
                        existing.Score = dto.Score;
                        existing.TextFormat = TextFormatHelper.Resolve(dto.TextFormat, TextFormat.Html);
                        existing.DataJson = dataJson;
                        existing.Order = position;
                        existing.ExplainText = dto.ExplainText;
                        existing.ApplyMedia(dto, mediaMap);
                    }
                }
                else
                {
                    questionCountAdd++;
                    var newQuestion = new Question
                    {
                        Id = Guid.NewGuid(),
                        QuestionSetId = questionSet.Id,
                        Type = type,
                        QuestionText = dto.QuestionText,
                        TextFormat = TextFormatHelper.Resolve(dto.TextFormat, TextFormat.PlainText),
                        ExplainText = dto.ExplainText,
                        Score = dto.Score,
                        Order = position,
                        DataJson = dataJson
                    };
                    newQuestion.ApplyMedia(dto, mediaMap);
                    _dbContext.Questions.Add(newQuestion);
                }
            }


            var normalizedCreateUpdateTagNames = request.Tags
                .Select(t => t.Trim().ToLower())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();

            // Lấy tag hiện tại của question set
            var currentTagLinks = await _dbContext.QuestionSetTags
                .Where(qt => qt.QuestionSetId == questionSet.Id)
                .Include(qt => qt.Tag)
                .ToListAsync(cancellationToken);

            var currentTagNames = currentTagLinks
                .Select(qt => qt.Tag!.Name.ToLower())
                .ToList();

            // Lấy tag đã tồn tại
            var existingTags = await _dbContext.Tags
                .Where(t => normalizedCreateUpdateTagNames.Contains(t.Name.ToLower()))
                .ToListAsync(cancellationToken);

            // Xóa tag cũ không còn dùng
            foreach (var tagLink in currentTagLinks)
            {
                if (!normalizedCreateUpdateTagNames.Contains(tagLink.Tag!.Name.ToLower()))
                {
                    tagLink.Tag.QuestionSetCount--;
                    _dbContext.QuestionSetTags.Remove(tagLink);
                }
            }

            // Thêm tag mới
            foreach (var tagName in normalizedCreateUpdateTagNames)
            {
                if (currentTagNames.Contains(tagName))
                    continue; // Đã tồn tại

                var tag = existingTags.FirstOrDefault(t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
                if (tag == null)
                {
                    tag = new Tag
                    {
                        Id = Guid.NewGuid(),
                        Name = tagName,
                        QuestionSetCount = 1
                    };
                    _dbContext.Tags.Add(tag);
                }
                else
                {
                    tag.QuestionSetCount++;
                }

                _dbContext.QuestionSetTags.Add(new QuestionSetTag
                {
                    QuestionSetId = questionSet.Id,
                    TagId = tag.Id
                });
            }

            questionSet.QuestionCount += questionCountAdd;
            
            if(questionSet.QuestionCount > 500)
            {
                throw new ErrorCodeException(ErrorCodes.NUMBER_OF_QUESTION_EXCEED_LIMIT, "Vượt quá 500 câu hỏi");
            }
            
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Log ra các entity không còn tồn tại hoặc bị lỗi
                foreach (var entry in ex.Entries)
                {
                    Console.WriteLine($"Concurrency issue on entity: {entry.Entity.GetType().Name}, state: {entry.State}");
                }

                throw new ErrorCodeException("Dữ liệu đã bị thay đổi hoặc xóa. Vui lòng tải lại trang và thử lại.");
            }
            return request.QuestionSetId;
        }
        
        
    }
